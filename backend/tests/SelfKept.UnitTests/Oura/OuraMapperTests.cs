using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Services.Oura;
using Xunit;

namespace SelfKept.UnitTests.Oura;

/// <summary>Documents shaped like Oura API v2 (OpenAPI 1.43) responses, mapped to ingest samples.</summary>
public class OuraMapperTests
{
  private static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement.Clone();

  [Fact]
  public void DailyReadiness_GivesTheScoreAndTheTemperatureDeviation_AtNoonUtc()
  {
    var samples = OuraMapper.Map("daily_readiness", Doc("""
      {"id":"r1","contributors":{"hrv_balance":80},"day":"2026-10-28","score":72,"temperature_deviation":0.31,"temperature_trend_deviation":0.1,"timestamp":"2026-10-28T00:00:00+00:00"}
      """));

    samples.Should().HaveCount(2);
    var score = samples.Single(s => s.Metric == MetricKeys.OuraReadiness);
    score.Value.Should().Be(72);
    score.Unit.Should().Be("score");
    score.Start.Should().Be(new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc));
    score.SourceUid.Should().Be("oura:daily_readiness:r1");
    var temp = samples.Single(s => s.Metric == MetricKeys.WristTempDeltaC);
    temp.Value.Should().Be(0.31);
    temp.Meta!.Value.GetProperty("origin").GetString().Should().Be(OuraMapper.Origin);
    temp.Meta!.Value.GetProperty("kind").GetString().Should().Be("delta");
  }

  [Fact]
  public void DailyScores_WithANullScore_GiveNothing()
  {
    OuraMapper.Map("daily_sleep", Doc("""{"id":"s1","contributors":{},"day":"2026-10-28","score":null,"timestamp":"x"}""")).Should().BeEmpty();
  }

  [Theory]
  [InlineData("limited", 1)]
  [InlineData("solid", 3)]
  [InlineData("exceptional", 5)]
  public void DailyResilience_MapsTheLevelToOneToFive(string level, int expected)
  {
    var sample = OuraMapper.Map("daily_resilience", Doc($$"""{"id":"x","day":"2026-10-28","contributors":{"sleep_recovery":50,"daytime_recovery":40,"stress":30},"level":"{{level}}"}""")).Single();
    sample.Metric.Should().Be("oura_resilience");
    sample.Value.Should().Be(expected);
  }

  [Fact]
  public void DailyStressAndSpo2_MapTheirFields()
  {
    OuraMapper.Map("daily_stress", Doc("""{"id":"x","day":"2026-10-28","day_summary":"normal","recovery_high":3600,"stress_high":5400}"""))
      .Single().Should().Match<Core.DTOs.SampleInput>(s => s.Metric == "oura_stress_high_s" && s.Value == 5400 && s.Unit == "s");
    OuraMapper.Map("daily_spo2", Doc("""{"id":"x","day":"2026-10-28","breathing_disturbance_index":2,"spo2_percentage":{"average":96.43}}"""))
      .Single().Should().Match<Core.DTOs.SampleInput>(s => s.Metric == MetricKeys.Spo2Pct && s.Value == 96.4 && s.Unit == "%");
    OuraMapper.Map("daily_spo2", Doc("""{"id":"x","day":"2026-10-28","spo2_percentage":null}""")).Should().BeEmpty();
  }

  private const string Night = """
    {
      "id": "n1", "day": "2026-10-28", "type": "long_sleep",
      "bedtime_start": "2026-10-27T23:00:00+01:00", "bedtime_end": "2026-10-28T00:00:00+01:00",
      "sleep_phase_5_min": "442221113334",
      "average_breath": 14.25, "lowest_heart_rate": 48, "time_in_bed": 3600, "period": 0, "low_battery_alert": false,
      "hrv": {"interval": 300.0, "items": [40.0, null, 52.0], "timestamp": "2026-10-27T23:05:00.000+01:00"},
      "heart_rate": {"interval": 300.0, "items": [58.0, 55.0], "timestamp": "2026-10-27T23:05:00.000+01:00"}
    }
    """;

  [Fact]
  public void SleepPeriod_MergesRunsOfTheSamePhaseIntoOneStageSample()
  {
    var stages = OuraMapper.Map("sleep", Doc(Night)).Where(s => s.Metric == MetricKeys.SleepStage).ToList();

    // 44 222 111 333 4 -> awake 10 min, light 15, deep 15, REM 15, awake 5
    stages.Select(s => s.Value).Should().Equal(SleepStages.Awake, SleepStages.Light, SleepStages.Deep, SleepStages.Rem, SleepStages.Awake);
    stages[0].Start.Should().Be(new DateTime(2026, 10, 27, 22, 0, 0, DateTimeKind.Utc));
    stages[0].End.Should().Be(new DateTime(2026, 10, 27, 22, 10, 0, DateTimeKind.Utc));
    stages[1].End.Should().Be(new DateTime(2026, 10, 27, 22, 25, 0, DateTimeKind.Utc));
    stages[^1].End.Should().Be(new DateTime(2026, 10, 27, 23, 0, 0, DateTimeKind.Utc), "the last run ends at bedtime_end");
    stages.Select(s => s.SourceUid).Should().OnlyHaveUniqueItems();
  }

  [Fact]
  public void SleepPeriod_GivesHrvAndHeartRateSeries_SkippingNulls_AndBreathingAndRestingHeartRate()
  {
    var samples = OuraMapper.Map("sleep", Doc(Night));

    var hrv = samples.Where(s => s.Metric == MetricKeys.HrvRmssdMs).ToList();
    hrv.Select(s => s.Value).Should().Equal(40, 52);
    hrv[1].Start.Should().Be(new DateTime(2026, 10, 27, 22, 15, 0, DateTimeKind.Utc), "item 2 is 600 s after the first timestamp");
    samples.Count(s => s.Metric == MetricKeys.HeartRateBpm).Should().Be(2);
    samples.Single(s => s.Metric == MetricKeys.RespiratoryRateBpm).Value.Should().Be(14.2);
    var rhr = samples.Single(s => s.Metric == MetricKeys.RestingHrBpm);
    rhr.Value.Should().Be(48);
    rhr.Start.Should().Be(new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc));
    samples.Should().OnlyContain(s => s.Meta!.Value.GetProperty("origin").GetString() == OuraMapper.Origin);
  }

  [Fact]
  public void SleepPeriod_AnAfternoonNap_HasNoRestingHeartRate_AndRestPeriodsAreSkipped()
  {
    var nap = Night.Replace("\"long_sleep\"", "\"late_nap\"");
    OuraMapper.Map("sleep", Doc(nap)).Should().NotContain(s => s.Metric == MetricKeys.RestingHrBpm);

    var rest = Night.Replace("\"long_sleep\"", "\"rest\"");
    OuraMapper.Map("sleep", Doc(rest)).Should().BeEmpty();
  }

  [Fact]
  public void DocumentWithoutAnId_GivesNothing()
  {
    OuraMapper.Map("daily_sleep", Doc("""{"day":"2026-10-28","score":80}""")).Should().BeEmpty();
  }
}

public class OuraSettingsTests
{
  private static IConfiguration Config(params (string Key, string Value)[] values) =>
    new ConfigurationBuilder()
      .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

  [Fact]
  public void From_BlankValuesAsShippedInAppsettings_AreTreatedAsUnset()
  {
    var settings = OuraSettings.From(Config(
      ("Mcp:Issuer", ""), ("App:BaseUrl", "https://box.tail1.ts.net/"), ("Oura:RedirectUri", ""),
      ("Oura:ClientId", "id"), ("Oura:ClientSecret", "secret")));

    settings.EnvironmentApp.Should().Be(new OuraApp("id", "secret"));
    settings.RedirectUri.Should().Be("https://box.tail1.ts.net/api/oura/callback");
    settings.AppBaseUrl.Should().Be("https://box.tail1.ts.net");
  }

  [Fact]
  public void From_WithoutCredentials_IsOff()
  {
    OuraSettings.From(Config(("App:BaseUrl", "https://x.example"), ("Oura:ClientId", " "), ("Oura:ClientSecret", "s"))).EnvironmentApp.Should().BeNull();
  }
}
