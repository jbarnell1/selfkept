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

/// <summary>lab_out_of_range and lab_trend over synthetic panels (docs/labs-design.md, "Insights").</summary>
public class LabRuleTests
{
  private static readonly DateOnly Day = new(2026, 10, 29);
  private static readonly SeedCatalogue Catalogue = new();
  private static readonly SeedLabReferences References = new();
  private static readonly RuleEngine Engine = new(RuleEngine.DefaultRules());

  private static LabPanelSnapshot Panel(DateOnly collected, DateOnly confirmed, params (string Key, double Value)[] values)
  {
    var id = Guid.NewGuid();
    var results = values.ToDictionary(v => v.Key, v => new LabResult
    {
      Id = Guid.NewGuid(), ReportId = id, MetricKey = v.Key, Value = v.Value, Unit = Catalogue.Find(v.Key)!.Unit, Confirmed = true,
    }, StringComparer.Ordinal);
    return new LabPanelSnapshot(id, collected, confirmed, results);
  }

  private static RuleContext Context(SexAtBirth sex, params LabPanelSnapshot[] panels)
  {
    var person = new Person { Id = Guid.NewGuid(), DisplayName = "Test", SexAtBirth = sex, TimeZone = "UTC" };
    var labs = new LabContext(
      panels.OrderByDescending(p => p.CollectedOn).ToList(),
      key => Catalogue.Find(key) is { } m ? References.Find(m, sex) : null,
      key => Catalogue.Find(key)?.DisplayName ?? key);
    return new RuleContext(person, Day, new Dictionary<string, Baseline>(), new Dictionary<string, IReadOnlyList<DailySummary>>(), labs);
  }

  // --- lab_out_of_range ---

  [Fact]
  public void OutOfRange_ClinicianAndWatchMarkersOnTheCurrentPanel_OneCard_ClinicianLeads_NamesBothDates()
  {
    var previous = Panel(new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 22), ("lab.apob", 120), ("lab.hemoglobin", 14.5), ("lab.tsh", 2.0));
    var latest = Panel(new DateOnly(2026, 10, 20), Day, ("lab.apob", 140), ("lab.hemoglobin", 12.0), ("lab.tsh", 2.1));
    var context = Context(SexAtBirth.Male, previous, latest);

    var candidate = new LabOutOfRangeRule().Evaluate(context);

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Clinician, "hemoglobin below range is clinician tier");
    candidate.Metrics.Should().Equal("lab.hemoglobin", "lab.apob");
    candidate.Templated.Should().StartWith("Hemoglobin is below the usual range: 12 g/dL on Oct 20, 2026 (usual 13.8 to 17.2).");
    candidate.Templated.Should().Contain("Last time it was 14.5 on Sep 20, 2026.");
    candidate.Templated.Should().Contain("Also outside the usual range: ApoB (140 mg/dL).");
    candidate.Templated.Should().Contain("Bring this panel from Oct 20, 2026 and the one from Sep 20, 2026.");
    candidate.Templated.Should().NotContain("\u2014");
    candidate.Evidence["collectedOn"].Should().Be("2026-10-20");
    candidate.Evidence["previousValue"].Should().Be(14.5);
    candidate.BandDistance.Should().BeGreaterThan(0);
  }

  [Fact]
  public void OutOfRange_OnlyWatchMarkers_IsWatch_AndSuggestsARecheck()
  {
    var latest = Panel(new DateOnly(2026, 10, 28), Day, ("lab.apob", 140), ("lab.vitamin_d", 15));
    var candidate = new LabOutOfRangeRule().Evaluate(Context(SexAtBirth.Female, latest));

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Watch);
    candidate.Templated.Should().Contain("rechecking");
    candidate.Templated.Should().NotContain("Last time", "there is no previous panel");
  }

  [Fact]
  public void OutOfRange_EverythingInside_DoesNotFire()
  {
    var latest = Panel(new DateOnly(2026, 10, 28), Day, ("lab.apob", 90), ("lab.tsh", 2.1), ("lab.hemoglobin", 15.1), ("lab.ldl_apob_ratio", 1.1));
    new LabOutOfRangeRule().Evaluate(Context(SexAtBirth.Male, latest)).Should().BeNull();
  }

  [Fact]
  public void OutOfRange_PanelConfirmedMoreThanAWeekAgo_IsNoLongerCurrent()
  {
    var stale = Panel(new DateOnly(2026, 10, 1), Day.AddDays(-(LabRules.RecentDays + 1)), ("lab.apob", 140));
    var fresh = Panel(new DateOnly(2026, 10, 1), Day.AddDays(-LabRules.RecentDays), ("lab.apob", 140));

    new LabOutOfRangeRule().Evaluate(Context(SexAtBirth.Male, stale)).Should().BeNull();
    new LabOutOfRangeRule().Evaluate(Context(SexAtBirth.Male, fresh)).Should().NotBeNull();
  }

  [Fact]
  public void OutOfRange_FemaleOnlyMarkerOnAMalePanel_IsIgnored()
  {
    var male = Panel(new DateOnly(2026, 10, 28), Day, ("lab.progesterone", 30));
    new LabOutOfRangeRule().Evaluate(Context(SexAtBirth.Male, male)).Should().BeNull("no range applies");
  }

  [Fact]
  public void OutOfRange_UnspecifiedSex_FlagsOnlyValuesOutsideTheCombinedRange()
  {
    var between = Panel(new DateOnly(2026, 10, 28), Day, ("lab.hdl", 45), ("lab.tsh", 2.0));
    new LabOutOfRangeRule().Evaluate(Context(SexAtBirth.Unspecified, between))
      .Should().BeNull("HDL 45 is low for a woman but fine for a man, so without a sex it is not flagged");

    var outside = Panel(new DateOnly(2026, 10, 28), Day, ("lab.apob", 200), ("lab.tsh", 2.0));
    var candidate = new LabOutOfRangeRule().Evaluate(Context(SexAtBirth.Unspecified, outside));
    candidate.Should().NotBeNull("ApoB 200 is above both sexes' ranges");
    candidate!.Metrics.Should().Contain("lab.apob");
  }

  // --- lab_trend ---

  [Fact]
  public void Trend_FifteenPercentTheWrongWay_Fires_AndNamesBothDatesAndValues()
  {
    var previous = Panel(new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 22),
      ("lab.apob", 88), ("lab.hscrp", 2.0), ("lab.ldl", 120), ("lab.tsh", 2.0), ("lab.ferritin", 100), ("lab.ggt", 30));
    var latest = Panel(new DateOnly(2026, 10, 20), Day,
      ("lab.apob", 104), ("lab.hscrp", 2.2), ("lab.ldl", 100), ("lab.tsh", 2.4), ("lab.ferritin", 60), ("lab.ggt", 30));

    var candidate = new LabTrendRule().Evaluate(Context(SexAtBirth.Male, previous, latest));

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Watch);
    candidate.Metrics.Should().BeEquivalentTo(new[] { "lab.ferritin", "lab.apob" });
    candidate.Metrics[0].Should().Be("lab.ferritin", "the larger move leads");
    candidate.Templated.Should().Contain("Ferritin moved from 100 ng/mL on Sep 20, 2026 to 60 ng/mL on Oct 20, 2026, down 40 percent.");
    candidate.Templated.Should().Contain("ApoB (up 18 percent)");
    candidate.Templated.Should().NotContain("hs-CRP", "10 percent is under the threshold");
    candidate.Templated.Should().NotContain("LDL", "LDL improved");
    candidate.Templated.Should().NotContain("TSH", "2.4 is nearer the middle of 0.3 to 4.2 than 2.0");
    candidate.Templated.Should().Contain("Sep 20, 2026 and Oct 20, 2026");
  }

  [Fact]
  public void Trend_LatestValueAtClinicianTier_IsClinician_AndSaysWhatToBring()
  {
    var previous = Panel(new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 22), ("lab.ldl", 160));
    var latest = Panel(new DateOnly(2026, 10, 20), Day, ("lab.ldl", 195));

    var candidate = new LabTrendRule().Evaluate(Context(SexAtBirth.Male, previous, latest));

    candidate.Should().NotBeNull();
    candidate!.Severity.Should().Be(InsightSeverities.Clinician);
    candidate.Templated.Should().Contain("Bring both panels, Sep 20, 2026 and Oct 20, 2026.");
  }

  [Fact]
  public void Trend_OnePanel_OrMarkerNotWatched_OrSmallMove_DoesNotFire()
  {
    new LabTrendRule().Evaluate(Context(SexAtBirth.Male, Panel(new DateOnly(2026, 10, 20), Day, ("lab.apob", 140)))).Should().BeNull("one panel is not a trend");

    var previous = Panel(new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 22), ("lab.hdl", 60), ("lab.apob", 100));
    var latest = Panel(new DateOnly(2026, 10, 20), Day, ("lab.hdl", 40), ("lab.apob", 110));
    new LabTrendRule().Evaluate(Context(SexAtBirth.Male, previous, latest)).Should().BeNull("HDL is not a watched marker and ApoB moved 10 percent");
  }

  [Fact]
  public void Engine_RunsTheLabRulesWithoutABaseline_AndRanksClinicianFirst()
  {
    var previous = Panel(new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 22), ("lab.apob", 88), ("lab.hemoglobin", 14.5));
    var latest = Panel(new DateOnly(2026, 10, 20), Day, ("lab.apob", 104), ("lab.hemoglobin", 12.0));

    var ranked = Engine.Evaluate(Context(SexAtBirth.Male, previous, latest));

    ranked.Select(c => c.Kind).Should().ContainInOrder("lab_out_of_range", "lab_trend");
    ranked.Should().Contain(c => c.Kind == "learning", "no baseline metric is established, so learning fires too");
    ranked[0].Kind.Should().Be("lab_out_of_range");
  }

  [Fact]
  public void Sentences_ReadDatesAsWords_AndLeaveOutTheUnitOfAPlainRatio()
  {
    LabRules.Spoken(new DateOnly(2026, 9, 7)).Should().Be("Sep 7, 2026");
    LabRules.Amount(3.44, "ratio").Should().Be("3.44");
    LabRules.Amount(12, "g/dL").Should().Be("12 g/dL");
  }
}
