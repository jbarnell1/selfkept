using System.Net;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>Runs only when SELFKEPT_TEST_CONNECTION points at a scratch Postgres; skips otherwise.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
  public PostgresFactAttribute()
  {
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiFactory.TestConnectionEnvVar)))
    {
      Skip = $"{ApiFactory.TestConnectionEnvVar} is not set; set it to a scratch Postgres connection string to run this test.";
    }
  }
}

/// <summary>
/// The real thing: the unnest + ON CONFLICT insert into the partitioned sample table, partitions
/// created per month, and the (sourceId, sourceUid) idempotency key enforced by the database.
/// </summary>
public class PostgresIngestTests
{
  private static object Sample(string uid, string metric, string start, double value, string unit, string? end = null) =>
    new { sourceUid = uid, metric, start, end, value, unit };

  [PostgresFact]
  public async Task Batch_PostedTwiceOnPostgres_DuplicatesEqualAccepted_PartitionsPerMonth_CountIsRight()
  {
    await using var factory = new ApiFactory();
    factory.UsesPostgres.Should().BeTrue();
    var client = factory.CreateClient();
    var registered = await client.PostJsonAsync("/api/auth/register", new { email = "pg@example.com", password = "correct horse battery staple", displayName = "Pg" });
    registered.StatusCode.Should().Be(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());
    client.UseBearer((await registered.ReadJsonAsync()).GetProperty("accessToken").GetString()!);
    var sourceId = (await (await client.PostJsonAsync("/api/sources", new { kind = "healthkit", deviceName = "iPhone", platform = "ios" })).ReadJsonAsync()).GetProperty("id").GetGuid();

    // Three samples across two months, so two partitions must be created on the fly.
    var batch = new
    {
      sourceId,
      cursor = "anchor-9",
      samples = new[]
      {
        Sample("hk-1", "steps", "2026-09-30T23:50:00Z", 100, "count", "2026-09-30T23:59:00Z"),
        Sample("hk-2", "heart_rate_bpm", "2026-10-01T06:00:00Z", 58, "bpm"),
        Sample("hk-3", "heart_rate_bpm", "2026-10-01T06:05:00Z", 60, "bpm"),
      },
    };

    var first = await (await client.PostJsonAsync("/api/ingest/batch", batch)).ReadJsonAsync();
    first.GetProperty("accepted").GetInt32().Should().Be(3);
    first.GetProperty("duplicates").GetInt32().Should().Be(0);

    var second = await (await client.PostJsonAsync("/api/ingest/batch", batch)).ReadJsonAsync();
    second.GetProperty("accepted").GetInt32().Should().Be(0);
    second.GetProperty("duplicates").GetInt32().Should().Be(first.GetProperty("accepted").GetInt32());

    var source = (await (await client.GetAsync("/api/sources")).ReadJsonAsync()).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == sourceId);
    source.GetProperty("sampleCount").GetInt64().Should().Be(3);
    source.GetProperty("cursor").GetString().Should().Be("anchor-9");

    // Keyset paging goes through the Guid tie-break translation on real SQL.
    var page = await client.GetAsync("/api/data/samples?metric=heart_rate_bpm&limit=1");
    page.StatusCode.Should().Be(HttpStatusCode.OK, await page.Content.ReadAsStringAsync());
    var cursor = (await page.ReadJsonAsync()).GetProperty("nextCursor").GetString()!;
    var next = await client.GetAsync($"/api/data/samples?metric=heart_rate_bpm&limit=1&cursor={Uri.EscapeDataString(cursor)}");
    next.StatusCode.Should().Be(HttpStatusCode.OK, await next.Content.ReadAsStringAsync());
    (await next.ReadJsonAsync()).GetProperty("items")[0].GetProperty("value").GetDouble().Should().Be(58);

    await using var connection = new NpgsqlConnection(factory.ScratchConnection);
    await connection.OpenAsync();
    await using (var partitions = new NpgsqlCommand("SELECT string_agg(c.relname, ',' ORDER BY c.relname) FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid WHERE i.inhparent = 'sample'::regclass", connection))
    {
      (await partitions.ExecuteScalarAsync()).Should().Be("sample_202609,sample_202610");
    }
    await using (var rows = new NpgsqlCommand("SELECT count(*) FROM sample WHERE source_id = @s", connection))
    {
      rows.Parameters.AddWithValue("s", sourceId);
      (await rows.ExecuteScalarAsync()).Should().Be(3L);
    }
    await using (var summaries = new NpgsqlCommand("SELECT count(*) FROM daily_summary", connection))
    {
      (await summaries.ExecuteScalarAsync()).Should().Be(2L, "one steps day and one heart rate day");
    }
  }
}
