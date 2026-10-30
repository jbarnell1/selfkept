using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret;
using Xunit;
using static SelfKept.UnitTests.Interpret.Synthetic;

namespace SelfKept.UnitTests.Interpret;

public class RankingTests
{
  private static InsightCandidate Candidate(string kind, string severity, double distance) =>
    new(kind, severity, [], new Dictionary<string, object?>(), kind, distance);

  [Fact]
  public void Rank_SeverityFirst_ThenDistance()
  {
    var ranked = RuleEngine.Rank(
    [
      Candidate("activity_dip", InsightSeverities.Notice, 5),
      Candidate("sleep_debt", InsightSeverities.Watch, 0.5),
      Candidate("spo2_low", InsightSeverities.Clinician, 0.1),
      Candidate("rhr_elevated", InsightSeverities.Watch, 2),
    ]);

    ranked.Select(c => c.Kind).Should().Equal("spo2_low", "rhr_elevated", "sleep_debt", "activity_dip");
  }

  [Fact]
  public void Engine_RanksWhatFired_ClinicianAhead()
  {
    var context = Context(new()
    {
      ["spo2_pct"] = Wobble(96.5, 0.6, 96, 92.4, 96, 96, 97, 96, 92.1),
      ["sleep_stage"] = Wobble(480, 20, 390, 400, 485, 395),
      ["steps"] = Steady(8000, 3000, 2500, 3200, 2800, 3100, 2900, 3000),
    });

    var ranked = new RuleEngine(RuleEngine.DefaultRules()).Evaluate(context);

    ranked.Select(c => c.Kind).Should().Equal("spo2_low", "sleep_debt", "activity_dip");
  }

  private static Insight Stored(string kind, string severity, double distance, DateTime? dismissed = null, string[]? metrics = null) => new()
  {
    Id = Guid.CreateVersion7(), PersonId = PersonId, Day = Day, Kind = kind, Severity = severity, Metrics = metrics ?? [],
    Evidence = InterpretMapping.EvidenceJson(Candidate(kind, severity, distance)), Templated = kind, DismissedAt = dismissed,
  };

  [Fact]
  public void SelectTodayCards_TwoCardsLeadingWithTheSameMetric_ShowsOnlyTheFirst()
  {
    var insights = new[]
    {
      Stored("lab_trend", InsightSeverities.Clinician, 5, metrics: ["lab.ferritin", "lab.hscrp"]),
      Stored("lab_out_of_range", InsightSeverities.Clinician, 2, metrics: ["lab.ferritin", "lab.ldl"]),
      Stored("sleep_debt", InsightSeverities.Watch, 1, metrics: ["sleep_stage"]),
    };

    var cards = InsightRanking.SelectTodayCards(insights, new HashSet<string>());

    cards.Select(c => c.Kind).Should().Equal(new[] { "lab_trend", "sleep_debt" }, "the second ferritin card would repeat the first; it stays in the archive");
  }

  [Fact]
  public void SelectTodayCards_TakesTheBestTwo_SkipsDismissedDemotedAndLearning()
  {
    var insights = new[]
    {
      Stored("learning", InsightSeverities.Notice, 0),
      Stored("activity_dip", InsightSeverities.Notice, 3),
      Stored("sleep_debt", InsightSeverities.Watch, 1),
      Stored("rhr_elevated", InsightSeverities.Watch, 4, dismissed: DateTime.UtcNow),
      Stored("hrv_low_run", InsightSeverities.Watch, 2),
      Stored("spo2_low", InsightSeverities.Clinician, 0.2),
    };

    var cards = InsightRanking.SelectTodayCards(insights, new HashSet<string> { "spo2_low" });

    cards.Select(c => c.Kind).Should().Equal("hrv_low_run", "sleep_debt");
  }

  [Fact]
  public void SelectTodayCards_WithoutDemotion_TheClinicianCardLeads()
  {
    var insights = new[]
    {
      Stored("sleep_debt", InsightSeverities.Watch, 1),
      Stored("spo2_low", InsightSeverities.Clinician, 0.2),
      Stored("activity_dip", InsightSeverities.Notice, 3),
    };

    InsightRanking.SelectTodayCards(insights, new HashSet<string>()).Select(c => c.Kind).Should().Equal("spo2_low", "sleep_debt");
  }

  [Fact]
  public void EvidenceJson_CarriesBandDistance_AndReadsBack()
  {
    var insight = Stored("sleep_debt", InsightSeverities.Watch, 1.25);
    InterpretMapping.BandDistanceOf(insight).Should().Be(1.25);
    InterpretMapping.BandDistanceOf(new Insight { Evidence = "not json" }).Should().Be(0);
  }
}
