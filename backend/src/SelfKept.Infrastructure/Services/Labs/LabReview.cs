using SelfKept.Core.Entities;
using SelfKept.Core.Enums;

namespace SelfKept.Infrastructure.Services.Labs;

/// <summary>
/// The verdicts a marker can get against the previous panel (docs/labs-design.md, "Lab review").
/// The first four are the proposal's; not_working is the fifth, for a change in the unwanted
/// direction that is larger than tests vary, so a real move the wrong way is never called
/// "no clear change".
/// </summary>
public static class LabVerdicts
{
  public const string Working = "working";
  public const string RightDirection = "right_direction";
  public const string NoClearChange = "no_clear_change";
  public const string NotWorking = "not_working";
  public const string CannotTell = "cannot_tell";
  public static readonly string[] All = [Working, RightDirection, NoClearChange, NotWorking, CannotTell];
}

/// <summary>Why a marker was judged "cannot tell" (or another verdict was qualified).</summary>
public static class LabVerdictReasons
{
  public const string NoPrevious = "no_previous";
  public const string NoVariationFigure = "no_variation_figure";
  public const string DrawConditionsDiffer = "draw_conditions_differ";
  public const string SubstanceAtDraw = "substance_at_draw";
  public const string NoWantedDirection = "no_wanted_direction";
}

/// <summary>What a panel looked like at the draw: its conditions and the substances active on that day.</summary>
public sealed record DrawContext(PanelConditions Conditions, IReadOnlyCollection<string> Substances)
{
  public static readonly DrawContext Unknown = new(PanelConditions.Unknown, []);

  public bool Has(string linkFrom) =>
    linkFrom.StartsWith(LabLinks.SubstancePrefix, StringComparison.Ordinal)
      ? Substances.Contains(linkFrom[LabLinks.SubstancePrefix.Length..])
      : DrawConditionKeys.Holds(linkFrom, Conditions);
}

/// <summary>One marker judged against the previous panel.</summary>
public sealed record MarkerJudgement(
  string MetricKey,
  double Value,
  double? PreviousValue,
  double? Change,
  double? ChangePercent,
  double? CviPercent,
  double? RcvPercent,
  bool? BeyondRcv,
  string? Moved,
  string Wanted,
  string Verdict,
  IReadOnlyList<string> Reasons,
  IReadOnlyList<LabLink> ReadWithCare,
  MarkerVariation? Variation);

/// <summary>
/// Pure verdict logic shared by the review endpoint and the rules. A change is called real only
/// when it is larger than the reference change value for the marker; a marker without a verified
/// variation figure, without a previous value, or drawn under a condition known to move it that
/// differs between the two panels, is "cannot tell".
/// </summary>
public static class LabReview
{
  public const string Up = "up";
  public const string Down = "down";
  public const string NoDirection = "none";

  /// <summary>
  /// The direction a marker should move: its good direction, or for a range marker the way back
  /// into the range when the earlier value sat outside it. None when there is no wanted way.
  /// </summary>
  public static string Wanted(LabReference? reference, string goodDirection, double? previous)
  {
    switch (reference?.GoodDirection ?? goodDirection)
    {
      case GoodDirections.Down:
        return Down;
      case GoodDirections.Up:
        return Up;
      default:
        if (reference == null || previous is not { } p) return NoDirection;
        if (reference.Low is { } low && p < low) return Up;
        if (reference.High is { } high && p > high) return Down;
        return NoDirection;
    }
  }

  /// <summary>The links from a draw condition or a substance that apply to this marker on the latest draw.</summary>
  public static IReadOnlyList<LabLink> ReadWithCare(string metricKey, DrawContext latest) =>
    LabLinks.ContextFor(metricKey).Where(l => latest.Has(l.From)).ToList();

  public static MarkerJudgement Judge(
    string metricKey,
    double value,
    double? previousValue,
    LabReference? reference,
    string goodDirection,
    SexAtBirth sex,
    DrawContext latest,
    DrawContext previous,
    string? wantedOverride = null)
  {
    var care = ReadWithCare(metricKey, latest);
    var variation = LabVariation.For(metricKey, sex);
    var wanted = wantedOverride ?? Wanted(reference, goodDirection, previousValue);
    var rcv = variation == null ? (double?)null : Math.Round(LabVariation.Rcv(variation.CviPercent), 1);
    var reasons = new List<string>();

    if (previousValue is not { } prev)
    {
      reasons.Add(LabVerdictReasons.NoPrevious);
      return new MarkerJudgement(metricKey, value, null, null, null, variation?.CviPercent, rcv, null, null, wanted, LabVerdicts.CannotTell, reasons, care, variation);
    }

    var change = Math.Round(value - prev, 4);
    var percent = LabVariation.ChangePercent(prev, value) is { } pc ? Math.Round(pc, 1) : (double?)null;
    var moved = change > 0 ? Up : change < 0 ? Down : NoDirection;
    bool? beyond = rcv is { } r && percent is { } p ? Math.Abs(p) > r : null;

    foreach (var link in LabLinks.ContextFor(metricKey))
    {
      if (link.From.StartsWith(LabLinks.SubstancePrefix, StringComparison.Ordinal))
      {
        // A substance that interferes or shifts the marker, on either draw, takes the comparison away.
        var onLatest = latest.Has(link.From);
        var onPrevious = previous.Has(link.From);
        var interferes = link.Direction == LinkDirections.Modifies ? onLatest || onPrevious : onLatest != onPrevious;
        if (interferes && !reasons.Contains(LabVerdictReasons.SubstanceAtDraw)) reasons.Add(LabVerdictReasons.SubstanceAtDraw);
      }
      else if (DrawConditionKeys.Differs(link.From, latest.Conditions, previous.Conditions) && !reasons.Contains(LabVerdictReasons.DrawConditionsDiffer))
      {
        reasons.Add(LabVerdictReasons.DrawConditionsDiffer);
      }
    }
    if (variation == null) reasons.Add(LabVerdictReasons.NoVariationFigure);

    string verdict;
    if (reasons.Count > 0)
    {
      verdict = LabVerdicts.CannotTell;
    }
    else if (wanted == NoDirection || moved == NoDirection)
    {
      if (wanted == NoDirection) reasons.Add(LabVerdictReasons.NoWantedDirection);
      verdict = LabVerdicts.NoClearChange;
    }
    else if (moved == wanted)
    {
      verdict = beyond == true ? LabVerdicts.Working : LabVerdicts.RightDirection;
    }
    else
    {
      verdict = beyond == true ? LabVerdicts.NotWorking : LabVerdicts.NoClearChange;
    }

    return new MarkerJudgement(metricKey, value, prev, change, percent, variation?.CviPercent, rcv, beyond, moved, wanted, verdict, reasons, care, variation);
  }

  /// <summary>The conditions that are known to differ between two draws, by name.</summary>
  public static IReadOnlyList<(string Condition, string? Now, string? Then)> Differences(DrawContext latest, DrawContext previous)
  {
    var list = new List<(string, string?, string?)>();
    var a = latest.Conditions;
    var b = previous.Conditions;
    if (a.Fasting != FastingStates.Unknown && b.Fasting != FastingStates.Unknown && a.Fasting != b.Fasting) list.Add(("fasting", a.Fasting, b.Fasting));
    if (a.DrawPeriod != DrawPeriods.Unknown && b.DrawPeriod != DrawPeriods.Unknown && a.DrawPeriod != b.DrawPeriod) list.Add(("drawPeriod", a.DrawPeriod, b.DrawPeriod));
    if (a.HardTraining is { } ht && b.HardTraining is { } pt && ht != pt) list.Add(("hardTraining", Yes(ht), Yes(pt)));
    if (a.Unwell is { } au && b.Unwell is { } bu && au != bu) list.Add(("unwell", Yes(au), Yes(bu)));
    foreach (var tag in ContextSubstances)
    {
      var now = latest.Substances.Contains(tag);
      var then = previous.Substances.Contains(tag);
      if (now != then) list.Add(("substance:" + tag, Yes(now), Yes(then)));
    }
    return list;
  }

  /// <summary>The substances some link reads (biotin, creatine).</summary>
  public static readonly IReadOnlyList<string> ContextSubstances =
    LabLinks.All.Where(l => l.From.StartsWith(LabLinks.SubstancePrefix, StringComparison.Ordinal))
      .Select(l => l.From[LabLinks.SubstancePrefix.Length..]).Distinct().ToList();

  private static string Yes(bool value) => value ? "yes" : "no";

  /// <summary>Preparation notes for the next draw, from the verified links only.</summary>
  public static IReadOnlyList<string> Prep(string metricKey, DrawContext latest)
  {
    var notes = new List<string>();
    if (metricKey == LabMetricKeys.TotalTestosterone) notes.Add("A fasting, morning draw is the one to compare.");
    if (LabLinks.ContextFor(metricKey).Any(l => l.From == DrawConditionKeys.NotFasting) && metricKey != LabMetricKeys.TotalTestosterone)
    {
      notes.Add("Draw the same way each time, fasting or not, so this compares with the last panel.");
    }
    if (latest.Substances.Contains(SubstanceTags.Biotin) && LabLinks.ContextFor(metricKey).Any(l => l.From == LabLinks.Substance(SubstanceTags.Biotin)))
    {
      notes.Add("Tell whoever orders the test that you take biotin.");
    }
    if (latest.Substances.Contains(SubstanceTags.Creatine) && LabLinks.ContextFor(metricKey).Any(l => l.From == LabLinks.Substance(SubstanceTags.Creatine)))
    {
      notes.Add("Mention that you take creatine when the result is read.");
    }
    return notes;
  }

  /// <summary>Non-fasting triglycerides at or above this suggest a fasting repeat (Nordestgaard 2016: above 5 mmol/L, 440 mg/dL).</summary>
  public const double NonFastingTriglycerideRepeat = 440;

  /// <summary>A suggested retest, only where a verified source says when or how.</summary>
  public static string? Retest(string metricKey, double value, DrawContext latest)
  {
    if (metricKey == LabMetricKeys.TotalTestosterone && (latest.Conditions.Fasting == FastingStates.No || DrawConditionKeys.Holds(DrawConditionKeys.NotMorning, latest.Conditions)))
    {
      return "A fasting, morning retest is the fair comparison.";
    }
    if (metricKey == LabMetricKeys.Triglycerides && latest.Conditions.Fasting == FastingStates.No && value > NonFastingTriglycerideRepeat)
    {
      return "Non-fasting triglycerides above 440 mg/dL (5 mmol/L) are a reason to consider a fasting repeat.";
    }
    return null;
  }
}
