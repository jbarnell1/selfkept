using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Services.Labs;
using SelfKept.UnitTests.Collect;
using Xunit;

namespace SelfKept.UnitTests.Labs;

/// <summary>Reference lookup by sex (docs/lab-ranges.md) and the tier a value earns.</summary>
public class LabReferenceAndFlaggingTests
{
  private static readonly SeedCatalogue Catalogue = new();
  private static readonly SeedLabReferences References = new();

  private static Metric M(string key) => Catalogue.Find(key)!;

  [Fact]
  public void Find_SexSplitMarker_ReturnsTheRowForThePersonsSex()
  {
    References.Find(M("lab.apob"), SexAtBirth.Male)!.High.Should().Be(133);
    References.Find(M("lab.apob"), SexAtBirth.Female)!.High.Should().Be(117);
    References.Find(M("lab.hdl"), SexAtBirth.Male)!.Low.Should().Be(40);
    References.Find(M("lab.hdl"), SexAtBirth.Female)!.Low.Should().Be(50);
  }

  [Fact]
  public void Find_BothRow_AppliesToEverySex()
  {
    foreach (var sex in Enum.GetValues<SexAtBirth>())
    {
      References.Find(M("lab.tsh"), sex)!.Sex.Should().Be("both");
    }
  }

  [Fact]
  public void Find_SexSplitMarker_UnspecifiedSex_GetsTheCombinedRange()
  {
    var male = References.Find(M("lab.apob"), SexAtBirth.Male)!;
    var female = References.Find(M("lab.apob"), SexAtBirth.Female)!;

    var combined = References.Find(M("lab.apob"), SexAtBirth.Unspecified)!;

    combined.Low.Should().Be(Math.Min(male.Low!.Value, female.Low!.Value));
    combined.High.Should().Be(Math.Max(male.High!.Value, female.High!.Value));
    combined.OptimalLow.Should().BeNull("an optimal band is sex-specific advice");
    combined.OptimalHigh.Should().BeNull();
  }

  [Fact]
  public void Assess_UnspecifiedSex_StillCatchesAValueOutsideBothRanges()
  {
    var ferritin = References.Find(M("lab.ferritin"), SexAtBirth.Unspecified);

    var assessment = LabFlagging.Assess(ferritin, 1180);

    assessment.Should().NotBeNull();
    assessment!.Position.Should().Be(LabPositions.Above);
    assessment.Severity.Should().Be("clinician", "ferritin above 1000 is a clinician trigger whatever the sex");
  }

  [Fact]
  public void Assess_UnspecifiedSex_DoesNotFlagAValueInsideEitherRange()
  {
    var hdl = References.Find(M("lab.hdl"), SexAtBirth.Unspecified)!;

    // 45 is below the female low (50) but above the male low (40): not flagged without a sex.
    LabFlagging.Assess(hdl, 45)!.OutOfRange.Should().BeFalse();
  }

  [Fact]
  public void Find_FemaleOnlyMarker_ForAMalePerson_IsNotApplicable_AndHasNoRange()
  {
    var progesterone = M("lab.progesterone");
    progesterone.SexApplicability.Should().Be("female");

    References.Find(progesterone, SexAtBirth.Male).Should().BeNull("the CSV has a male progesterone row, but the catalogue says the marker is female-only");
    References.Find(progesterone, SexAtBirth.Female)!.High.Should().Be(24);
    References.Find(progesterone, SexAtBirth.Unspecified)!.High.Should().Be(24, "a female-only marker uses the female row when the sex is unset");
  }

  [Fact]
  public void Find_UnknownMarker_IsNull()
  {
    References.Find(new Metric { Key = "lab.nothing", SexApplicability = "both" }, SexAtBirth.Male).Should().BeNull();
  }

  [Theory]
  [InlineData("lab.tsh", 2.0, "inside", null)]
  [InlineData("lab.tsh", 5.0, "above", "watch")]
  [InlineData("lab.tsh", 12.0, "above", "clinician")]
  [InlineData("lab.tsh", 0.05, "below", "clinician")]
  [InlineData("lab.ldl", 150, "above", "watch")]
  [InlineData("lab.ldl", 195, "above", "clinician")]
  [InlineData("lab.egfr", 75, "below", "watch")]
  [InlineData("lab.egfr", 55, "below", "clinician")]
  [InlineData("lab.hscrp", 5, "above", "watch")]
  [InlineData("lab.hscrp", 12, "above", "clinician")]
  [InlineData("lab.vitamin_d", 15, "below", "watch")]
  [InlineData("lab.vitamin_d", 9, "below", "clinician")]
  [InlineData("lab.vitamin_d", 60, "above", "watch")]
  [InlineData("lab.ferritin", 10, "below", "clinician")]
  [InlineData("lab.ferritin", 20, "below", "watch")]
  [InlineData("lab.triglycerides", 300, "above", "watch")]
  [InlineData("lab.triglycerides", 520, "above", "clinician")]
  [InlineData("lab.cortisol", 6, "below", "watch")]
  [InlineData("lab.cortisol", 4, "below", "clinician")]
  [InlineData("lab.free_t4", 2.3, "above", "clinician")]
  [InlineData("lab.alp", 150, "above", "watch")]
  [InlineData("lab.apob", 140, "above", "watch")]
  public void Assess_MalePerson_PositionAndTier_FollowTheFlagTiersTable(string key, double value, string position, string? severity)
  {
    var assessment = LabFlagging.Assess(References.Find(M(key), SexAtBirth.Male), value);

    assessment.Should().NotBeNull();
    assessment!.Position.Should().Be(position);
    assessment.Severity.Should().Be(severity);
    if (position != "inside") assessment.Distance.Should().BeGreaterThan(0);
  }

  [Fact]
  public void Assess_ClinicianRowsNarrowedByDirection()
  {
    // Hemoglobin and hematocrit: clinician below range, watch above. Creatinine, fructosamine: the other way round.
    LabFlagging.Assess(References.Find(M("lab.hemoglobin"), SexAtBirth.Male), 12.0)!.Severity.Should().Be("clinician");
    LabFlagging.Assess(References.Find(M("lab.hemoglobin"), SexAtBirth.Male), 18.0)!.Severity.Should().Be("watch");
    LabFlagging.Assess(References.Find(M("lab.hematocrit"), SexAtBirth.Female), 30)!.Severity.Should().Be("clinician");
    LabFlagging.Assess(References.Find(M("lab.creatinine"), SexAtBirth.Female), 1.2)!.Severity.Should().Be("clinician");
    LabFlagging.Assess(References.Find(M("lab.creatinine"), SexAtBirth.Female), 0.4)!.Severity.Should().Be("watch");
    LabFlagging.Assess(References.Find(M("lab.fructosamine"), SexAtBirth.Male), 300)!.Severity.Should().Be("clinician");
    LabFlagging.Assess(References.Find(M("lab.fructosamine"), SexAtBirth.Male), 190)!.Severity.Should().Be("watch");
    LabFlagging.Assess(References.Find(M("lab.albumin"), SexAtBirth.Male), 3.0)!.Severity.Should().Be("clinician");
  }

  [Fact]
  public void Assess_RowWithoutARange_IsNull_AndRowWithoutATier_HasNoSeverity()
  {
    LabFlagging.Assess(References.Find(M("lab.ldl_apob_ratio"), SexAtBirth.Male), 1.5).Should().BeNull("no bounds at all");
    LabFlagging.Assess(null, 1).Should().BeNull();
  }

  [Fact]
  public void IsWorse_FollowsTheGoodDirection()
  {
    var apob = References.Find(M("lab.apob"), SexAtBirth.Male)!;
    LabFlagging.IsWorse(apob, 88, 104).Should().BeTrue("lower is better");
    LabFlagging.IsWorse(apob, 104, 88).Should().BeFalse();

    var hdl = References.Find(M("lab.hdl"), SexAtBirth.Male)!;
    LabFlagging.IsWorse(hdl, 60, 48).Should().BeTrue("higher is better");

    var tsh = References.Find(M("lab.tsh"), SexAtBirth.Male)!;
    LabFlagging.IsWorse(tsh, 2.0, 2.4).Should().BeFalse("towards the middle of the range");
    LabFlagging.IsWorse(tsh, 3.0, 3.9).Should().BeTrue("away from the middle");
    LabFlagging.IsWorse(tsh, 1.0, 0.5).Should().BeTrue();

    var ferritin = References.Find(M("lab.ferritin"), SexAtBirth.Male)!;
    LabFlagging.IsWorse(ferritin, 100, 60).Should().BeTrue("31 to 409: 60 is further from the middle than 100");
  }
}
