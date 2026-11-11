using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret.Rules;
using Xunit;
using static SelfKept.UnitTests.Interpret.Synthetic;

namespace SelfKept.UnitTests.ContextEngine;

/// <summary>
/// Context before alarm: each alarming rule that context touches, under a context that explains
/// it (annotated, one severity lower), under one that does not (unchanged) and with none.
/// </summary>
public class ContextRuleTests
{
  public static RuleContext With(RuleContext c, params ActiveContext[] active) =>
    new(c.Person, c.Day, c.Baselines, c.Series, c.Labs, c.Body, new ContextState { Active = active });

  public static ActiveContext Ill(DateOnly since) => new(ContextKinds.Illness, since, null, ContextSources.Derived, $"You logged a sore throat on {LabRules.Spoken(since)}", Detail: JournalTags.SoreThroat);

  private static ActiveContext Of(string kind) => new(kind, Day.AddDays(-1), null, ContextSources.User, $"You logged {ContextKinds.Phrase(kind)} on Oct 28, 2026");

  private static RuleContext HrvRun() => Context(new() { ["hrv_rmssd_ms"] = Wobble(50, 6, 30, 31, 29) });

  [Fact]
  public void HrvLowRun_UnderHardTraining_IsAnnotatedAndDemotedToNotice()
  {
    var plain = new HrvLowRunRule().Evaluate(HrvRun())!;

    var candidate = new HrvLowRunRule().Evaluate(With(HrvRun(), Of(ContextKinds.HardTraining)))!;

    candidate.Severity.Should().Be(InsightSeverities.Notice);
    candidate.Templated.Should().StartWith(plain.Templated).And.EndWith("You logged hard training on Oct 28, 2026, which can explain readings like these.");
    candidate.Evidence["demotedFrom"].Should().Be(InsightSeverities.Watch);
    candidate.Evidence.Should().ContainKey("context");
  }

  [Fact]
  public void HrvLowRun_UnderAContextThatDoesNotExplainIt_IsUnchanged()
  {
    var plain = new HrvLowRunRule().Evaluate(HrvRun())!;

    var candidate = new HrvLowRunRule().Evaluate(With(HrvRun(), Of(ContextKinds.ScaleChange)))!;

    candidate.Should().BeEquivalentTo(plain);
  }

  [Fact]
  public void RhrElevated_UnderIllness_IsAnnotatedAndDemoted()
  {
    var context = With(Context(new() { ["resting_hr_bpm"] = Wobble(56, 4, 66, 67) }), Ill(Day.AddDays(-1)));

    var candidate = new RhrElevatedRule().Evaluate(context)!;

    candidate.Severity.Should().Be(InsightSeverities.Notice);
    candidate.Templated.Should().Contain("You logged a sore throat on Oct 28, 2026");
  }

  [Fact]
  public void RhrElevated_UnderTheCycle_IsAnnotatedAndDemoted() =>
    new RhrElevatedRule().Evaluate(With(Context(new() { ["resting_hr_bpm"] = Wobble(56, 4, 66, 67) }), Of(ContextKinds.CyclePhase)))!
      .Templated.Should().Contain("a point in your cycle");

  [Fact]
  public void IllnessPattern_UnderLoggedIllness_IsDemotedToNotice()
  {
    var series = new Dictionary<string, double?[]> { ["resting_hr_bpm"] = Wobble(56, 4, 66, 67), ["wrist_temp_delta_c"] = Wobble(0, 0.2, 0.9) };

    new IllnessPatternRule().Evaluate(Context(series))!.Severity.Should().Be(InsightSeverities.Watch);
    new IllnessPatternRule().Evaluate(With(Context(series), Ill(Day)))!.Severity.Should().Be(InsightSeverities.Notice);
  }

  [Fact]
  public void SleepDebt_UnderTravel_IsDemoted_UnderHardTraining_IsNot()
  {
    var context = Context(new() { ["sleep_stage"] = Steady(470, 380, 375, 470, 370) });

    new SleepDebtRule().Evaluate(With(context, Of(ContextKinds.Travel)))!.Severity.Should().Be(InsightSeverities.Notice);
    new SleepDebtRule().Evaluate(With(context, Of(ContextKinds.HardTraining)))!.Severity.Should().Be(InsightSeverities.Watch);
  }

  [Fact]
  public void ActivityDip_UnderIllness_IsAnnotated_AndStaysNotice()
  {
    var context = With(Context(new() { ["steps"] = Steady(8000, 3000, 2500, 3200, 2800, 3100, 2900, 3000) }), Ill(Day.AddDays(-2)));

    var candidate = new ActivityDipRule().Evaluate(context)!;

    candidate.Severity.Should().Be(InsightSeverities.Notice);
    candidate.Evidence.Should().NotContainKey("demotedFrom", "notice is already the lowest step");
    candidate.Templated.Should().Contain("which can explain readings like these");
  }

  [Fact]
  public void WeightTrend_UnderAScaleChange_IsAnnotated()
  {
    var values = new double?[28];
    for (var i = 0; i < 28; i++) values[i] = 80 - 0.06 * i;

    var candidate = new WeightTrendRule().Evaluate(With(Context(new() { ["weight_kg"] = values }), Of(ContextKinds.ScaleChange)))!;

    candidate.Templated.Should().Contain("a change of scale");
  }

  [Fact]
  public void Spo2Low_UnderIllness_IsAnnotated_ButStaysClinician()
  {
    var context = With(Context(new() { ["spo2_pct"] = Wobble(96.5, 0.6, 96, 92.4, 96, 96, 97, 96, 92.1) }), Ill(Day));

    var candidate = new Spo2LowRule().Evaluate(context)!;

    candidate.Severity.Should().Be(InsightSeverities.Clinician, "a clinician card is never stepped down");
    candidate.Templated.Should().Contain("You logged a sore throat");
  }

  [Fact]
  public void Explain_WithTwoExplainingKinds_UsesTheFirstInTheRuleOrder()
  {
    var context = With(HrvRun(), Of(ContextKinds.Travel), Ill(Day.AddDays(-3)));

    new HrvLowRunRule().Evaluate(context)!.Templated.Should().Contain("sore throat", "illness comes first in the recovery list");
  }

  [Fact]
  public void Demote_StepsDownOnce() =>
    new[] { ContextNotes.Demote(InsightSeverities.Clinician), ContextNotes.Demote(InsightSeverities.Watch), ContextNotes.Demote(InsightSeverities.Notice) }
      .Should().Equal(InsightSeverities.Watch, InsightSeverities.Notice, InsightSeverities.Notice);
}
