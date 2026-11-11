using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Services.Collect;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// The one-time recompute after the sleep day moved to 18:00 to 18:00: rows written under the old
/// rule (a night split at midnight) are replaced, a day with no samples is left alone, and once the
/// marker is written a second run changes nothing. Runs on InMemory or, with
/// SELFKEPT_TEST_CONNECTION, on Postgres.
/// </summary>
public class SleepDayRecomputeTests
{
  private static object Sample(string uid, string start, double value, string end) =>
    new { sourceUid = uid, metric = "sleep_stage", start, end, value, unit = "stage" };

  [Fact]
  public async Task Run_OldRuleRows_AreReplaced_AndASecondRunDoesNothing()
  {
    await using var factory = new ApiFactory();
    var ada = factory.CreateClient();
    var registered = await ada.PostJsonAsync("/api/auth/register", new { email = "sleep-day@example.com", password = "correct horse battery staple", displayName = "Ada" });
    registered.StatusCode.Should().Be(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());
    var auth = await registered.ReadJsonAsync();
    ada.UseBearer(auth.GetProperty("accessToken").GetString()!);
    var personId = auth.GetProperty("user").GetProperty("person").GetProperty("id").GetGuid();
    var sourceId = (await (await ada.PostJsonAsync("/api/sources", new { kind = "healthkit", deviceName = "Watch", platform = "ios" })).ReadJsonAsync()).GetProperty("id").GetGuid();
    var batch = new
    {
      sourceId,
      samples = new[]
      {
        Sample("n-1", "2026-11-07T22:30:00Z", 2, "2026-11-07T23:55:00Z"),
        Sample("n-2", "2026-11-07T23:55:00Z", 3, "2026-11-08T06:30:00Z"),
      },
    };
    (await (await ada.PostJsonAsync("/api/ingest/batch", batch)).ReadJsonAsync()).GetProperty("accepted").GetInt32().Should().Be(2);

    // What an install that ran the old rule holds: the night split at midnight, plus a day whose samples are not here.
    await WriteRowsAsync(factory, personId, (new DateOnly(2026, 11, 7), 85), (new DateOnly(2026, 11, 8), 395), (new DateOnly(2026, 10, 1), 400));
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<SelfKeptDbContext>();
      context.ServerSettings.RemoveRange(await context.ServerSettings.Where(s => s.Key == SleepDayRecompute.MarkerKey).ToListAsync());
      await context.SaveChangesAsync();
    }

    var first = await RunAsync(factory);

    first.Should().Be(new SleepDayRecomputeResult(true, 1, 2));
    (await DaysAsync(ada)).Should().Equal(new Dictionary<string, double> { ["2026-10-01"] = 400, ["2026-11-08"] = 480 });

    // Anything written after the marker is no business of the fix: a second run must not touch it.
    await WriteRowsAsync(factory, personId, (new DateOnly(2026, 11, 7), 85));
    var second = await RunAsync(factory);

    second.Ran.Should().BeFalse();
    (await DaysAsync(ada)).Should().ContainKey("2026-11-07");
  }

  private static async Task<SleepDayRecomputeResult> RunAsync(ApiFactory factory)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    return await scope.ServiceProvider.GetRequiredService<SleepDayRecompute>().RunAsync();
  }

  private static async Task WriteRowsAsync(ApiFactory factory, Guid personId, params (DateOnly Day, double Value)[] rows)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<SelfKeptDbContext>();
    foreach (var (day, value) in rows)
    {
      var existing = await context.DailySummaries.SingleOrDefaultAsync(d => d.PersonId == personId && d.MetricKey == MetricKeys.SleepStage && d.Day == day);
      if (existing == null)
        context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = MetricKeys.SleepStage, Day = day, Value = value, Count = 1, ComputedAt = DateTime.UtcNow });
      else
        existing.Value = value;
    }
    await context.SaveChangesAsync();
  }

  private static async Task<Dictionary<string, double>> DaysAsync(HttpClient client)
  {
    var days = (await (await client.GetAsync("/api/data/daily?metric=sleep_stage&from=2026-09-30&to=2026-11-10")).ReadJsonAsync()).GetProperty("days");
    return days.EnumerateArray().ToDictionary(d => d.GetProperty("day").GetString()!, d => d.GetProperty("value").GetDouble());
  }
}
