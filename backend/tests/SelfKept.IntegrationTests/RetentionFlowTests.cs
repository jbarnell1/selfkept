using System.Globalization;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Data;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// Sample retention end to end (docs/retention-design.md): the admin setting, a run that folds an
/// old month of heart rate, and every reader afterwards. Runs on InMemory or, with
/// SELFKEPT_TEST_CONNECTION, on Postgres, where it also checks the partition was rewritten.
/// </summary>
public class RetentionFlowTests
{
  private static async Task<(HttpClient Ada, Guid AdaPerson, HttpClient Bob, Guid BobPerson)> HouseholdAsync(ApiFactory factory)
  {
    var suffix = Guid.NewGuid().ToString("N")[..8];
    var ada = factory.CreateClient();
    var adaAuth = await RegisterAsync(ada, $"ada-{suffix}@example.com", "Ada");
    var invite = await (await ada.PostJsonAsync("/api/household/invites", new { })).ReadJsonAsync();
    var bob = factory.CreateClient();
    var bobAuth = await RegisterAsync(bob, $"bob-{suffix}@example.com", "Bob", invite.GetProperty("code").GetString());
    return (ada, PersonId(adaAuth), bob, PersonId(bobAuth));
  }

  private static async Task<JsonElement> RegisterAsync(HttpClient client, string email, string name, string? inviteCode = null)
  {
    var response = await client.PostJsonAsync("/api/auth/register", new { email, password = "correct horse battery staple", displayName = name, inviteCode });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    var auth = await response.ReadJsonAsync();
    client.UseBearer(auth.GetProperty("accessToken").GetString()!);
    return auth;
  }

  private static Guid PersonId(JsonElement auth) => auth.GetProperty("user").GetProperty("person").GetProperty("id").GetGuid();

  private static object Sample(string uid, string metric, DateTime start, double value, string unit, DateTime? end = null) =>
    new { sourceUid = uid, metric, start = Iso(start), end = end == null ? null : Iso(end.Value), value, unit };

  private static string Iso(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

  private static async Task<Guid> SourceAsync(HttpClient client, string deviceName) =>
    (await (await client.PostJsonAsync("/api/sources", new { kind = "health_connect", deviceName, platform = "android" })).ReadJsonAsync()).GetProperty("id").GetGuid();

  private static async Task<JsonElement> IngestAsync(HttpClient client, Guid sourceId, params object[] samples)
  {
    var response = await client.PostJsonAsync("/api/ingest/batch", new { sourceId, samples });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    return await response.ReadJsonAsync();
  }

  private static async Task<JsonElement> RunAsync(HttpClient client)
  {
    var response = await client.PostAsync("/api/retention/run", null);
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    return await response.ReadJsonAsync();
  }

  private static async Task<JsonElement> DayAsync(HttpClient client, DateOnly day)
  {
    var series = await (await client.GetAsync($"/api/data/daily?metric=heart_rate_bpm&from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}")).ReadJsonAsync();
    return series.GetProperty("days").EnumerateArray().Single();
  }

  [Fact]
  public async Task Setting_OnlyTheAdmin_ReadsAndChangesIt_WithinTheFloor_Audited()
  {
    await using var factory = new ApiFactory();
    var (ada, _, bob, _) = await HouseholdAsync(factory);

    (await bob.GetAsync("/api/retention")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await bob.PutJsonAsync("/api/retention", new { rawMonths = 24 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await bob.PostAsync("/api/retention/run", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

    var initial = await (await ada.GetAsync("/api/retention")).ReadJsonAsync();
    initial.GetProperty("rawMonths").GetInt32().Should().Be(12);
    initial.GetProperty("source").GetString().Should().Be("default");
    initial.GetProperty("minimumMonths").GetInt32().Should().Be(4);
    initial.GetProperty("months").GetArrayLength().Should().Be(0);
    var today = DateTime.UtcNow;
    initial.GetProperty("rawSince").GetString().Should().Be(new DateOnly(today.Year, today.Month, 1).AddMonths(-12).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    (await ada.PutJsonAsync("/api/retention", new { rawMonths = 3 })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "three months is under the floor");
    (await ada.PutJsonAsync("/api/retention", new { rawMonths = 601 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var forever = await (await ada.PutJsonAsync("/api/retention", new { rawMonths = 0 })).ReadJsonAsync();
    forever.GetProperty("rawMonths").GetInt32().Should().Be(0);
    forever.GetProperty("source").GetString().Should().Be("app");
    forever.TryGetProperty("rawSince", out var since).Should().BeTrue();
    since.ValueKind.Should().Be(JsonValueKind.Null);
    (await RunAsync(ada)).GetProperty("folded").GetArrayLength().Should().Be(0, "nothing is folded while raw is kept for ever");

    var back = await (await ada.PutJsonAsync("/api/retention", new { rawMonths = 18 })).ReadJsonAsync();
    back.GetProperty("rawMonths").GetInt32().Should().Be(18);

    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SelfKeptDbContext>();
    var changes = await db.AuditLogs.AsNoTracking().Where(a => a.Action == AuditActions.RetentionChanged).OrderBy(a => a.OccurredAt).ToListAsync();
    changes.Should().HaveCount(2);
    static (int From, int To) Change(AuditLog row)
    {
      using var details = JsonDocument.Parse(row.Details!);
      return (details.RootElement.GetProperty("from").GetInt32(), details.RootElement.GetProperty("to").GetInt32());
    }
    Change(changes[0]).Should().Be((12, 0));
    Change(changes[1]).Should().Be((0, 18));
  }

  [Fact]
  public async Task Run_FoldsAnOldMonthOfHeartRate_AndEveryReaderStillSeesIt()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, bob, _) = await HouseholdAsync(factory);
    var watch = await SourceAsync(ada, "Pixel Watch");

    // A day twenty months back (folded under the default 12) and one two days back (kept raw).
    var thisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    var oldNoon = thisMonth.AddMonths(-20).AddDays(9).AddHours(12);
    var oldDay = DateOnly.FromDateTime(oldNoon);
    var recent = DateTime.UtcNow.Date.AddDays(-2).AddHours(12);
    var batch = new[]
    {
      Sample("hr-1", "heart_rate_bpm", oldNoon, 60, "bpm"),
      Sample("hr-2", "heart_rate_bpm", oldNoon.AddSeconds(20), 63, "bpm"),
      Sample("hr-3", "heart_rate_bpm", oldNoon.AddSeconds(40), 66, "bpm"),
      Sample("hr-4", "heart_rate_bpm", oldNoon.AddSeconds(70), 70, "bpm"),
      Sample("sl-1", "sleep_stage", oldNoon.AddHours(-10), 3, "stage", oldNoon.AddHours(-9)),
      Sample("hr-5", "heart_rate_bpm", recent, 58, "bpm"),
    };
    (await IngestAsync(ada, watch, batch)).GetProperty("accepted").GetInt32().Should().Be(6);
    var before = await DayAsync(ada, oldDay);
    before.GetProperty("value").GetDouble().Should().BeApproximately(64.75, 1e-9);

    var run = await RunAsync(ada);
    var folded = run.GetProperty("folded").EnumerateArray().Single();
    folded.GetProperty("month").GetString().Should().Be(new DateOnly(oldDay.Year, oldDay.Month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    folded.GetProperty("rawRows").GetInt64().Should().Be(4);
    folded.GetProperty("minuteRows").GetInt64().Should().Be(2);
    folded.GetProperty("compacted").GetBoolean().Should().BeTrue();

    (await RunAsync(ada)).GetProperty("folded").GetArrayLength().Should().Be(0, "a second run has nothing left to fold");

    // The day is untouched: it was computed before, and nothing recomputed it from fewer rows.
    var after = await DayAsync(ada, oldDay);
    after.GetProperty("value").GetDouble().Should().BeApproximately(64.75, 1e-9);
    after.GetProperty("count").GetInt32().Should().Be(4);

    // The samples page shows the minutes, newest first, with their range; paging works across them.
    var from = Iso(oldNoon.AddHours(-1));
    var to = Iso(oldNoon.AddHours(1));
    var page = await (await ada.GetAsync($"/api/data/samples?metric=heart_rate_bpm&from={from}&to={to}")).ReadJsonAsync();
    var items = page.GetProperty("items").EnumerateArray().ToList();
    items.Should().HaveCount(2);
    items[0].GetProperty("value").GetDouble().Should().Be(70);
    items[0].GetProperty("meta").GetProperty("rollup").GetProperty("count").GetInt32().Should().Be(1);
    items[1].GetProperty("value").GetDouble().Should().Be(63);
    items[1].GetProperty("meta").GetProperty("rollup").GetProperty("min").GetDouble().Should().Be(60);
    items[1].GetProperty("meta").GetProperty("rollup").GetProperty("max").GetDouble().Should().Be(66);
    var first = await (await ada.GetAsync($"/api/data/samples?metric=heart_rate_bpm&from={from}&to={to}&limit=1")).ReadJsonAsync();
    var cursor = first.GetProperty("nextCursor").GetString()!;
    var second = await (await ada.GetAsync($"/api/data/samples?metric=heart_rate_bpm&from={from}&to={to}&limit=1&cursor={Uri.EscapeDataString(cursor)}")).ReadJsonAsync();
    second.GetProperty("items")[0].GetProperty("value").GetDouble().Should().Be(63);

    // A metric that is not dense keeps its raw rows in the folded month.
    var sleep = await (await ada.GetAsync($"/api/data/samples?metric=sleep_stage&from={Iso(oldNoon.AddDays(-1))}&to={to}")).ReadJsonAsync();
    sleep.GetProperty("items").GetArrayLength().Should().Be(1);
    sleep.GetProperty("items")[0].TryGetProperty("meta", out _).Should().BeFalse();

    // The overview still counts every reading and starts where the history starts.
    var overview = await (await ada.GetAsync("/api/data/overview")).ReadJsonAsync();
    var hr = overview.GetProperty("metrics").EnumerateArray().Single(m => m.GetProperty("metric").GetString() == "heart_rate_bpm");
    hr.GetProperty("count").GetInt64().Should().Be(5);
    hr.GetProperty("firstAt").GetDateTime().ToUniversalTime().Should().Be(oldNoon);
    hr.GetProperty("lastValue").GetDouble().Should().Be(58);

    // Bob has no vitals grant: the folded minutes are as hidden as the raw rows.
    (await bob.GetAsync($"/api/data/samples?metric=heart_rate_bpm&personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

    // The watch sending the same readings again stores nothing.
    var resent = await IngestAsync(ada, watch, batch);
    resent.GetProperty("accepted").GetInt32().Should().Be(0);
    resent.GetProperty("duplicates").GetInt32().Should().Be(6);

    // A phone never seen before reads that old day too: kept, and the day counts it with the minutes.
    var phone = await SourceAsync(ada, "Pixel 10");
    (await IngestAsync(ada, phone, Sample("ph-1", "heart_rate_bpm", oldNoon.AddSeconds(30), 80, "bpm"))).GetProperty("accepted").GetInt32().Should().Be(1);
    var withPhone = await DayAsync(ada, oldDay);
    withPhone.GetProperty("count").GetInt32().Should().Be(5);
    withPhone.GetProperty("value").GetDouble().Should().BeApproximately((60 + 63 + 66 + 70 + 80) / 5.0, 1e-9);
    withPhone.GetProperty("max").GetDouble().Should().Be(80);

    // The next run folds the phone's reading into its own minute.
    var again = (await RunAsync(ada)).GetProperty("folded").EnumerateArray().Single();
    again.GetProperty("rawRows").GetInt64().Should().Be(1);
    again.GetProperty("minuteRows").GetInt64().Should().Be(1);
    var setting = await (await ada.GetAsync("/api/retention")).ReadJsonAsync();
    var month = setting.GetProperty("months").EnumerateArray().Single();
    month.GetProperty("rawRows").GetInt64().Should().Be(5);
    month.GetProperty("minuteRows").GetInt64().Should().Be(3);
    (await DayAsync(ada, oldDay)).GetProperty("count").GetInt32().Should().Be(5);

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<SelfKeptDbContext>();
      var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == AuditActions.RetentionRollup).ToListAsync();
      audits.Should().HaveCount(2);
      audits.Should().OnlyContain(a => a.TargetType == "sample_month" && a.Details!.Contains("\"rawRows\""));
      audits.Should().NotContain(a => a.Details!.Contains("avg") || a.Details!.Contains("value"), "no health values go to the audit log");
    }

    if (!factory.UsesPostgres) return;
    await using var connection = new NpgsqlConnection(factory.ScratchConnection);
    await connection.OpenAsync();
    var partition = $"sample_{oldDay:yyyyMM}";
    await using (var cmd = new NpgsqlCommand($"SELECT metric_key FROM {partition}", connection))
    {
      (await cmd.ExecuteScalarAsync()).Should().Be("sleep_stage", "only the sparse rows are left in the folded partition");
    }
    await using (var cmd = new NpgsqlCommand("SELECT count(*) FROM sample WHERE metric_key = 'heart_rate_bpm'", connection))
    {
      (await cmd.ExecuteScalarAsync()).Should().Be(1L, "the recent reading stays raw");
    }
    await using (var cmd = new NpgsqlCommand("SELECT compacted_at IS NOT NULL FROM sample_rollup_month", connection))
    {
      (await cmd.ExecuteScalarAsync()).Should().Be(true);
    }
  }

  /// <summary>
  /// A realistic month (a watch at one reading every two seconds for ten days) on real Postgres:
  /// the disk space comes back, a busy partition is left for the next run, and a second run
  /// while one holds the lock is refused.
  /// </summary>
  [PostgresFact]
  public async Task Run_OnPostgres_GivesTheSpaceBack_RetriesABusyPartition_AndRefusesASecondRun()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, _, _) = await HouseholdAsync(factory);
    var watch = await SourceAsync(ada, "Pixel Watch");
    var thisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    var month = thisMonth.AddMonths(-18);
    var partition = $"sample_{month:yyyyMM}";
    // One sleep reading through the API so a sparse row shares the month; the bulk goes in by SQL.
    await IngestAsync(ada, watch, Sample("sl-1", "sleep_stage", month.AddDays(3), 3, "stage", month.AddDays(3).AddHours(1)));

    await using var connection = new NpgsqlConnection(factory.ScratchConnection);
    await connection.OpenAsync();
    await using (var fill = new NpgsqlCommand($"""
      INSERT INTO sample (id, person_id, source_id, source_uid, metric_key, start_at, "value", unit)
      SELECT gen_random_uuid(), @person, @source, 'bulk-' || n, 'heart_rate_bpm', @start + n * interval '2 seconds', 50 + (n % 40), 'bpm'
      FROM generate_series(0, 431999) AS n
      """, connection))
    {
      fill.Parameters.AddWithValue("person", adaPerson);
      fill.Parameters.AddWithValue("source", watch);
      fill.Parameters.AddWithValue("start", month);
      (await fill.ExecuteNonQueryAsync()).Should().Be(432_000);
    }
    async Task<long> SizeAsync()
    {
      await using var cmd = new NpgsqlCommand($"SELECT pg_total_relation_size('{partition}')", connection);
      return (long)(await cmd.ExecuteScalarAsync())!;
    }
    var before = await SizeAsync();

    // Another session reading the partition holds a lock VACUUM FULL must wait for.
    await using (var reader = new NpgsqlConnection(factory.ScratchConnection))
    {
      await reader.OpenAsync();
      await using var tx = await reader.BeginTransactionAsync();
      await using (var hold = new NpgsqlCommand($"SELECT count(*) FROM {partition}", reader, tx)) await hold.ExecuteScalarAsync();

      var busy = (await RunAsync(ada)).GetProperty("folded").EnumerateArray().Single();
      busy.GetProperty("rawRows").GetInt64().Should().Be(432_000);
      busy.GetProperty("minuteRows").GetInt64().Should().Be(14_400, "ten days of minutes");
      busy.GetProperty("compacted").GetBoolean().Should().BeFalse("the partition was busy past the lock timeout");
      await tx.RollbackAsync();
    }
    // Not compacted yet. (The size is not compared here: autovacuum may trim the emptied tail.)
    await using (var cmd = new NpgsqlCommand("SELECT compacted_at IS NULL FROM sample_rollup_month", connection))
    {
      (await cmd.ExecuteScalarAsync()).Should().Be(true);
    }

    var retry = await RunAsync(ada);
    retry.GetProperty("folded").GetArrayLength().Should().Be(0);
    retry.GetProperty("compacted").EnumerateArray().Select(e => e.GetString()).Should().Equal(month.ToString("yyyy-MM", CultureInfo.InvariantCulture));
    var after = await SizeAsync();
    after.Should().BeLessThan(before / 20, $"the partition went from {before:N0} to {after:N0} bytes");

    await using (var cmd = new NpgsqlCommand($"SELECT count(*), sum(count), min(min), max(max) FROM sample_minute", connection))
    await using (var reader = await cmd.ExecuteReaderAsync())
    {
      await reader.ReadAsync();
      reader.GetInt64(0).Should().Be(14_400);
      reader.GetInt64(1).Should().Be(432_000, "every reading is counted in a minute");
      reader.GetDouble(2).Should().Be(50);
      reader.GetDouble(3).Should().Be(89);
    }
    await using (var cmd = new NpgsqlCommand($"SELECT metric_key FROM {partition}", connection))
    {
      (await cmd.ExecuteScalarAsync()).Should().Be("sleep_stage");
    }

    // A run already going (here, this session holding its lock) turns a second one away.
    await using (var cmd = new NpgsqlCommand("SELECT pg_advisory_lock(hashtext('sample_retention'))", connection)) await cmd.ExecuteScalarAsync();
    (await ada.PostAsync("/api/retention/run", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    await using (var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtext('sample_retention'))", connection)) await cmd.ExecuteScalarAsync();
    (await ada.PostAsync("/api/retention/run", null)).StatusCode.Should().Be(HttpStatusCode.OK);
  }
}
