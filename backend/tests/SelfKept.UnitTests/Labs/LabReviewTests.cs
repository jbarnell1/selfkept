using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Services.Labs;
using Xunit;

namespace SelfKept.UnitTests.Labs;

/// <summary>The reference change value, each verdict, and the link and variation catalogues' integrity.</summary>
public class LabReviewTests
{
  private static readonly SeedLabReferences References = new();
  private static readonly Collect.SeedCatalogue Catalogue = new();

  private static DrawContext Fasting(params string[] substances) =>
    new(new PanelConditions(FastingStates.Yes, 10, DrawPeriods.Morning, null, null), substances);

  private static DrawContext NotFasting(string period = DrawPeriods.Morning) =>
    new(new PanelConditions(FastingStates.No, null, period, null, null), []);

  private static MarkerJudgement Judge(string key, double value, double? previous, DrawContext? latest = null, DrawContext? earlier = null, SexAtBirth sex = SexAtBirth.Male)
  {
    var reference = References.Find(Catalogue.Find(key)!, sex);
    return LabReview.Judge(key, value, previous, reference, Catalogue.Find(key)!.GoodDirection, sex, latest ?? Fasting(), earlier ?? Fasting());
  }

  // --- RCV maths ---

  [Theory]
  [InlineData(17.7, 49.03)]
  [InlineData(16.9, 46.81)]
  [InlineData(1.2, 3.32)]
  [InlineData(4.4, 12.19)]
  public void Rcv_IsTwoPointSevenSevenTimesCvi_WithoutAnalyticalVariation(double cvi, double expected)
  {
    LabVariation.Rcv(cvi).Should().BeApproximately(expected, 0.01);
  }

  [Fact]
  public void Rcv_AddsAnalyticalVariationInQuadrature()
  {
    LabVariation.Rcv(4, 3).Should().BeApproximately(2.77 * 5, 1e-9);
  }

  [Fact]
  public void ChangePercent_IsAgainstThePreviousValue_AndNullFromZero()
  {
    LabVariation.ChangePercent(100, 60).Should().Be(-40);
    LabVariation.ChangePercent(0, 5).Should().BeNull();
  }

  // --- verdicts ---

  [Fact]
  public void Judge_DownMarkerFallsBeyondTheRcv_IsWorking()
  {
    // GGT CVi 8.9, RCV 24.7; 40 to 28 is down 30 percent.
    var j = Judge(LabMetricKeys.Ggt, 28, 40);

    j.Verdict.Should().Be(LabVerdicts.Working);
    j.BeyondRcv.Should().BeTrue();
    j.RcvPercent.Should().Be(24.7);
    j.ChangePercent.Should().Be(-30);
    j.Moved.Should().Be(LabReview.Down);
    j.Wanted.Should().Be(LabReview.Down);
  }

  [Fact]
  public void Judge_RightWayInsideTheRcv_IsRightDirection()
  {
    var j = Judge(LabMetricKeys.Ggt, 36, 40);

    j.Verdict.Should().Be(LabVerdicts.RightDirection);
    j.BeyondRcv.Should().BeFalse();
  }

  [Fact]
  public void Judge_WrongWayInsideTheRcv_IsNoClearChange()
  {
    Judge(LabMetricKeys.Ggt, 44, 40).Verdict.Should().Be(LabVerdicts.NoClearChange);
  }

  [Fact]
  public void Judge_WrongWayBeyondTheRcv_IsNotWorking()
  {
    Judge(LabMetricKeys.Ggt, 60, 40).Verdict.Should().Be(LabVerdicts.NotWorking);
  }

  [Fact]
  public void Judge_RangeMarkerInsideBothTimes_HasNoWantedDirection()
  {
    // TSH 1.5 to 3.0 is up 100 percent, beyond its RCV of 49, but inside 0.3 to 4.2 both times.
    var j = Judge(LabMetricKeys.Tsh, 3.0, 1.5);

    j.Wanted.Should().Be(LabReview.NoDirection);
    j.BeyondRcv.Should().BeTrue();
    j.Verdict.Should().Be(LabVerdicts.NoClearChange);
    j.Reasons.Should().Contain(LabVerdictReasons.NoWantedDirection);
  }

  [Fact]
  public void Judge_RangeMarkerComingBackIntoRange_WantsThatWay()
  {
    // TSH 6.0 (above 4.2) to 2.5 is down 58 percent: back toward the range, beyond the RCV.
    Judge(LabMetricKeys.Tsh, 2.5, 6.0).Verdict.Should().Be(LabVerdicts.Working);
  }

  [Fact]
  public void Judge_NoPreviousValue_CannotTell()
  {
    var j = Judge(LabMetricKeys.Ggt, 30, null);

    j.Verdict.Should().Be(LabVerdicts.CannotTell);
    j.Reasons.Should().Equal(LabVerdictReasons.NoPrevious);
  }

  [Fact]
  public void Judge_MarkerWithoutAVerifiedVariationFigure_CannotTell_EvenForABigMove()
  {
    var j = Judge(LabMetricKeys.Ldl, 100, 160);

    j.Verdict.Should().Be(LabVerdicts.CannotTell);
    j.Reasons.Should().Contain(LabVerdictReasons.NoVariationFigure);
    j.RcvPercent.Should().BeNull();
    j.ChangePercent.Should().Be(-37.5, "the change is still reported");
  }

  [Fact]
  public void Judge_TestosteroneFigureIsForMen_SoAWomanCannotTell()
  {
    Judge(LabMetricKeys.TotalTestosterone, 40, 20, sex: SexAtBirth.Female).Reasons.Should().Contain(LabVerdictReasons.NoVariationFigure);
    Judge(LabMetricKeys.TotalTestosterone, 300, 200).Verdict.Should().Be(LabVerdicts.Working);
  }

  [Fact]
  public void Judge_AfternoonDrawAgainstAMorningOne_CannotTell_AndIsReadWithCare()
  {
    var afternoon = new DrawContext(new PanelConditions(FastingStates.Yes, 10, DrawPeriods.Afternoon, null, null), []);

    var j = Judge(LabMetricKeys.TotalTestosterone, 400, 600, latest: afternoon);

    j.Verdict.Should().Be(LabVerdicts.CannotTell);
    j.Reasons.Should().Contain(LabVerdictReasons.DrawConditionsDiffer);
    j.ReadWithCare.Select(l => l.Id).Should().Contain("not_morning_testosterone");
  }

  [Fact]
  public void Judge_BothDrawsAfternoon_CompareLikeWithLike()
  {
    var afternoon = new DrawContext(new PanelConditions(FastingStates.Yes, 10, DrawPeriods.Afternoon, null, null), []);

    Judge(LabMetricKeys.TotalTestosterone, 300, 200, latest: afternoon, earlier: afternoon).Verdict.Should().Be(LabVerdicts.Working);
  }

  [Fact]
  public void Judge_UnknownConditionOnOneSide_IsNotADifference()
  {
    Judge(LabMetricKeys.TotalTestosterone, 300, 200, earlier: DrawContext.Unknown).Verdict.Should().Be(LabVerdicts.Working);
  }

  [Fact]
  public void Judge_BiotinAtEitherDraw_TakesTheThyroidComparisonAway()
  {
    var j = Judge(LabMetricKeys.Tsh, 2.5, 6.0, earlier: Fasting(SubstanceTags.Biotin));

    j.Verdict.Should().Be(LabVerdicts.CannotTell);
    j.Reasons.Should().Contain(LabVerdictReasons.SubstanceAtDraw);
  }

  [Fact]
  public void Judge_CreatineStartedBetweenDraws_CannotTellCreatinine_ButOnBothDrawsCompares()
  {
    Judge(LabMetricKeys.Creatinine, 1.25, 1.0, latest: Fasting(SubstanceTags.Creatine)).Verdict.Should().Be(LabVerdicts.CannotTell);
    Judge(LabMetricKeys.Creatinine, 1.25, 1.0, latest: Fasting(SubstanceTags.Creatine), earlier: Fasting(SubstanceTags.Creatine))
      .Verdict.Should().NotBe(LabVerdicts.CannotTell);
  }

  [Fact]
  public void ReadWithCare_NotFasting_MarksTriglyceridesAndLdl_NotApoBOrHdl()
  {
    LabReview.ReadWithCare(LabMetricKeys.Triglycerides, NotFasting()).Should().NotBeEmpty();
    LabReview.ReadWithCare(LabMetricKeys.Ldl, NotFasting()).Should().NotBeEmpty();
    LabReview.ReadWithCare(LabMetricKeys.ApoB, NotFasting()).Should().BeEmpty("a meal does not move ApoB");
    LabReview.ReadWithCare(LabMetricKeys.Hdl, NotFasting()).Should().BeEmpty();
  }

  [Fact]
  public void Differences_ListsWhatIsKnownToDiffer_Only()
  {
    var now = new DrawContext(new PanelConditions(FastingStates.No, null, DrawPeriods.Afternoon, true, null), [SubstanceTags.Biotin]);
    var then = new DrawContext(new PanelConditions(FastingStates.Yes, 12, DrawPeriods.Morning, false, false), []);

    LabReview.Differences(now, then).Select(d => d.Condition).Should().Equal("fasting", "drawPeriod", "hardTraining", "substance:biotin");
  }

  [Fact]
  public void Retest_AfternoonTestosterone_AndHighNonFastingTriglycerides()
  {
    LabReview.Retest(LabMetricKeys.TotalTestosterone, 400, NotFasting(DrawPeriods.Afternoon)).Should().Contain("morning");
    LabReview.Retest(LabMetricKeys.Triglycerides, 500, NotFasting()).Should().Contain("440");
    LabReview.Retest(LabMetricKeys.Triglycerides, 300, NotFasting()).Should().BeNull();
    LabReview.Retest(LabMetricKeys.Ggt, 300, NotFasting()).Should().BeNull();
  }

  // --- catalogue integrity ---

  [Fact]
  public void Links_EveryEndExists_InTheCatalogue_TheSubstanceTags_OrTheDrawConditions()
  {
    var keys = MetricSeed.All.Select(m => m.Key).ToHashSet();
    foreach (var link in LabLinks.All)
    {
      foreach (var end in new[] { link.From, link.To })
      {
        if (end.StartsWith(LabLinks.SubstancePrefix, StringComparison.Ordinal)) SubstanceTags.All.Should().Contain(end[LabLinks.SubstancePrefix.Length..], link.Id);
        else if (end.StartsWith(DrawConditionKeys.Prefix, StringComparison.Ordinal)) DrawConditionKeys.All.Should().Contain(end, link.Id);
        else keys.Should().Contain(end, link.Id);
      }
      LinkDirections.All.Should().Contain(link.Direction, link.Id);
      link.To.Should().NotStartWith(LabLinks.SubstancePrefix, "a link ends at a reading");
    }
  }

  [Fact]
  public void Links_AreWellFormed_WithASourceAndNoEmDashes()
  {
    LabLinks.All.Select(l => l.Id).Should().OnlyHaveUniqueItems();
    foreach (var link in LabLinks.All)
    {
      link.SourceUrl.Should().StartWith("https://", link.Id);
      link.Source.Should().NotBeNullOrWhiteSpace(link.Id);
      link.Evidence.Should().NotBeNullOrWhiteSpace(link.Id);
      link.Explanation.Should().NotContain("\u2014", link.Id);
      link.Source.Should().NotContainEquivalentOf("EFLM database", "papers are cited, never the database");
    }
  }

  [Fact]
  public void Variation_EveryKeyIsALabMarker_AndCitesAPaper()
  {
    var labKeys = MetricSeed.All.Where(m => m.IsLab).Select(m => m.Key).ToHashSet();
    LabVariation.All.Select(v => v.MetricKey).Should().OnlyHaveUniqueItems();
    foreach (var v in LabVariation.All)
    {
      labKeys.Should().Contain(v.MetricKey);
      v.CviPercent.Should().BeGreaterThan(0);
      v.SourceUrl.Should().StartWith("https://");
      v.Source.Should().NotContain("biologicalvariation.eu");
    }
  }

  [Fact]
  public void Variation_HoldsOnlyTheVerifiedFigures()
  {
    LabVariation.All.ToDictionary(v => v.MetricKey, v => v.CviPercent).Should().BeEquivalentTo(new Dictionary<string, double>
    {
      [LabMetricKeys.TotalTestosterone] = 10.0, [LabMetricKeys.VitaminD] = 6.3, [LabMetricKeys.Ferritin] = 16.9,
      [LabMetricKeys.Tsh] = 17.7, [LabMetricKeys.FreeT3] = 5.0, [LabMetricKeys.FreeT4] = 4.8,
      [LabMetricKeys.Alt] = 9.3, [LabMetricKeys.Ast] = 9.5, [LabMetricKeys.Ggt] = 8.9, [LabMetricKeys.Alp] = 5.3,
      [LabMetricKeys.Creatinine] = 4.4, [LabMetricKeys.HbA1c] = 1.2, [LabMetricKeys.FastingGlucose] = 5.0,
    });
  }
}
