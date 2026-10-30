using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Data;
using Xunit;

namespace SelfKept.UnitTests.Data;

/// <summary>
/// MetricSeed.cs is the single source of the catalogue; 0004_metric_seed.up.sql is generated from
/// it. Regenerate after editing the list:
///   SELFKEPT_REGENERATE_SEED=1 dotnet test --filter MetricSeedTests
/// </summary>
public class MetricSeedTests
{
  private static readonly string[] ContractKeys =
  [
    "steps", "distance_m", "active_energy_kcal", "basal_energy_kcal", "exercise_min", "flights", "heart_rate_bpm",
    "resting_hr_bpm", "hrv_sdnn_ms", "hrv_rmssd_ms", "spo2_pct", "respiratory_rate_bpm", "wrist_temp_delta_c", "body_temp_c",
    "sleep_stage", "vo2max", "weight_kg", "body_fat_pct", "height_cm", "menstrual_flow", "basal_body_temp_c",
    "blood_glucose_mmol", "blood_pressure_sys", "blood_pressure_dia", "oura_readiness", "oura_sleep_score",
    "oura_activity_score", "oura_resilience", "oura_stress_high_s", "checkin_mood",
  ];

  [Fact]
  public void Seed_ContainsEveryContractKey_AndThirtyLabMarkers()
  {
    var keys = MetricSeed.All.Select(m => m.Key).ToList();

    keys.Should().Contain(ContractKeys);
    keys.Where(k => k.StartsWith("lab.", StringComparison.Ordinal)).Should().HaveCount(30);
    keys.Should().OnlyHaveUniqueItems();
  }

  [Fact]
  public void Seed_EveryRow_IsWellFormed()
  {
    foreach (var m in MetricSeed.All)
    {
      MetricKinds.All.Should().Contain(m.Kind, m.Key);
      GoodDirections.All.Should().Contain(m.GoodDirection, m.Key);
      SexApplicabilities.All.Should().Contain(m.SexApplicability, m.Key);
      m.DisplayName.Should().NotBeNullOrWhiteSpace(m.Key);
      m.Blurb.Should().NotBeNullOrWhiteSpace(m.Key).And.NotContain("\u2014", "no em dashes");
      m.Key.Length.Should().BeLessThanOrEqualTo(64);
      m.Unit.Length.Should().BeLessThanOrEqualTo(32);
      if (m.Key.StartsWith("lab.", StringComparison.Ordinal)) m.Category.Should().Be(ShareCategory.Labs, m.Key);
    }
  }

  [Theory]
  [InlineData("steps", ShareCategory.Activity)]
  [InlineData("sleep_stage", ShareCategory.Sleep)]
  [InlineData("heart_rate_bpm", ShareCategory.Vitals)]
  [InlineData("blood_glucose_mmol", ShareCategory.Vitals)]
  [InlineData("oura_readiness", ShareCategory.Vitals)]
  [InlineData("weight_kg", ShareCategory.Body)]
  [InlineData("vo2max", ShareCategory.Body)]
  [InlineData("menstrual_flow", ShareCategory.Cycle)]
  [InlineData("basal_body_temp_c", ShareCategory.Cycle)]
  [InlineData("checkin_mood", ShareCategory.Checkins)]
  [InlineData("lab.apob", ShareCategory.Labs)]
  public void Seed_CategoryMapping_FollowsTheContract(string key, ShareCategory expected)
  {
    MetricSeed.All.Single(m => m.Key == key).Category.Should().Be(expected);
  }

  [Fact]
  public void SeedMigration_MatchesTheGeneratedSql()
  {
    var path = Path.Combine(MigrationLintTests.SqlFolder(), MetricSeed.SeedScriptName);
    var expected = MetricSeed.ToSql();

    if (Environment.GetEnvironmentVariable("SELFKEPT_REGENERATE_SEED") == "1")
    {
      File.WriteAllText(path, expected);
    }

    File.Exists(path).Should().BeTrue($"{MetricSeed.SeedScriptName} must exist; regenerate with SELFKEPT_REGENERATE_SEED=1");
    File.ReadAllText(path).Replace("\r\n", "\n").Should().Be(expected,
      "the SQL seed is generated from MetricSeed.cs; regenerate with SELFKEPT_REGENERATE_SEED=1 dotnet test --filter MetricSeedTests");
  }
}
