using System.Globalization;
using System.Text;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Body;
using SelfKept.Infrastructure.Services.Collect;
using SelfKept.Infrastructure.Services.Interpret;
using SelfKept.Infrastructure.Services.Labs;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>One titled block of the brief, in priority order.</summary>
public sealed record BriefSection(string Title, IReadOnlyList<string> Lines);

/// <summary>
/// Joins the sections into the brief, most important first, and stops before the cap: a line
/// that would pass it is left out together with everything after it, so the goal and today's
/// readings always survive and the older day log is what gets cut. Pure.
/// </summary>
public static class StateBriefComposer
{
  public const int Cap = 6000;

  public static StateBrief Compose(IReadOnlyList<BriefSection> sections, IReadOnlyList<PlaybookMatch> playbook, BriefFacts facts, int cap = Cap)
  {
    var text = new StringBuilder();
    var full = false;
    foreach (var section in sections)
    {
      if (full) break;
      if (section.Lines.Count == 0) continue;
      var heading = section.Title + ":";
      if (text.Length + heading.Length + 2 > cap) break;
      var block = new StringBuilder();
      block.Append(heading);
      var lines = 0;
      foreach (var line in section.Lines)
      {
        if (text.Length + block.Length + line.Length + 3 > cap)
        {
          full = true;
          break;
        }
        block.Append('\n').Append("- ").Append(line);
        lines++;
      }
      if (lines == 0) break;
      if (text.Length > 0) text.Append("\n\n");
      text.Append(block);
    }
    var brief = text.ToString();
    return new StateBrief(brief, GuardNumbers.Of(brief), playbook, facts);
  }
}

/// <summary>
/// Builds the brief from the person's own record (docs/assistant-design.md, "State brief"), by
/// code and with no tool calls by the model: goals, today's targets, readings against their
/// usual, today's insights, applicable playbook entries, medicines and supplements with their
/// ingredients, the latest blood panel's out-of-range markers, remembered facts, and seven days
/// of the day log. Every read is a server-internal own read.
/// </summary>
public sealed class StateBriefBuilder : IStateBriefBuilder
{
  private static readonly string[] ReadingKeys =
  [
    MetricKeys.SleepStage, MetricKeys.HrvRmssdMs, MetricKeys.HrvSdnnMs, MetricKeys.RestingHrBpm, MetricKeys.RespiratoryRateBpm,
    MetricKeys.WristTempDeltaC, MetricKeys.Spo2Pct, MetricKeys.Steps, MetricKeys.ExerciseMin, MetricKeys.WeightKg, MetricKeys.CheckinMood,
  ];

  /// <summary>Overnight readings: today's row is last night. The rest read yesterday's full day.</summary>
  private static readonly HashSet<string> Overnight =
    [MetricKeys.SleepStage, MetricKeys.HrvRmssdMs, MetricKeys.HrvSdnnMs, MetricKeys.RestingHrBpm, MetricKeys.RespiratoryRateBpm, MetricKeys.WristTempDeltaC, MetricKeys.Spo2Pct];

  private readonly IBodyContextLoader _body;
  private readonly ITargetService _targets;
  private readonly IDailySummaryRepository _summaries;
  private readonly IBaselineRepository _baselines;
  private readonly IInsightRepository _insights;
  private readonly IJournalRepository _journal;
  private readonly IMedicationRepository _medications;
  private readonly ILabReportRepository _labs;
  private readonly ILabReferenceRepository _references;
  private readonly IMetricCatalogue _catalogue;
  private readonly IAiMemoryService _memory;
  private readonly IPlaybookService _playbook;
  private readonly IEncryptionService _encryption;

  public StateBriefBuilder(
    IBodyContextLoader body,
    ITargetService targets,
    IDailySummaryRepository summaries,
    IBaselineRepository baselines,
    IInsightRepository insights,
    IJournalRepository journal,
    IMedicationRepository medications,
    ILabReportRepository labs,
    ILabReferenceRepository references,
    IMetricCatalogue catalogue,
    IAiMemoryService memory,
    IPlaybookService playbook,
    IEncryptionService encryption)
  {
    _body = body;
    _targets = targets;
    _summaries = summaries;
    _baselines = baselines;
    _insights = insights;
    _journal = journal;
    _medications = medications;
    _labs = labs;
    _references = references;
    _catalogue = catalogue;
    _memory = memory;
    _playbook = playbook;
    _encryption = encryption;
  }

  public async Task<StateBrief> BuildAsync(Person person, DateOnly today, CancellationToken ct = default)
  {
    var sections = new List<BriefSection>();
    sections.Add(new BriefSection("Today", [
      $"Today is {today.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)} for {person.DisplayName}.",
      person.Units == UnitSystem.Imperial ? "They prefer imperial units." : "They prefer metric units.",
    ]));

    var body = await _body.LoadAsync(person, today, ct);
    var goalLines = body.Goals.Select(GoalLine).ToList();
    sections.Add(new BriefSection("Goals", goalLines.Count > 0 ? goalLines : ["No goal is set."]));

    var tally = await _targets.TallyOwnAsync(person.Id, today, ct);
    var targetLines = tally.Select(TargetLine).ToList();
    sections.Add(new BriefSection("Targets", targetLines));

    var (readingLines, sleepText, standouts) = await ReadingsAsync(person.Id, today, ct);
    sections.Add(new BriefSection("Readings against their usual range", readingLines));

    var insights = await _insights.ListOwnAsync(person.Id, today.AddDays(-1), today, ct);
    var ranked = insights
      .Where(i => i.Kind != InsightKinds.Learning)
      .OrderByDescending(i => SeverityRank(i.Severity))
      .ThenByDescending(i => i.Day)
      .ThenByDescending(InterpretMapping.BandDistanceOf)
      .ThenBy(i => i.Kind, StringComparer.Ordinal)
      .Take(4)
      .ToList();
    sections.Add(new BriefSection("Insights from the rules", ranked.Select(i => $"{i.Severity}: {i.Generated ?? i.Templated}").ToList()));

    var playbook = await _playbook.ApplicableAsync(person, today, ct);
    sections.Add(new BriefSection(
      "Playbook entries that apply today (the only suggestions you may make)",
      playbook.Select(p => $"{p.Title}. {p.Text} Evidence: {p.EvidenceGrade}. Source: {p.Source}. Why now: {p.Why}" +
        (p.Cautions.Count > 0 ? " Caution: " + string.Join(" ", p.Cautions) : string.Empty)).ToList()));

    var medicines = (await _medications.ListOwnAsync(person.Id, ct)).Where(m => m.ActiveOn(today)).ToList();
    sections.Add(new BriefSection("Medicines and supplements they take", medicines.Select(MedicineLine).ToList()));

    sections.Add(new BriefSection("Latest blood panel", await LabLinesAsync(person, ct)));

    var memory = await _memory.RecallAsync(person.Id, null, ct);
    sections.Add(new BriefSection("Things they told you before", memory.Take(30).Select(m => m.Value).ToList()));

    var journal = await _journal.ListOwnAsync(person.Id, today.AddDays(-6), today, ct);
    sections.Add(new BriefSection("Day log, last 7 days, newest first", journal
      .OrderByDescending(j => j.At)
      .Select(JournalLine)
      .ToList()));

    var targetsText = TargetsSummary(tally);
    var facts = new BriefFacts(
      sleepText,
      standouts,
      targetsText,
      ranked.FirstOrDefault() is { } top ? top.Generated ?? top.Templated : null,
      body.Goals.Select(g => BodyMapping.Title(g.Goal, _encryption)).ToList());
    return StateBriefComposer.Compose(sections, playbook, facts);
  }

  private string GoalLine(GoalStanding standing)
  {
    var goal = standing.Goal;
    var line = new StringBuilder();
    line.Append(goal.Slot == GoalSlots.Main ? "Main goal: " : "Secondary goal: ").Append(BodyMapping.Title(goal, _encryption)).Append('.');
    if (standing.MetricName != null && standing.Current is { } current)
    {
      line.Append(' ').Append(standing.MetricName).Append(' ').Append(Format(current)).Append(Unit(standing.Unit)).Append(" now");
      if (goal.TargetValue is { } target) line.Append(", aiming for ").Append(Format(target)).Append(Unit(standing.Unit));
      line.Append('.');
    }
    if (standing.PacePerWeek is { } pace) line.Append(" Moving ").Append(Format(pace)).Append(Unit(standing.Unit)).Append(" a week.");
    line.Append(" Status: ").Append(standing.Status.Replace('_', ' ')).Append('.');
    return line.ToString();
  }

  private static string TargetLine(TargetTallyDto t)
  {
    var unit = Unit(t.Target.Unit);
    var verdict = t.Met switch { true => "met", false => "not met", null => "not known yet" };
    var current = t.Current is { } c ? Format(c) + unit : "nothing logged";
    var comparator = t.Target.Comparator == "at_most" ? "at most" : "at least";
    return t.Target.Period == "week"
      ? $"{t.Target.Label} ({comparator} {Format(t.Target.Value)}{unit} a week): {current} over the last 7 days, {verdict}."
      : $"{t.Target.Label} ({comparator} {Format(t.Target.Value)}{unit} a day): {current} today, {verdict}; kept on {t.MetDays} of {t.KnownDays} {(t.KnownDays == 1 ? "day" : "days")} with an answer this week.";
  }

  /// <summary>"Today 2 of your 3 daily targets are met so far.", with "is" for one met and a sentence of its own for a single target.</summary>
  public static string? TargetsSummary(IReadOnlyList<TargetTallyDto> tally)
  {
    var daily = tally.Where(t => t.Target.Period == "day").ToList();
    if (daily.Count == 0) return null;
    var met = daily.Count(t => t.Met == true);
    if (daily.Count == 1) return met == 1 ? "Today your one daily target is met so far." : "Today your one daily target is not met yet.";
    return $"Today {met} of your {daily.Count} daily targets {(met == 1 ? "is" : "are")} met so far.";
  }

  private async Task<(List<string> Lines, string? SleepText, List<string> Standouts)> ReadingsAsync(Guid personId, DateOnly today, CancellationToken ct)
  {
    var rows = await _summaries.GetOwnRangeAsync(personId, ReadingKeys, today.AddDays(-1), today, ct);
    var baselines = (await _baselines.GetLatestOwnPerMetricAsync(personId, ct)).ToDictionary(b => b.MetricKey, StringComparer.Ordinal);
    var lines = new List<string>();
    var standouts = new List<string>();
    string? sleepText = null;
    foreach (var key in ReadingKeys)
    {
      var mine = rows.Where(r => r.MetricKey == key).ToList();
      var row = Overnight.Contains(key)
        ? mine.FirstOrDefault(r => r.Day == today) ?? mine.FirstOrDefault(r => r.Day == today.AddDays(-1))
        : mine.FirstOrDefault(r => r.Day == today.AddDays(-1)) ?? mine.FirstOrDefault(r => r.Day == today);
      if (row == null) continue;
      var metric = _catalogue.Find(key);
      var name = key == MetricKeys.SleepStage ? "Sleep" : metric?.DisplayName ?? key;
      var unit = key == MetricKeys.SleepStage ? null : metric?.Unit;
      var when = Overnight.Contains(key) ? (row.Day == today ? "last night" : "the night before") : (row.Day == today ? "today so far" : "yesterday");
      var value = ValueText(key, row.Value, unit);

      baselines.TryGetValue(key, out var b);
      var band = b is { IsEstablished: true } ? BandPositions.Of(row.Value, b.P25_28, b.P75_28) : null;
      var line = $"{name}: {value} {when}";
      if (band != null)
      {
        line += $"; usual range {ValueText(key, b!.P25_28!.Value, unit)} to {ValueText(key, b.P75_28!.Value, unit)}, so {BandWords(band)}.";
        var standout = b.P10_28 is { } p10 && row.Value < p10 || b.P90_28 is { } p90 && row.Value > p90;
        if (standout && key != MetricKeys.SleepStage) standouts.Add($"{name} {MetricNames.Was(key)} {value} {when}, {BandWords(band)}.");
      }
      else
      {
        line += "; its usual range is not known yet.";
      }
      lines.Add(line);
      if (key == MetricKeys.SleepStage && row.Day == today)
      {
        sleepText = $"You slept {value} last night" + (band == null ? "." : $", {BandWords(band)}.");
      }
    }
    return (lines, sleepText, standouts);
  }

  private async Task<List<string>> LabLinesAsync(Person person, CancellationToken ct)
  {
    var reports = await _labs.ListOwnAsync(person.Id, 1, ct);
    if (reports.Count == 0) return [];
    var report = reports[0];
    var lines = new List<string> { $"Collected on {report.CollectedOn.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}." };
    foreach (var result in report.Results.OrderBy(r => r.MetricKey, StringComparer.Ordinal))
    {
      var metric = _catalogue.Find(result.MetricKey);
      var name = metric?.DisplayName ?? result.MetricKey;
      var assessment = metric == null ? null : LabFlagging.Assess(_references.Find(metric, person.SexAtBirth), result.Value);
      var flagged = result.Flag is LabFlags.High or LabFlags.Low;
      if (assessment is { OutOfRange: true })
      {
        lines.Add($"{name} {Format(result.Value)} {result.Unit}, {(assessment.Position == LabPositions.Above ? "above" : "below")} the usual adult range.");
      }
      else if (flagged)
      {
        lines.Add($"{name} {Format(result.Value)} {result.Unit}, flagged {result.Flag} by the lab.");
      }
    }
    if (lines.Count == 1) lines.Add("Nothing on it was outside the usual range.");
    return lines;
  }

  private string MedicineLine(Medication medication)
  {
    var detail = BodyMapping.ReadDetail(medication, _encryption);
    var line = new StringBuilder();
    line.Append(medication.Kind == MedicationKinds.Medication ? "Medicine: " : "Supplement: ").Append(detail.Product);
    if (!string.IsNullOrWhiteSpace(detail.Brand)) line.Append(" (").Append(detail.Brand).Append(')');
    if (!string.IsNullOrWhiteSpace(detail.Dose)) line.Append(", ").Append(detail.Dose);
    line.Append(", ").Append(medication.Schedule.Replace('_', ' '));
    if (detail.Ingredients.Count > 0)
    {
      line.Append("; contains ").Append(string.Join(", ", detail.Ingredients.Select(i =>
        i.Name + (i.Amount is { } amount ? " " + Format(amount) + (string.IsNullOrWhiteSpace(i.Unit) ? string.Empty : " " + i.Unit) : string.Empty))));
    }
    line.Append('.');
    return line.ToString();
  }

  private string JournalLine(JournalEntry entry)
  {
    var text = entry.TextCipher == null ? null : _encryption.TryDecrypt(CollectMapping.FromCipherBytes(entry.TextCipher));
    if (text is { Length: > 160 }) text = text[..160];
    var line = new StringBuilder();
    line.Append(entry.Day.ToString("ddd d MMM", CultureInfo.InvariantCulture)).Append(", ").Append(entry.Kind);
    if (text != null) line.Append(": \"").Append(text).Append('"');
    if (entry.Tags.Length > 0) line.Append(" [").Append(string.Join(", ", entry.Tags.Select(JournalTags.DisplayName))).Append(']');
    if (entry.Portion != null) line.Append(", ").Append(entry.Portion).Append(" portion");
    return line.ToString();
  }

  private static string ValueText(string key, double value, string? unit) => key switch
  {
    MetricKeys.SleepStage => TodayComposer.FormatMinutes(value),
    MetricKeys.Steps => Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + " steps",
    MetricKeys.CheckinMood => Format(value) + " out of 6",
    _ => Format(value) + Unit(unit),
  };

  private static string BandWords(string band) => band switch
  {
    BandPositions.Below => "below your usual range",
    BandPositions.Above => "above your usual range",
    _ => "inside your usual range",
  };

  private static int SeverityRank(string severity) => severity switch
  {
    InsightSeverities.Clinician => 3,
    InsightSeverities.Watch => 2,
    _ => 1,
  };

  private static string Unit(string? unit) => string.IsNullOrWhiteSpace(unit) ? string.Empty : " " + unit;

  public static string Format(double value) => Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);
}
