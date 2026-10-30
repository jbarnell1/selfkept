using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// The interpret pipeline on real Postgres: 30 days of daily_summary, RunForPersonAsync, the
/// baseline rows, the insight outcome (a real card or the learning card) and GET /api/today.
/// </summary>
public class PostgresInterpretTests
{
  [PostgresFact]
  public async Task ThirtyDaysOfSummaries_RunForPerson_WritesBaselinesAndInsights_AndTodayAnswers()
  {
    await using var factory = new ApiFactory();
    factory.UsesPostgres.Should().BeTrue();
    var client = factory.CreateClient();
    var registered = await client.PostJsonAsync("/api/auth/register", new { email = "pg-interpret@example.com", password = "correct horse battery staple", displayName = "Pg" });
    registered.StatusCode.Should().Be(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());
    var auth = await registered.ReadJsonAsync();
    client.UseBearer(auth.GetProperty("accessToken").GetString()!);
    var personId = auth.GetProperty("user").GetProperty("person").GetProperty("id").GetGuid();

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var yesterday = today.AddDays(-1);
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<SelfKeptDbContext>();
      for (var i = 0; i < 30; i++)
      {
        var day = yesterday.AddDays(-(29 - i));
        context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = "steps", Day = day, Value = 6000 + (i % 6) * 250, Count = 12, ComputedAt = DateTime.UtcNow });
        context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = "resting_hr_bpm", Day = day, Value = 58 + (i % 4), Min = 55, Max = 62, Count = 1, ComputedAt = DateTime.UtcNow });
        context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = "hrv_sdnn_ms", Day = day, Value = i >= 27 ? 25 : 48 + (i % 5) * 2, Min = 40, Max = 60, Count = 1, ComputedAt = DateTime.UtcNow });
        context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = "sleep_stage", Day = day, Value = 450 + (i % 3) * 10, Count = 4, ComputedAt = DateTime.UtcNow });
      }
      context.DailySummaries.Add(new DailySummary { PersonId = personId, MetricKey = "sleep_stage", Day = today, Value = 465, Count = 4, ComputedAt = DateTime.UtcNow });
      await context.SaveChangesAsync();
    }

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var runner = scope.ServiceProvider.GetRequiredService<IInterpretRunner>();
      var result = await runner.RunForPersonAsync(personId, yesterday);
      result.Should().NotBeNull();
      result!.Baselines.Should().Be(4);
      result.Kinds.Should().Contain("hrv_low_run").And.NotContain("learning");
      (await runner.HasRunAsync(personId, yesterday)).Should().BeTrue();
      (await runner.HasRunAsync(personId, yesterday.AddDays(-1))).Should().BeFalse();
    }

    await using var connection = new NpgsqlConnection(factory.ScratchConnection);
    await connection.OpenAsync();
    await using (var baselines = new NpgsqlCommand("SELECT metric_key, n28, n7, median28, p10_28, p90_28 FROM baseline WHERE person_id = @p AND computed_for = @d ORDER BY metric_key", connection))
    {
      baselines.Parameters.AddWithValue("p", personId);
      baselines.Parameters.AddWithValue("d", yesterday);
      var rows = new List<(string Metric, int N28, int N7)>();
      await using var reader = await baselines.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        rows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
        reader.GetDouble(3).Should().BeGreaterThan(0);
        reader.GetDouble(4).Should().BeLessOrEqualTo(reader.GetDouble(5));
      }
      rows.Select(r => r.Metric).Should().Equal("hrv_sdnn_ms", "resting_hr_bpm", "sleep_stage", "steps");
      rows.Should().OnlyContain(r => r.N28 == 28 && r.N7 == 7, "the window is 28 days even when 30 were seeded");
    }
    await using (var insights = new NpgsqlCommand("SELECT kind, severity, metrics, evidence::text, generated FROM insight WHERE person_id = @p AND day = @d", connection))
    {
      insights.Parameters.AddWithValue("p", personId);
      insights.Parameters.AddWithValue("d", yesterday);
      var kinds = new List<string>();
      await using var reader = await insights.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        kinds.Add(reader.GetString(0));
        reader.GetString(1).Should().BeOneOf("notice", "watch", "clinician");
        reader.GetFieldValue<string[]>(2).Should().NotBeNull();
        reader.GetString(3).Should().Contain("bandDistance");
        reader.IsDBNull(4).Should().BeTrue("no model is configured, so generated stays null");
      }
      kinds.Should().Contain("hrv_low_run");
    }
    await using (var calls = new NpgsqlCommand("SELECT count(*) FROM ai_call", connection))
    {
      (await calls.ExecuteScalarAsync()).Should().Be(0L, "Ai:Enabled is false, nothing calls a model");
    }

    var response = await client.GetAsync("/api/today");
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    var body = await response.ReadJsonAsync();
    body.GetProperty("sentence").GetString().Should().NotBeNullOrWhiteSpace();
    body.GetProperty("learning").GetBoolean().Should().BeFalse();
    body.GetProperty("sleepMinutes").GetDouble().Should().Be(465);
    body.GetProperty("cards").GetArrayLength().Should().BeGreaterThan(0);
    body.GetProperty("cards")[0].GetProperty("kind").GetString().Should().Be("hrv_low_run");

    await using (var cache = new NpgsqlCommand("SELECT sentence FROM today_cache WHERE person_id = @p", connection))
    {
      cache.Parameters.AddWithValue("p", personId);
      (await cache.ExecuteScalarAsync()).Should().Be(body.GetProperty("sentence").GetString());
    }

    var trend = await client.GetAsync("/api/trends/resting_hr_bpm");
    trend.StatusCode.Should().Be(HttpStatusCode.OK, await trend.Content.ReadAsStringAsync());
    (await trend.ReadJsonAsync()).GetProperty("band").GetProperty("median28").GetDouble().Should().BeInRange(58, 61);
  }
}
