using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Data;
using Xunit;

namespace SelfKept.UnitTests.Data;

/// <summary>
/// LabSeed.cs is the single source of the reference ranges and aliases; 0007_lab_seed.up.sql is
/// generated from it. Regenerate after editing the lists:
///   SELFKEPT_REGENERATE_SEED=1 dotnet test --filter LabSeedTests
/// </summary>
public class LabSeedTests
{
  [Fact]
  public void References_CoverEveryLabMarker_OnceOrOncePerSex()
  {
    var labKeys = MetricSeed.All.Where(m => m.IsLab).Select(m => m.Key).ToList();
    var byKey = LabSeed.References.GroupBy(r => r.MetricKey).ToDictionary(g => g.Key, g => g.Select(r => r.Sex).ToList());

    byKey.Keys.Should().BeEquivalentTo(labKeys, "every marker has a row (docs/lab-ranges.md lists all 42)");
    foreach (var (key, sexes) in byKey)
    {
      if (sexes.Contains(SexApplicabilities.Both)) sexes.Should().ContainSingle($"{key}: a both row stands alone");
      else sexes.Should().BeEquivalentTo(new[] { SexApplicabilities.Male, SexApplicabilities.Female }, $"{key}: sex-split rows come in pairs");
    }
    LabSeed.References.Should().HaveCount(61, "the CSV block has 61 rows");
  }

  [Fact]
  public void References_EveryRow_IsWellFormed()
  {
    foreach (var r in LabSeed.References)
    {
      SexApplicabilities.All.Should().Contain(r.Sex, r.MetricKey);
      LabTiers.All.Should().Contain(r.Tier, r.MetricKey);
      new[] { GoodDirections.Up, GoodDirections.Down, GoodDirections.Range }.Should().Contain(r.GoodDirection, r.MetricKey);
      r.SourceUrl.Should().StartWith("https://", r.MetricKey);
      if (r.Low != null && r.High != null) r.Low.Should().BeLessThan(r.High.Value, r.MetricKey);
      if (r.Tier == LabTiers.None) r.HasRange.Should().BeFalse($"{r.MetricKey}: a row without a tier has no range to flag");
    }
  }

  [Theory]
  [InlineData("lab.apob", "male", 66.0, 133.0, "watch")]
  [InlineData("lab.apob", "female", 60.0, 117.0, "watch")]
  [InlineData("lab.tsh", "both", 0.3, 4.2, "clinician")]
  [InlineData("lab.ldl", "both", null, 100.0, "watch")]
  [InlineData("lab.egfr", "both", 90.0, null, "clinician")]
  [InlineData("lab.ldl_apob_ratio", "both", null, null, "none")]
  [InlineData("lab.alt", "male", 7.0, 55.0, "watch")]
  [InlineData("lab.alt", "female", 7.0, 45.0, "watch")]
  [InlineData("lab.platelets", "female", 157.0, 371.0, "watch")]
  [InlineData("lab.hba1c", "both", 4.0, 5.6, "watch")]
  [InlineData("lab.glucose_fasting", "both", null, 100.0, "watch")]
  [InlineData("lab.transferrin_saturation", "both", 14.0, 50.0, "watch")]
  [InlineData("lab.iron", "female", 35.0, 145.0, "watch")]
  public void References_MatchTheCsv(string key, string sex, double? low, double? high, string tier)
  {
    var row = LabSeed.References.Single(r => r.MetricKey == key && r.Sex == sex);
    row.Low.Should().Be(low);
    row.High.Should().Be(high);
    row.Tier.Should().Be(tier);
  }

  [Fact]
  public void CystatinC_CarriesTheAgeSplit_AndNoOtherRowDoes()
  {
    var row = LabSeed.References.Single(r => r.MetricKey == LabMetricKeys.CystatinC);

    (row.Low, row.High).Should().Be((0.63, 1.03));
    row.AgeSplitAt.Should().Be(50);
    (row.OlderLow, row.OlderHigh).Should().Be((0.67, 1.21));
    LabSeed.References.Where(r => r.AgeSplitAt != null).Should().ContainSingle();
  }

  [Fact]
  public void Aliases_CoverEveryLabMarker_IncludeTheDisplayName_AndAreUnique()
  {
    var labs = MetricSeed.All.Where(m => m.IsLab).ToList();
    var byKey = LabSeed.Aliases.GroupBy(a => a.MetricKey).ToDictionary(g => g.Key, g => g.Select(a => a.Alias).ToList());

    byKey.Keys.Should().BeEquivalentTo(labs.Select(m => m.Key));
    foreach (var metric in labs)
    {
      byKey[metric.Key].Should().Contain(metric.DisplayName);
      byKey[metric.Key].Count.Should().BeGreaterThan(1, $"{metric.Key} needs synonyms, not just the display name");
    }
    LabSeed.Aliases.Select(a => (a.MetricKey, a.Alias.ToLowerInvariant())).Should().OnlyHaveUniqueItems();
    LabSeed.Aliases.Should().OnlyContain(a => a.Alias.Length <= 80 && !a.Alias.Contains('\u2014'), "no em dashes, fits the column");
    LabSeed.Aliases.Select(a => a.Alias).Should().Contain(new[] { "ApoB", "Apolipoprotein B", "hs-CRP", "hsCRP", "25-OH Vitamin D", "Estimated GFR", "Thyroid stimulating hormone", "FT3", "FT4", "Hct", "Hgb", "LDL-C", "HDL-C", "Gamma GT", "Alkaline phosphatase" });
  }

  [Fact]
  public void SeedMigration_MatchesTheGeneratedSql()
  {
    var path = Path.Combine(MigrationLintTests.SqlFolder(), LabSeed.SeedScriptName);
    var expected = LabSeed.ToSql();

    if (Environment.GetEnvironmentVariable("SELFKEPT_REGENERATE_SEED") == "1")
    {
      File.WriteAllText(path, expected);
    }

    File.Exists(path).Should().BeTrue($"{LabSeed.SeedScriptName} must exist; regenerate with SELFKEPT_REGENERATE_SEED=1");
    File.ReadAllText(path).Replace("\r\n", "\n").Should().Be(expected,
      "the SQL seed is generated from LabSeed.cs; regenerate with SELFKEPT_REGENERATE_SEED=1 dotnet test --filter LabSeedTests");
  }
}
