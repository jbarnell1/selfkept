using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Labs;

namespace SelfKept.Infrastructure.Services.Interpret.Rules;

/// <summary>
/// Shared by the cross-reference rules (docs/cross-reference.md, "Rules"). Every threshold here
/// is one whose source was opened and whose figure was seen there; a rule without a verified
/// number says what it saw in plain words and uses the reference table's own usual range.
/// </summary>
public static class CrossReferenceRules
{
  /// <summary>Triglycerides at or above this: labs usually do not report a calculated LDL (Sampson 2020; Martin 2013).</summary>
  public const double CalculatedLdlTriglycerides = 400;

  /// <summary>With inflammation, a ferritin below this may indicate iron deficiency in adults (WHO 2020).</summary>
  public const double FerritinWithInflammation = 70;

  /// <summary>Metabolic hyperferritinaemia starts above these (Valenti 2023): men 300, women 200 ng/mL.</summary>
  public const double HighFerritinMen = 300;
  public const double HighFerritinWomen = 200;

  /// <summary>A transferrin saturation persistently above this excludes the metabolic explanation (Valenti 2023).</summary>
  public const double TransferrinSaturationOverload = 50;

  public static DrawContext Draw(LabPanelSnapshot panel) => new(panel.Conditions, panel.Substances);

  public static bool Above(LabReference? reference, double value) => reference?.High is { } high && value > high;

  public static bool Below(LabReference? reference, double value) => reference?.Low is { } low && value < low;

  /// <summary>The previous panel, when the current one has one before it.</summary>
  public static LabPanelSnapshot? Previous(RuleContext context) => context.Labs.Panels.Count > 1 ? context.Labs.Panels[1] : null;

  public static string Name(RuleContext context, string key) => context.Labs.DisplayName(key);

  public static MarkerJudgement Judge(RuleContext context, string key, LabPanelSnapshot latest, LabPanelSnapshot? previous)
  {
    var result = latest.Result(key)!;
    var reference = context.Labs.Reference(key);
    return LabReview.Judge(key, result.Value, previous?.Result(key)?.Value, reference, reference?.GoodDirection ?? GoodDirections.Range,
      context.Labs.Sex, Draw(latest), previous == null ? DrawContext.Unknown : Draw(previous));
  }

  /// <summary>How a difference in draw conditions reads in a sentence.</summary>
  public static string Phrase(string condition, string? now, string? then) => condition switch
  {
    "fasting" => now == FastingStates.Yes ? "fasting this time, not last time" : "not fasting this time, fasting last time",
    "drawPeriod" => $"drawn in the {now} this time, in the {then} last time",
    "hardTraining" => now == "yes" ? "hard training before this draw, not before the last" : "hard training before the last draw, not this one",
    "unwell" => now == "yes" ? "unwell around this draw, not the last" : "unwell around the last draw, not this one",
    _ when condition.StartsWith("substance:", StringComparison.Ordinal) =>
      now == "yes" ? $"{Tag(condition)} on your list at this draw, not the last" : $"{Tag(condition)} on your list at the last draw, not this one",
    _ => condition,
  };

  private static string Tag(string condition) => condition["substance:".Length..].Replace('_', ' ');

  public static string List(IEnumerable<string> items)
  {
    var list = items.ToList();
    return list.Count switch
    {
      0 => string.Empty,
      1 => list[0],
      _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
    };
  }
}

/// <summary>
/// A trial has something to read: a panel collected after its start has just come in, or its
/// planned read date has arrived (for a trial on blood markers, only once a panel after the start
/// exists). Notice. Fires for <see cref="LabRules.RecentDays"/> days after the trigger.
/// </summary>
public sealed class TrialReadyRule : RuleBase
{
  public override string Kind => InsightKinds.TrialReady;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    var latest = context.Labs.Latest;
    foreach (var trial in context.Labs.Trials)
    {
      var panelAfterStart = latest != null && latest.CollectedOn > trial.StartedOn;
      var sinceRead = context.Day.DayNumber - trial.ReadOn.DayNumber;
      var readDue = sinceRead is >= 0 and <= LabRules.RecentDays && (!trial.HasLabMarkers || panelAfterStart);
      var newPanel = trial.HasLabMarkers && panel != null && panel.CollectedOn > trial.StartedOn;
      if (!readDue && !newPanel) continue;

      var others = context.Labs.Trials.Count(t => t.Id != trial.Id) > 0 ? " Other trials running at the same time share the credit for any change." : string.Empty;
      var text = newPanel
        ? $"A panel from {LabRules.Spoken(panel!.CollectedOn)} has come in since the trial you started on {LabRules.Spoken(trial.StartedOn)}. The lab review sets its markers against the panel before it and against how much each test varies by itself.{others}"
        : $"The trial you started on {LabRules.Spoken(trial.StartedOn)} has reached its read date, {LabRules.Spoken(trial.ReadOn)}. Its readings are ready to compare with where they started.{others}";
      return Candidate(InsightSeverities.Notice, trial.MetricKeys, text, 0,
        ("trialId", trial.Id), ("startedOn", LabRules.Iso(trial.StartedOn)), ("readOn", LabRules.Iso(trial.ReadOn)),
        ("reportId", newPanel ? panel!.ReportId : null), ("trigger", newPanel ? "panel" : "read_date"));
    }
    return null;
  }
}

/// <summary>
/// A trial on blood markers has reached its read date and no panel has been collected since it
/// started: the markers can only be read from a new blood test. Notice, for
/// <see cref="LabRules.RecentDays"/> days from the read date. The timing is the person's own plan.
/// </summary>
public sealed class RetestDueRule : RuleBase
{
  public override string Kind => InsightKinds.RetestDue;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var latest = context.Labs.Latest;
    foreach (var trial in context.Labs.Trials.Where(t => t.HasLabMarkers))
    {
      var sinceRead = context.Day.DayNumber - trial.ReadOn.DayNumber;
      if (sinceRead is < 0 or > LabRules.RecentDays) continue;
      if (latest != null && latest.CollectedOn > trial.StartedOn) continue;

      var markers = trial.MetricKeys.Where(k => k.StartsWith(MetricKeys.LabPrefix, StringComparison.Ordinal)).ToList();
      var names = CrossReferenceRules.List(markers.Select(k => context.Labs.DisplayName(k)));
      var prep = markers.SelectMany(k => LabReview.Prep(k, DrawContext.Unknown)).Distinct().ToList();
      var text = $"The trial you started on {LabRules.Spoken(trial.StartedOn)} reached its read date on {LabRules.Spoken(trial.ReadOn)}, and no panel has been taken since it started. {names} can only be read from a blood test, so a panel now would answer it."
        + (prep.Count > 0 ? " " + string.Join(" ", prep) : string.Empty);
      return Candidate(InsightSeverities.Notice, markers, text, 0,
        ("trialId", trial.Id), ("startedOn", LabRules.Iso(trial.StartedOn)), ("readOn", LabRules.Iso(trial.ReadOn)),
        ("lastPanelOn", latest == null ? null : LabRules.Iso(latest.CollectedOn)));
    }
    return null;
  }
}

/// <summary>
/// The current panel was drawn under different conditions from the one before: fasting, time of
/// day, hard training, illness, or a substance the links read (biotin, creatine). Notice. Names the
/// markers on both panels that a verified link says the difference can move.
/// </summary>
public sealed class DrawConditionsDifferRule : RuleBase
{
  public override string Kind => InsightKinds.DrawConditionsDiffer;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    var previous = CrossReferenceRules.Previous(context);
    if (panel == null || previous == null) return null;
    var differences = LabReview.Differences(CrossReferenceRules.Draw(panel), CrossReferenceRules.Draw(previous));
    if (differences.Count == 0) return null;

    var froms = new HashSet<string>(StringComparer.Ordinal);
    foreach (var (condition, _, _) in differences)
    {
      if (condition == "fasting") froms.Add(DrawConditionKeys.NotFasting);
      else if (condition == "drawPeriod") froms.Add(DrawConditionKeys.NotMorning);
      else if (condition.StartsWith("substance:", StringComparison.Ordinal)) froms.Add(LabLinks.SubstancePrefix + condition["substance:".Length..]);
    }
    var affected = LabLinks.All
      .Where(l => froms.Contains(l.From) && l.Direction != LinkDirections.None && panel.Result(l.To) != null && previous.Result(l.To) != null)
      .Select(l => l.To).Distinct().OrderBy(LabPanel.OrderOf).ToList();

    var text = $"This panel was drawn differently from the one on {LabRules.Spoken(previous.CollectedOn)}: {CrossReferenceRules.List(differences.Select(d => CrossReferenceRules.Phrase(d.Condition, d.Now, d.Then)))}.";
    text += affected.Count > 0
      ? $" {CrossReferenceRules.List(affected.Select(k => context.Labs.DisplayName(k)))} can move with that, so the review reads {(affected.Count == 1 ? "it" : "them")} with care rather than as a change."
      : " None of the markers on both panels is known to move with that.";
    return Candidate(InsightSeverities.Notice, affected, text, 0,
      ("reportId", panel.ReportId), ("previousReportId", previous.ReportId),
      ("differences", differences.Select(d => new { condition = d.Condition, now = d.Now, then = d.Then }).ToArray()));
  }
}

/// <summary>
/// ApoB and LDL disagree on the current panel: one is above its line and the other is not, or they
/// moved in opposite directions since the last panel by at least the app's 15 percent lab_trend
/// line each. ApoB's line is its desirable bound (90 mg/dL, Mayo) and LDL's its usual upper bound
/// (100 mg/dL). Notice (Marston 2022; Johannesen 2021).
/// </summary>
public sealed class ApoBLdlDisagreeRule : RuleBase
{
  public override string Kind => InsightKinds.ApoBLdlDisagree;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    var apob = panel?.Result(LabMetricKeys.ApoB);
    var ldl = panel?.Result(LabMetricKeys.Ldl);
    if (panel == null || apob == null || ldl == null) return null;

    var apobLine = context.Labs.Reference(LabMetricKeys.ApoB) is { } ar ? ar.OptimalHigh ?? ar.High : null;
    var ldlLine = context.Labs.Reference(LabMetricKeys.Ldl)?.High;
    if (apobLine == null || ldlLine == null) return null;
    var apobAbove = apob.Value > apobLine;
    var ldlAbove = ldl.Value > ldlLine;

    string? text = null;
    if (apobAbove != ldlAbove)
    {
      text = apobAbove
        ? $"ApoB is {LabRules.Amount(apob.Value, apob.Unit)}, above the line of {LabRules.Num(apobLine.Value)}, while LDL is {LabRules.Amount(ldl.Value, ldl.Unit)}, inside its line of {LabRules.Num(ldlLine.Value)}."
        : $"LDL is {LabRules.Amount(ldl.Value, ldl.Unit)}, above its line of {LabRules.Num(ldlLine.Value)}, while ApoB is {LabRules.Amount(apob.Value, apob.Unit)}, inside the line of {LabRules.Num(apobLine.Value)}.";
    }
    var previous = CrossReferenceRules.Previous(context);
    double? apobPct = null, ldlPct = null;
    if (text == null && previous?.Result(LabMetricKeys.ApoB) is { } pa && previous.Result(LabMetricKeys.Ldl) is { } pl)
    {
      apobPct = LabVariation.ChangePercent(pa.Value, apob.Value);
      ldlPct = LabVariation.ChangePercent(pl.Value, ldl.Value);
      if (apobPct is { } a && ldlPct is { } l && Math.Sign(a) != Math.Sign(l) && Math.Abs(a) >= LabTrendRule.MovePercent && Math.Abs(l) >= LabTrendRule.MovePercent)
      {
        text = $"ApoB and LDL moved in opposite directions since {LabRules.Spoken(previous.CollectedOn)}: ApoB {(a > 0 ? "up" : "down")} {Format(Math.Abs(a), 0)} percent to {LabRules.Amount(apob.Value, apob.Unit)}, LDL {(l > 0 ? "up" : "down")} {Format(Math.Abs(l), 0)} percent to {LabRules.Amount(ldl.Value, ldl.Unit)}.";
      }
    }
    if (text == null) return null;

    text += " The two usually move together. When they disagree, ApoB tracks heart risk more closely, so the lipid card leads with ApoB.";
    return Candidate(InsightSeverities.Notice, [LabMetricKeys.ApoB, LabMetricKeys.Ldl], text, 0,
      ("reportId", panel.ReportId), ("apob", apob.Value), ("ldl", ldl.Value), ("apobLine", apobLine), ("ldlLine", ldlLine),
      ("apobChangePercent", apobPct is { } x ? Math.Round(x, 1) : null), ("ldlChangePercent", ldlPct is { } y ? Math.Round(y, 1) : null),
      ("link", "apob_ldl"));
  }
}

/// <summary>Triglycerides of 400 mg/dL or more with an LDL on the panel: a calculated LDL carries large errors there. Notice (Sampson 2020).</summary>
public sealed class CalculatedLdlRule : RuleBase
{
  public override string Kind => InsightKinds.CalculatedLdlUnreliable;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    var tg = panel?.Result(LabMetricKeys.Triglycerides);
    var ldl = panel?.Result(LabMetricKeys.Ldl);
    if (panel == null || tg == null || ldl == null || tg.Value < CrossReferenceRules.CalculatedLdlTriglycerides) return null;

    var text = $"Triglycerides were {LabRules.Amount(tg.Value, tg.Unit)} on {LabRules.Spoken(panel.CollectedOn)}. Most labs calculate LDL from total cholesterol, HDL and triglycerides, and usually do not report a calculated LDL at 400 or more because the estimate carries large errors there. If this LDL of {LabRules.Amount(ldl.Value, ldl.Unit)} was calculated, it is best read with care, and ApoB, where the panel has it, is the steadier read.";
    if (panel.Conditions.Fasting == FastingStates.No) text += " This draw was not fasting, which lifts triglycerides.";
    return Candidate(InsightSeverities.Notice, [LabMetricKeys.Ldl, LabMetricKeys.Triglycerides], text, 0,
      ("reportId", panel.ReportId), ("triglycerides", tg.Value), ("ldl", ldl.Value), ("threshold", CrossReferenceRules.CalculatedLdlTriglycerides), ("link", "ldl_triglycerides"));
  }
}

/// <summary>Ferritin under 70 ng/mL while hs-CRP is above its usual range on the same panel: low iron is not ruled out. Notice (WHO 2020).</summary>
public sealed class FerritinInflammationRule : RuleBase
{
  public override string Kind => InsightKinds.FerritinInflammation;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    var ferritin = panel?.Result(LabMetricKeys.Ferritin);
    var crp = panel?.Result(LabMetricKeys.HsCrp);
    if (panel == null || ferritin == null || crp == null) return null;
    if (ferritin.Value >= CrossReferenceRules.FerritinWithInflammation || !CrossReferenceRules.Above(context.Labs.Reference(LabMetricKeys.HsCrp), crp.Value)) return null;

    var text = $"Ferritin was {LabRules.Amount(ferritin.Value, ferritin.Unit)} with hs-CRP at {LabRules.Amount(crp.Value, crp.Unit)}, above its usual range. Inflammation raises ferritin and can hide low iron: with inflammation present, WHO guidance treats a ferritin below 70 as possibly low. So low iron is not ruled out, and a retest once any infection or injury has settled reads cleaner.";
    return Candidate(InsightSeverities.Notice, [LabMetricKeys.Ferritin, LabMetricKeys.HsCrp], text, 0,
      ("reportId", panel.ReportId), ("ferritin", ferritin.Value), ("hscrp", crp.Value), ("threshold", CrossReferenceRules.FerritinWithInflammation), ("link", "ferritin_hscrp"));
  }
}

/// <summary>
/// Ferritin above 300 ng/mL (men, and anyone without a sex set) or 200 (women), shown with what
/// the panel says about triglycerides, HDL, hs-CRP and transferrin saturation. Clinician (Valenti 2023).
/// </summary>
public sealed class HighFerritinRule : RuleBase
{
  public override string Kind => InsightKinds.HighFerritinContext;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    var ferritin = panel?.Result(LabMetricKeys.Ferritin);
    if (panel == null || ferritin == null) return null;
    var threshold = context.Labs.Sex == SexAtBirth.Female ? CrossReferenceRules.HighFerritinWomen : CrossReferenceRules.HighFerritinMen;
    if (ferritin.Value <= threshold) return null;

    var metrics = new List<string> { LabMetricKeys.Ferritin };
    var context1 = new List<string>();
    if (panel.Result(LabMetricKeys.Triglycerides) is { } tg && CrossReferenceRules.Above(context.Labs.Reference(LabMetricKeys.Triglycerides), tg.Value))
    {
      context1.Add($"triglycerides {LabRules.Amount(tg.Value, tg.Unit)} (above range)");
      metrics.Add(LabMetricKeys.Triglycerides);
    }
    if (panel.Result(LabMetricKeys.Hdl) is { } hdl && CrossReferenceRules.Below(context.Labs.Reference(LabMetricKeys.Hdl), hdl.Value))
    {
      context1.Add($"HDL {LabRules.Amount(hdl.Value, hdl.Unit)} (below range)");
      metrics.Add(LabMetricKeys.Hdl);
    }
    if (panel.Result(LabMetricKeys.HsCrp) is { } crp && CrossReferenceRules.Above(context.Labs.Reference(LabMetricKeys.HsCrp), crp.Value))
    {
      context1.Add($"hs-CRP {LabRules.Amount(crp.Value, crp.Unit)} (raised, which lifts ferritin too)");
      metrics.Add(LabMetricKeys.HsCrp);
    }

    var text = $"Ferritin was {LabRules.Amount(ferritin.Value, ferritin.Unit)}, above {LabRules.Num(threshold)}. Alongside metabolic features such as high triglycerides, low HDL or higher weight, a ferritin like this usually reflects metabolic health rather than iron overload.";
    if (context1.Count > 0) text += $" On this panel: {CrossReferenceRules.List(context1)}.";
    var tsat = panel.Result(LabMetricKeys.TransferrinSaturation);
    if (tsat != null)
    {
      metrics.Add(LabMetricKeys.TransferrinSaturation);
      text += tsat.Value > CrossReferenceRules.TransferrinSaturationOverload
        ? $" Transferrin saturation was {LabRules.Num(tsat.Value)} %, above 50 %; a saturation that stays above 50 % points away from the metabolic explanation."
        : $" Transferrin saturation was {LabRules.Num(tsat.Value)} %, not above 50 %.";
    }
    else
    {
      text += " Transferrin saturation, which helps tell the two apart, is not on this panel.";
    }
    text += $" Worth raising with a clinician. Bring this panel from {LabRules.Spoken(panel.CollectedOn)}.";
    return Candidate(InsightSeverities.Clinician, metrics, text, (ferritin.Value - threshold) / threshold,
      ("reportId", panel.ReportId), ("ferritin", ferritin.Value), ("threshold", threshold), ("transferrinSaturation", tsat?.Value), ("link", "ferritin_triglycerides"));
  }
}

/// <summary>
/// TSH above its usual range on a panel where LDL or total cholesterol is above range or higher
/// than last time. Clinician: guidelines list an underactive thyroid among the causes of raised
/// cholesterol to rule out (NICE NG238; Abreu 2017).
/// </summary>
public sealed class ThyroidCholesterolRule : RuleBase
{
  public override string Kind => InsightKinds.ThyroidCholesterol;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    var tsh = panel?.Result(LabMetricKeys.Tsh);
    if (panel == null || tsh == null || !CrossReferenceRules.Above(context.Labs.Reference(LabMetricKeys.Tsh), tsh.Value)) return null;
    var previous = CrossReferenceRules.Previous(context);

    var lipids = new List<(string Key, string Phrase)>();
    foreach (var key in new[] { LabMetricKeys.Ldl, LabMetricKeys.TotalCholesterol })
    {
      if (panel.Result(key) is not { } r) continue;
      var earlier = previous?.Result(key);
      if (CrossReferenceRules.Above(context.Labs.Reference(key), r.Value)) lipids.Add((key, $"{context.Labs.DisplayName(key)} {LabRules.Amount(r.Value, r.Unit)}, above range"));
      else if (earlier != null && r.Value > earlier.Value) lipids.Add((key, $"{context.Labs.DisplayName(key)} {LabRules.Amount(r.Value, r.Unit)}, up from {LabRules.Num(earlier.Value)}"));
    }
    if (lipids.Count == 0) return null;

    var text = $"TSH was {LabRules.Amount(tsh.Value, tsh.Unit)}, above its usual range, on a panel with {CrossReferenceRules.List(lipids.Select(l => l.Phrase))}. An underactive thyroid raises cholesterol, and guidelines list it among the causes to rule out before putting a rise down to diet. Worth raising with a clinician. Bring this panel from {LabRules.Spoken(panel.CollectedOn)}"
      + (previous != null ? $" and the one from {LabRules.Spoken(previous.CollectedOn)}." : ".");
    if (panel.Substances.Contains(SubstanceTags.Biotin)) text += " Biotin is on your list, and it can interfere with some thyroid test methods; mention it.";
    return Candidate(InsightSeverities.Clinician, [LabMetricKeys.Tsh, .. lipids.Select(l => l.Key)], text, 0,
      ("reportId", panel.ReportId), ("tsh", tsh.Value), ("lipids", lipids.Select(l => l.Key).ToArray()), ("link", "tsh_ldl"));
  }
}

/// <summary>
/// SHBG outside its usual range with a total testosterone on the panel. Notice. Plain wording only:
/// no verified source sets a number here, so the rule states the relationship and points to free
/// testosterone and a clinician.
/// </summary>
public sealed class FreeTestosteroneShbgRule : RuleBase
{
  public override string Kind => InsightKinds.FreeTestosteroneShbg;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    var shbg = panel?.Result(LabMetricKeys.Shbg);
    var total = panel?.Result(LabMetricKeys.TotalTestosterone);
    if (panel == null || shbg == null || total == null) return null;
    var reference = context.Labs.Reference(LabMetricKeys.Shbg);
    var above = CrossReferenceRules.Above(reference, shbg.Value);
    if (!above && !CrossReferenceRules.Below(reference, shbg.Value)) return null;

    var free = panel.Result(LabMetricKeys.FreeTestosterone);
    var text = $"SHBG was {LabRules.Amount(shbg.Value, shbg.Unit)}, {(above ? "above" : "below")} its usual range. Total testosterone ({LabRules.Amount(total.Value, total.Unit)}) moves with SHBG, so when SHBG is out of range the total alone can mislead.";
    text += free != null
      ? $" Free testosterone on the same panel ({LabRules.Amount(free.Value, free.Unit)}) is worth reading beside it."
      : " Free testosterone is not on this panel; a clinician can work it out from total testosterone, SHBG and albumin.";
    return Candidate(InsightSeverities.Notice, [LabMetricKeys.Shbg, LabMetricKeys.TotalTestosterone], text, 0,
      ("reportId", panel.ReportId), ("shbg", shbg.Value), ("totalTestosterone", total.Value), ("freeTestosterone", free?.Value));
  }
}

/// <summary>Creatine on the list at the draw with creatinine above range or eGFR below it. Notice (Antonio 2021).</summary>
public sealed class CreatinineMuscleRule : RuleBase
{
  public override string Kind => InsightKinds.CreatinineMuscle;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    if (panel == null || !panel.Substances.Contains(SubstanceTags.Creatine)) return null;
    var creatinine = panel.Result(LabMetricKeys.Creatinine);
    var egfr = panel.Result(LabMetricKeys.Egfr);
    var high = creatinine != null && CrossReferenceRules.Above(context.Labs.Reference(LabMetricKeys.Creatinine), creatinine.Value);
    var low = egfr != null && CrossReferenceRules.Below(context.Labs.Reference(LabMetricKeys.Egfr), egfr.Value);
    if (!high && !low) return null;

    var seen = new List<string>();
    if (high) seen.Add($"creatinine {LabRules.Amount(creatinine!.Value, creatinine.Unit)}, above its usual range");
    if (low) seen.Add($"eGFR {LabRules.Amount(egfr!.Value, egfr.Unit)}, below its usual range");
    var text = $"This panel shows {CrossReferenceRules.List(seen)}, and creatine is on your list. Creatine supplements can raise blood creatinine, and so lower the eGFR worked out from it, without any loss of kidney function. Mention the creatine when the result is read.";
    return Candidate(InsightSeverities.Notice, [LabMetricKeys.Creatinine, LabMetricKeys.Egfr], text, 0,
      ("reportId", panel.ReportId), ("creatinine", creatinine?.Value), ("egfr", egfr?.Value), ("link", "creatine_creatinine"));
  }
}

/// <summary>
/// Testosterone on the list at the draw and haematocrit above its usual range. Clinician. No
/// stop line is quoted: the figure was not confirmed at the guideline it is usually credited to.
/// </summary>
public sealed class HematocritTestosteroneRule : RuleBase
{
  public override string Kind => InsightKinds.HematocritTestosterone;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    var hct = panel?.Result(LabMetricKeys.Hematocrit);
    if (panel == null || hct == null || !panel.Substances.Contains(SubstanceTags.Testosterone)) return null;
    if (!CrossReferenceRules.Above(context.Labs.Reference(LabMetricKeys.Hematocrit), hct.Value)) return null;

    var previous = CrossReferenceRules.Previous(context);
    var earlier = previous?.Result(LabMetricKeys.Hematocrit);
    var text = $"Haematocrit was {LabRules.Amount(hct.Value, hct.Unit)}, above its usual range"
      + (earlier != null && hct.Value > earlier.Value ? $" and up from {LabRules.Num(earlier.Value)} on {LabRules.Spoken(previous!.CollectedOn)}" : string.Empty)
      + ", and testosterone is on your medicines list. Testosterone therapy raises haematocrit, and prescribers keep an eye on it. Worth raising with whoever prescribes it. Bring this panel"
      + (previous != null ? $" and the one from {LabRules.Spoken(previous.CollectedOn)}." : ".");
    return Candidate(InsightSeverities.Clinician, [LabMetricKeys.Hematocrit], text, 0,
      ("reportId", panel.ReportId), ("hematocrit", hct.Value), ("previous", earlier?.Value));
  }
}

/// <summary>
/// A marker with a verified variation figure moved by the app's 15 percent lab_trend line or more,
/// but by no more than its reference change value, under the same draw conditions. Notice: the
/// swing is labelled as expected test-to-test variation and lab_trend holds its card for it.
/// </summary>
public sealed class ExpectedSwingRule : RuleBase
{
  public override string Kind => InsightKinds.ExpectedSwing;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    var previous = CrossReferenceRules.Previous(context);
    if (panel == null || previous == null) return null;

    var swings = ExpectedSwings(context, panel, previous);
    if (swings.Count == 0) return null;
    var lead = swings[0];
    var text = $"{context.Labs.DisplayName(lead.MetricKey)} moved from {LabRules.Num(lead.PreviousValue!.Value)} to {LabRules.Num(lead.Value)}, {(lead.ChangePercent > 0 ? "up" : "down")} {Format(Math.Abs(lead.ChangePercent!.Value), 0)} percent. Two tests of {context.Labs.DisplayName(lead.MetricKey)} can differ by up to about {Format(lead.RcvPercent!.Value, 0)} percent with nothing changed, so this is not read as a change yet.";
    if (swings.Count > 1) text += $" The same holds for {CrossReferenceRules.List(swings.Skip(1).Select(s => context.Labs.DisplayName(s.MetricKey)))}.";
    return Candidate(InsightSeverities.Notice, swings.Select(s => s.MetricKey).ToList(), text, 0,
      ("reportId", panel.ReportId), ("previousReportId", previous.ReportId),
      ("swings", swings.Select(s => new { metric = s.MetricKey, from = s.PreviousValue, to = s.Value, percent = s.ChangePercent, rcv = s.RcvPercent }).ToArray()));
  }

  /// <summary>The moves at or above the lab_trend line that stay inside the marker's RCV, largest first.</summary>
  public static IReadOnlyList<MarkerJudgement> ExpectedSwings(RuleContext context, LabPanelSnapshot panel, LabPanelSnapshot previous) =>
    LabVariation.All
      .Where(v => panel.Result(v.MetricKey) != null && previous.Result(v.MetricKey) != null)
      .Select(v => CrossReferenceRules.Judge(context, v.MetricKey, panel, previous))
      .Where(j => j.RcvPercent != null && j.ChangePercent is { } p && Math.Abs(p) >= LabTrendRule.MovePercent && j.BeyondRcv == false
        && !j.Reasons.Contains(LabVerdictReasons.DrawConditionsDiffer) && !j.Reasons.Contains(LabVerdictReasons.SubstanceAtDraw))
      .OrderByDescending(j => Math.Abs(j.ChangePercent!.Value))
      .ToList();
}

/// <summary>A panel was confirmed and there is one before it: the review is ready, with how many markers got each verdict. Notice.</summary>
public sealed class LabReviewReadyRule : RuleBase
{
  public override string Kind => InsightKinds.LabReviewReady;

  public override IReadOnlyList<string> MetricsUsed(RuleContext context) => [];

  public override InsightCandidate? Evaluate(RuleContext context)
  {
    var panel = LabRules.CurrentPanel(context);
    var previous = CrossReferenceRules.Previous(context);
    if (panel == null || previous == null) return null;

    var judgements = panel.Results.Keys
      .Where(k => previous.Result(k) != null)
      .Select(k => CrossReferenceRules.Judge(context, k, panel, previous))
      .ToList();
    if (judgements.Count == 0) return null;
    var counts = LabVerdicts.All.ToDictionary(v => v, v => judgements.Count(j => j.Verdict == v));
    var words = new List<string>();
    void Say(string verdict, string phrase)
    {
      if (counts[verdict] > 0) words.Add($"{counts[verdict]} {phrase}");
    }
    Say(LabVerdicts.Working, "moved the better way by more than tests vary");
    Say(LabVerdicts.RightDirection, "moved the better way but not clearly yet");
    Say(LabVerdicts.NotWorking, "moved the other way by more than tests vary");
    Say(LabVerdicts.NoClearChange, "with no clear change");
    Say(LabVerdicts.CannotTell, "that cannot be judged from one pair of panels");

    var text = $"The review of your {LabRules.Spoken(panel.CollectedOn)} panel against {LabRules.Spoken(previous.CollectedOn)} is ready: {CrossReferenceRules.List(words)}.";
    return Candidate(InsightSeverities.Notice, [], text, 0,
      ("reportId", panel.ReportId), ("previousReportId", previous.ReportId), ("verdicts", counts));
  }
}
