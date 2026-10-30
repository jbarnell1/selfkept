using System.Globalization;
using SelfKept.Core.Entities;
using SelfKept.Core.Formatting;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Labs;

namespace SelfKept.Infrastructure.Services.Interpret.Rules;

/// <summary>
/// Fewer than 14 days of any baseline metric. Notice. The only rule that fires without an
/// established baseline; the engine runs it only when every other metric is still short.
/// </summary>
public sealed class LearningRule : RuleBase
{
  public override string Kind => InsightKinds.Learning;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    if (!context.StillLearning) return null;
    var most = context.Baselines.Values.Select(b => b.N28).DefaultIfEmpty(0).Max();
    return Candidate(InsightSeverities.Notice, [],
      "Still learning your normal. A couple more weeks of data and the comparisons start.",
      0,
      ("daysSoFar", most), ("daysNeeded", Baseline.MinimumDays));
  }
}

/// <summary>Shared by the two lab rules: which panel counts as current, and how numbers and dates are written.</summary>
public static class LabRules
{
  /// <summary>
  /// A panel is current for this many days after it was confirmed. The rules fire from the
  /// runner on the day a report is confirmed and again from the nightly job on the following
  /// days, then the card leaves Today on its own; the archive keeps it.
  /// </summary>
  public const int RecentDays = 7;

  public static LabPanelSnapshot? CurrentPanel(RuleContext context)
  {
    var latest = context.Labs.Latest;
    if (latest == null) return null;
    var age = context.Day.DayNumber - latest.ConfirmedOn.DayNumber;
    return age is >= -1 and <= RecentDays ? latest : null;
  }

  public static string Iso(DateOnly day) => day.ToIso();

  /// <summary>A date as a person reads it in a sentence: "Sep 27, 2026". Evidence keeps ISO.</summary>
  public static string Spoken(DateOnly day) => day.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

  /// <summary>A value with its unit, leaving out the unit for a plain ratio ("3.4", not "3.4 ratio").</summary>
  public static string Amount(double value, string? unit) =>
    string.IsNullOrEmpty(unit) || unit == "ratio" ? Num(value) : $"{Num(value)} {unit}";

  public static string Num(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

  public static string Range(LabReference r) => (r.Low, r.High) switch
  {
    ({ } low, { } high) => $"{Num(low)} to {Num(high)}",
    (null, { } high) => $"up to {Num(high)}",
    ({ } low, null) => $"{Num(low)} or more",
    _ => string.Empty,
  };
}

/// <summary>
/// A lab value outside the population range for the person's sex, on the current panel. One card
/// for the panel: the marker furthest out leads, the others are listed; severity is the highest
/// tier among them (docs/lab-ranges.md, "Flag tiers").
/// </summary>
public sealed class LabOutOfRangeRule : RuleBase
{
  public override string Kind => InsightKinds.LabOutOfRange;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    if (panel == null) return null;
    var previous = context.Labs.Panels.Skip(1).FirstOrDefault();

    var hits = new List<(LabResult Result, LabReference Reference, LabAssessment Assessment)>();
    foreach (var result in panel.Results.Values)
    {
      var reference = context.Labs.Reference(result.MetricKey);
      var assessment = LabFlagging.Assess(reference, result.Value);
      if (assessment is { OutOfRange: true, Severity: not null }) hits.Add((result, reference!, assessment));
    }
    if (hits.Count == 0) return null;

    var ordered = hits
      .OrderByDescending(h => InsightSeverities.Rank(h.Assessment.Severity!))
      .ThenByDescending(h => h.Assessment.Distance)
      .ToList();
    var lead = ordered[0];
    var severity = lead.Assessment.Severity!;
    var name = context.Labs.DisplayName(lead.Result.MetricKey);
    var direction = lead.Assessment.Position == LabPositions.Above ? "above" : "below";

    var text = $"{name} is {direction} the usual range: {LabRules.Amount(lead.Result.Value, lead.Result.Unit)} on {LabRules.Spoken(panel.CollectedOn)} (usual {LabRules.Range(lead.Reference)}).";
    var previousResult = previous?.Result(lead.Result.MetricKey);
    if (previousResult != null && previous != null)
    {
      text += $" Last time it was {LabRules.Num(previousResult.Value)} on {LabRules.Spoken(previous.CollectedOn)}.";
    }
    if (ordered.Count > 1)
    {
      var others = ordered.Skip(1).Select(h => $"{context.Labs.DisplayName(h.Result.MetricKey)} ({LabRules.Amount(h.Result.Value, h.Result.Unit)})");
      text += $" Also outside the usual range: {string.Join(", ", others)}.";
    }
    text += severity == InsightSeverities.Clinician
      ? $" Worth raising with a clinician. Bring this panel from {LabRules.Spoken(panel.CollectedOn)}" + (previous != null ? $" and the one from {LabRules.Spoken(previous.CollectedOn)}." : ".")
      : " Worth rechecking on next month's panel before reading much into it.";

    return Candidate(severity, ordered.Select(h => h.Result.MetricKey).ToList(), text, lead.Assessment.Distance,
      ("reportId", panel.ReportId), ("collectedOn", LabRules.Iso(panel.CollectedOn)),
      ("markers", ordered.Select(h => new { metric = h.Result.MetricKey, value = h.Result.Value, unit = h.Result.Unit, position = h.Assessment.Position, low = h.Reference.Low, high = h.Reference.High, severity = h.Assessment.Severity }).ToArray()),
      ("previousCollectedOn", previous == null ? null : LabRules.Iso(previous.CollectedOn)),
      ("previousValue", previousResult?.Value));
  }
}

/// <summary>
/// A watched marker moved 15 percent or more in the direction of worse across the two most
/// recent panels (ApoB, LDL, hs-CRP, fructosamine, triglycerides, ferritin, TSH, GGT). Watch,
/// or clinician when the latest value also trips the clinician tier.
/// </summary>
public sealed class LabTrendRule : RuleBase
{
  public const double MovePercent = 15;

  public override string Kind => InsightKinds.LabTrend;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    if (panel == null || context.Labs.Panels.Count < 2) return null;
    var previous = context.Labs.Panels[1];

    var moves = new List<(string Metric, LabResult Old, LabResult New, double Percent, string Severity)>();
    foreach (var metric in LabMetricKeys.TrendMarkers)
    {
      var latest = panel.Result(metric);
      var earlier = previous.Result(metric);
      if (latest == null || earlier == null || earlier.Value <= 0) continue;
      var reference = context.Labs.Reference(metric);
      if (reference == null) continue;
      var percent = (latest.Value - earlier.Value) / earlier.Value * 100;
      if (Math.Abs(percent) < MovePercent || !LabFlagging.IsWorse(reference, earlier.Value, latest.Value)) continue;
      var tier = LabFlagging.Assess(reference, latest.Value)?.Severity == InsightSeverities.Clinician ? InsightSeverities.Clinician : InsightSeverities.Watch;
      moves.Add((metric, earlier, latest, percent, tier));
    }
    if (moves.Count == 0) return null;

    var ordered = moves.OrderByDescending(m => InsightSeverities.Rank(m.Severity)).ThenByDescending(m => Math.Abs(m.Percent)).ToList();
    var lead = ordered[0];
    var severity = lead.Severity;
    var name = context.Labs.DisplayName(lead.Metric);
    var word = lead.Percent > 0 ? "up" : "down";
    var text = $"{name} moved from {LabRules.Amount(lead.Old.Value, lead.Old.Unit)} on {LabRules.Spoken(previous.CollectedOn)} to {LabRules.Amount(lead.New.Value, lead.New.Unit)} on {LabRules.Spoken(panel.CollectedOn)}, {word} {Format(Math.Abs(lead.Percent), 0)} percent.";
    if (ordered.Count > 1)
    {
      var others = ordered.Skip(1).Select(m => $"{context.Labs.DisplayName(m.Metric)} ({(m.Percent > 0 ? "up" : "down")} {Format(Math.Abs(m.Percent), 0)} percent)");
      text += $" Also moved the wrong way: {string.Join(", ", others)}.";
    }
    text += severity == InsightSeverities.Clinician
      ? $" Worth raising with a clinician. Bring both panels, {LabRules.Spoken(previous.CollectedOn)} and {LabRules.Spoken(panel.CollectedOn)}."
      : $" Two panels make a direction, not a verdict; keep both dates ({LabRules.Spoken(previous.CollectedOn)} and {LabRules.Spoken(panel.CollectedOn)}) to hand for your next appointment.";

    return Candidate(severity, ordered.Select(m => m.Metric).ToList(), text, Math.Abs(lead.Percent) / 100,
      ("reportId", panel.ReportId), ("collectedOn", LabRules.Iso(panel.CollectedOn)), ("previousCollectedOn", LabRules.Iso(previous.CollectedOn)),
      ("moves", ordered.Select(m => new { metric = m.Metric, from = m.Old.Value, to = m.New.Value, percent = Math.Round(m.Percent, 1), unit = m.New.Unit }).ToArray()));
  }
}
