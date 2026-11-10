using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret;
using SelfKept.Infrastructure.Services.Interpret.Rules;
using SelfKept.UnitTests.Collect;
using SelfKept.UnitTests.Labs;
using Xunit;

namespace SelfKept.UnitTests.Interpret;

/// <summary>The trial and cross-reference rules over synthetic panels: each fires, stays quiet, and respects the draw conditions.</summary>
public class CrossReferenceRuleTests
{
  private static readonly DateOnly Day = new(2026, 11, 10);
  private static readonly DateOnly Earlier = new(2026, 9, 1);
  private static readonly DateOnly Latest = new(2026, 11, 3);
  private static readonly SeedCatalogue Catalogue = new();
  private static readonly SeedLabReferences References = new();
  private static readonly PanelConditions Morning = new(FastingStates.Yes, 10, DrawPeriods.Morning, null, null);

  private static LabPanelSnapshot Panel(DateOnly collected, DateOnly confirmed, PanelConditions? conditions, string[]? substances, params (string Key, double Value)[] values)
  {
    var id = Guid.NewGuid();
    var results = values.ToDictionary(v => v.Key, v => new LabResult
    {
      Id = Guid.NewGuid(), ReportId = id, MetricKey = v.Key, Value = v.Value, Unit = Catalogue.Find(v.Key)!.Unit, Confirmed = true,
    }, StringComparer.Ordinal);
    return new LabPanelSnapshot(id, collected, confirmed, results) { Conditions = conditions ?? Morning, Substances = substances ?? [] };
  }

  private static LabPanelSnapshot Now(params (string, double)[] values) => Panel(Latest, Day, null, null, values);
  private static LabPanelSnapshot Then(params (string, double)[] values) => Panel(Earlier, Earlier, null, null, values);

  private static RuleContext Context(SexAtBirth sex, IReadOnlyList<Trial>? trials, params LabPanelSnapshot[] panels)
  {
    var person = new Person { Id = Guid.NewGuid(), DisplayName = "Test", SexAtBirth = sex, TimeZone = "UTC" };
    var labs = new LabContext(
      panels.OrderByDescending(p => p.CollectedOn).ToList(),
      key => Catalogue.Find(key) is { } m ? References.Find(m, sex) : null,
      key => Catalogue.Find(key)?.DisplayName ?? key)
    {
      Sex = sex,
      Trials = trials ?? [],
    };
    return new RuleContext(person, Day, new Dictionary<string, Baseline>(), new Dictionary<string, IReadOnlyList<DailySummary>>(), labs);
  }

  private static RuleContext Male(params LabPanelSnapshot[] panels) => Context(SexAtBirth.Male, null, panels);

  private static Trial Trial(DateOnly started, DateOnly readOn, params string[] keys) => new()
  {
    Id = Guid.NewGuid(), TitleCipher = "x", StartedOn = started, ReadOn = readOn, MetricKeys = keys, Status = TrialStatuses.Active,
  };

  // --- trial_ready ---

  [Fact]
  public void TrialReady_NewPanelAfterTheStart_Fires()
  {
    var trial = Trial(new DateOnly(2026, 9, 10), new DateOnly(2026, 12, 10), LabMetricKeys.Ldl);

    var c = new TrialReadyRule().Evaluate(Context(SexAtBirth.Male, [trial], Then((LabMetricKeys.Ldl, 150)), Now((LabMetricKeys.Ldl, 120))));

    c.Should().NotBeNull();
    c!.Kind.Should().Be(InsightKinds.TrialReady);
    c.Evidence["trigger"].Should().Be("panel");
    c.Templated.Should().Contain("Sep 10, 2026");
  }

  [Fact]
  public void TrialReady_WearableTrialOnItsReadDate_Fires_ButNotAWeekAndMoreLater()
  {
    var due = Trial(new DateOnly(2026, 10, 1), Day, MetricKeys.RestingHrBpm);
    var old = Trial(new DateOnly(2026, 9, 1), Day.AddDays(-(LabRules.RecentDays + 1)), MetricKeys.RestingHrBpm);

    new TrialReadyRule().Evaluate(Context(SexAtBirth.Male, [due])).Should().NotBeNull();
    new TrialReadyRule().Evaluate(Context(SexAtBirth.Male, [old])).Should().BeNull();
  }

  [Fact]
  public void TrialReady_BloodTrialOnItsReadDateWithoutAPanelSinceTheStart_IsQuiet_RetestDueFiresInstead()
  {
    var trial = Trial(new DateOnly(2026, 9, 10), Day, LabMetricKeys.Ldl, LabMetricKeys.ApoB);
    var context = Context(SexAtBirth.Male, [trial], Then((LabMetricKeys.Ldl, 150)));

    new TrialReadyRule().Evaluate(context).Should().BeNull();
    var retest = new RetestDueRule().Evaluate(context);
    retest.Should().NotBeNull();
    retest!.Templated.Should().Contain("LDL").And.Contain("ApoB").And.Contain("blood test");
    retest.Metrics.Should().BeEquivalentTo(new[] { LabMetricKeys.Ldl, LabMetricKeys.ApoB });
  }

  [Fact]
  public void RetestDue_QuietBeforeTheReadDate_AndOnceAPanelHasCome()
  {
    var early = Trial(new DateOnly(2026, 9, 10), Day.AddDays(3), LabMetricKeys.Ldl);
    var answered = Trial(new DateOnly(2026, 9, 10), Day, LabMetricKeys.Ldl);

    new RetestDueRule().Evaluate(Context(SexAtBirth.Male, [early])).Should().BeNull();
    new RetestDueRule().Evaluate(Context(SexAtBirth.Male, [answered], Now((LabMetricKeys.Ldl, 120)))).Should().BeNull();
  }

  // --- draw_conditions_differ ---

  [Fact]
  public void DrawConditionsDiffer_FastingThenNot_NamesTheLipidsOnBothPanels()
  {
    var then = Then((LabMetricKeys.Triglycerides, 120), (LabMetricKeys.ApoB, 90), (LabMetricKeys.Ldl, 110));
    var now = Panel(Latest, Day, new PanelConditions(FastingStates.No, null, DrawPeriods.Morning, null, null), null,
      (LabMetricKeys.Triglycerides, 168), (LabMetricKeys.ApoB, 92), (LabMetricKeys.Ldl, 104));

    var c = new DrawConditionsDifferRule().Evaluate(Male(then, now));

    c.Should().NotBeNull();
    c!.Templated.Should().Contain("not fasting this time, fasting last time");
    c.Metrics.Should().BeEquivalentTo(new[] { LabMetricKeys.Ldl, LabMetricKeys.Triglycerides }, "a meal does not move ApoB");
  }

  [Fact]
  public void DrawConditionsDiffer_SameConditions_OrUnknown_IsQuiet()
  {
    new DrawConditionsDifferRule().Evaluate(Male(Then((LabMetricKeys.Ldl, 110)), Now((LabMetricKeys.Ldl, 100)))).Should().BeNull();
    var unknown = Panel(Earlier, Earlier, PanelConditions.Unknown, null, (LabMetricKeys.Ldl, 110));
    new DrawConditionsDifferRule().Evaluate(Male(unknown, Now((LabMetricKeys.Ldl, 100)))).Should().BeNull("unknown is not a difference");
  }

  [Fact]
  public void DrawConditionsDiffer_BiotinStarted_NamesTheThyroidMarkers()
  {
    var now = Panel(Latest, Day, null, [SubstanceTags.Biotin], (LabMetricKeys.Tsh, 1.1));

    var c = new DrawConditionsDifferRule().Evaluate(Male(Then((LabMetricKeys.Tsh, 2.0)), now));

    c!.Templated.Should().Contain("biotin on your list at this draw");
    c.Metrics.Should().Equal(LabMetricKeys.Tsh);
  }

  // --- apob_ldl_disagree ---

  [Fact]
  public void ApoBLdl_LdlAboveItsLineWithApoBInside_Fires()
  {
    var c = new ApoBLdlDisagreeRule().Evaluate(Male(Now((LabMetricKeys.ApoB, 80), (LabMetricKeys.Ldl, 130))));

    c.Should().NotBeNull();
    c!.Templated.Should().Contain("LDL is 130 mg/dL, above its line of 100").And.Contain("ApoB tracks heart risk more closely");
  }

  [Fact]
  public void ApoBLdl_BothAboveOrBothInside_IsQuiet()
  {
    new ApoBLdlDisagreeRule().Evaluate(Male(Now((LabMetricKeys.ApoB, 120), (LabMetricKeys.Ldl, 140)))).Should().BeNull();
    new ApoBLdlDisagreeRule().Evaluate(Male(Now((LabMetricKeys.ApoB, 70), (LabMetricKeys.Ldl, 90)))).Should().BeNull();
  }

  [Fact]
  public void ApoBLdl_OppositeMovesOfFifteenPercentEach_Fire()
  {
    var c = new ApoBLdlDisagreeRule().Evaluate(Male(
      Then((LabMetricKeys.ApoB, 70), (LabMetricKeys.Ldl, 95)),
      Now((LabMetricKeys.ApoB, 85), (LabMetricKeys.Ldl, 80))));

    c!.Templated.Should().Contain("moved in opposite directions");
  }

  // --- calculated_ldl_unreliable ---

  [Fact]
  public void CalculatedLdl_TriglyceridesAt400_Fires_Below_IsQuiet()
  {
    new CalculatedLdlRule().Evaluate(Male(Now((LabMetricKeys.Triglycerides, 400), (LabMetricKeys.Ldl, 90))))!.Templated.Should().Contain("400 or more");
    new CalculatedLdlRule().Evaluate(Male(Now((LabMetricKeys.Triglycerides, 399), (LabMetricKeys.Ldl, 90)))).Should().BeNull();
    new CalculatedLdlRule().Evaluate(Male(Now((LabMetricKeys.Triglycerides, 450)))).Should().BeNull("no LDL on the panel");
  }

  // --- ferritin_inflammation ---

  [Fact]
  public void FerritinInflammation_LowFerritinWithRaisedCrp_Fires()
  {
    var c = new FerritinInflammationRule().Evaluate(Male(Now((LabMetricKeys.Ferritin, 38), (LabMetricKeys.HsCrp, 4.1))));

    c!.Templated.Should().Contain("Ferritin was 38 ng/mL with hs-CRP at 4.1 mg/L").And.Contain("below 70");
  }

  [Fact]
  public void FerritinInflammation_NormalCrp_OrFerritinAt70_IsQuiet()
  {
    new FerritinInflammationRule().Evaluate(Male(Now((LabMetricKeys.Ferritin, 38), (LabMetricKeys.HsCrp, 1.0)))).Should().BeNull();
    new FerritinInflammationRule().Evaluate(Male(Now((LabMetricKeys.Ferritin, 70), (LabMetricKeys.HsCrp, 4.1)))).Should().BeNull();
  }

  // --- high_ferritin_context ---

  [Fact]
  public void HighFerritin_ManAbove300_IsClinician_AndListsTheMetabolicContext()
  {
    var c = new HighFerritinRule().Evaluate(Male(Now((LabMetricKeys.Ferritin, 420), (LabMetricKeys.Triglycerides, 210), (LabMetricKeys.Hdl, 35))));

    c!.Severity.Should().Be(InsightSeverities.Clinician);
    c.Templated.Should().Contain("above 300").And.Contain("triglycerides 210 mg/dL").And.Contain("HDL 35 mg/dL").And.Contain("Transferrin saturation, which helps tell the two apart, is not on this panel");
  }

  [Fact]
  public void HighFerritin_WomanUsesTwoHundred_AndHighSaturationIsNamed()
  {
    var c = Context(SexAtBirth.Female, null, Now((LabMetricKeys.Ferritin, 250), (LabMetricKeys.TransferrinSaturation, 62)));

    var candidate = new HighFerritinRule().Evaluate(c);

    candidate!.Templated.Should().Contain("above 200").And.Contain("above 50 %");
    new HighFerritinRule().Evaluate(Male(Now((LabMetricKeys.Ferritin, 250)))).Should().BeNull("250 is not above 300 for a man");
  }

  // --- thyroid_cholesterol ---

  [Fact]
  public void ThyroidCholesterol_HighTshWithLdlAboveRange_IsClinician()
  {
    var c = new ThyroidCholesterolRule().Evaluate(Male(Now((LabMetricKeys.Tsh, 6.2), (LabMetricKeys.Ldl, 150))));

    c!.Severity.Should().Be(InsightSeverities.Clinician);
    c.Templated.Should().Contain("TSH was 6.2 mIU/L").And.Contain("LDL cholesterol 150 mg/dL, above range").And.Contain("rule out");
  }

  [Fact]
  public void ThyroidCholesterol_RisenLdlCounts_NormalTshIsQuiet()
  {
    new ThyroidCholesterolRule().Evaluate(Male(Then((LabMetricKeys.Ldl, 80)), Now((LabMetricKeys.Tsh, 5.0), (LabMetricKeys.Ldl, 95))))!
      .Templated.Should().Contain("up from 80");
    new ThyroidCholesterolRule().Evaluate(Male(Now((LabMetricKeys.Tsh, 2.0), (LabMetricKeys.Ldl, 150)))).Should().BeNull();
  }

  // --- free_testosterone_shbg ---

  [Fact]
  public void FreeTestosterone_ShbgOutOfRange_Fires_InRangeIsQuiet()
  {
    new FreeTestosteroneShbgRule().Evaluate(Male(Now((LabMetricKeys.Shbg, 95), (LabMetricKeys.TotalTestosterone, 600), (LabMetricKeys.FreeTestosterone, 90))))!
      .Templated.Should().Contain("SHBG was 95 nmol/L, above").And.Contain("Free testosterone on the same panel (90 pg/mL)");
    new FreeTestosteroneShbgRule().Evaluate(Male(Now((LabMetricKeys.Shbg, 40), (LabMetricKeys.TotalTestosterone, 600)))).Should().BeNull();
  }

  // --- creatinine_muscle ---

  [Fact]
  public void CreatinineMuscle_CreatineWithHighCreatinine_Fires_WithoutCreatineIsQuiet()
  {
    var withCreatine = Panel(Latest, Day, null, [SubstanceTags.Creatine], (LabMetricKeys.Creatinine, 1.4), (LabMetricKeys.Egfr, 85));

    new CreatinineMuscleRule().Evaluate(Male(withCreatine))!.Templated.Should().Contain("creatine is on your list").And.Contain("eGFR 85");
    new CreatinineMuscleRule().Evaluate(Male(Now((LabMetricKeys.Creatinine, 1.4)))).Should().BeNull();
  }

  // --- hematocrit_testosterone ---

  [Fact]
  public void HematocritTestosterone_OnTherapyAboveRange_IsClinician_WithoutTherapyQuiet()
  {
    var onTherapy = Panel(Latest, Day, null, [SubstanceTags.Testosterone], (LabMetricKeys.Hematocrit, 51));
    var then = Panel(Earlier, Earlier, null, [SubstanceTags.Testosterone], (LabMetricKeys.Hematocrit, 47));

    var c = new HematocritTestosteroneRule().Evaluate(Male(then, onTherapy));

    c!.Severity.Should().Be(InsightSeverities.Clinician);
    c.Templated.Should().Contain("up from 47").And.NotContain("54");
    new HematocritTestosteroneRule().Evaluate(Male(Now((LabMetricKeys.Hematocrit, 51)))).Should().BeNull();
  }

  // --- expected_swing ---

  [Fact]
  public void ExpectedSwing_MoveInsideTheRcv_Fires_AndLabTrendHolds()
  {
    // TSH 2.0 to 2.7: up 35 percent, past the 15 percent line, inside the RCV of 49.
    var context = Male(Then((LabMetricKeys.Tsh, 2.0)), Now((LabMetricKeys.Tsh, 2.7)));

    var c = new ExpectedSwingRule().Evaluate(context);

    c!.Templated.Should().Contain("up 35 percent").And.Contain("about 49 percent");
    new LabTrendRule().Evaluate(context).Should().BeNull();
  }

  [Fact]
  public void ExpectedSwing_MoveBeyondTheRcv_OrUnderFifteenPercent_IsQuiet()
  {
    new ExpectedSwingRule().Evaluate(Male(Then((LabMetricKeys.Ggt, 30)), Now((LabMetricKeys.Ggt, 45)))).Should().BeNull("50 percent is past GGT's RCV");
    new ExpectedSwingRule().Evaluate(Male(Then((LabMetricKeys.Tsh, 2.0)), Now((LabMetricKeys.Tsh, 2.2)))).Should().BeNull();
  }

  [Fact]
  public void ExpectedSwing_DifferentBiotinStatus_IsNotCalledExpected()
  {
    var now = Panel(Latest, Day, null, [SubstanceTags.Biotin], (LabMetricKeys.Tsh, 2.7));

    new ExpectedSwingRule().Evaluate(Male(Then((LabMetricKeys.Tsh, 2.0)), now)).Should().BeNull();
  }

  // --- lab_review_ready ---

  [Fact]
  public void LabReviewReady_TwoPanels_CountsTheVerdicts()
  {
    var c = new LabReviewReadyRule().Evaluate(Male(
      Then((LabMetricKeys.Ggt, 40), (LabMetricKeys.Ldl, 150)),
      Now((LabMetricKeys.Ggt, 28), (LabMetricKeys.Ldl, 120))));

    c!.Templated.Should().Contain("1 moved the better way by more than tests vary").And.Contain("1 that cannot be judged");
  }

  [Fact]
  public void LabReviewReady_OnePanel_OrAnOldPanel_IsQuiet()
  {
    new LabReviewReadyRule().Evaluate(Male(Now((LabMetricKeys.Ggt, 28)))).Should().BeNull();
    var old = Panel(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), null, null, (LabMetricKeys.Ggt, 28));
    new LabReviewReadyRule().Evaluate(Male(Then((LabMetricKeys.Ggt, 40)), old)).Should().BeNull("the panel is not current");
  }

  // --- the engine ---

  [Fact]
  public void DefaultRules_IncludeEveryNewKind_Once()
  {
    var kinds = RuleEngine.DefaultRules().Select(r => r.Kind).ToList();

    kinds.Should().OnlyHaveUniqueItems();
    kinds.Should().Contain(new[]
    {
      InsightKinds.TrialReady, InsightKinds.RetestDue, InsightKinds.DrawConditionsDiffer, InsightKinds.ApoBLdlDisagree,
      InsightKinds.CalculatedLdlUnreliable, InsightKinds.FerritinInflammation, InsightKinds.HighFerritinContext,
      InsightKinds.ThyroidCholesterol, InsightKinds.FreeTestosteroneShbg, InsightKinds.CreatinineMuscle,
      InsightKinds.HematocritTestosterone, InsightKinds.ExpectedSwing, InsightKinds.LabReviewReady,
    });
  }

  [Fact]
  public void NewRules_TextHasNoEmDashes()
  {
    var context = Male(
      Then((LabMetricKeys.Tsh, 2.0), (LabMetricKeys.Ldl, 90), (LabMetricKeys.Ferritin, 420)),
      Panel(Latest, Day, new PanelConditions(FastingStates.No, null, DrawPeriods.Afternoon, true, null), [SubstanceTags.Biotin, SubstanceTags.Creatine],
        (LabMetricKeys.Tsh, 6.0), (LabMetricKeys.Ldl, 140), (LabMetricKeys.Ferritin, 420), (LabMetricKeys.Triglycerides, 450), (LabMetricKeys.Creatinine, 1.5)));

    var candidates = new RuleEngine(RuleEngine.DefaultRules()).Evaluate(context);

    candidates.Should().NotBeEmpty();
    candidates.Should().OnlyContain(c => !c.Templated.Contains('\u2014'));
  }
}
