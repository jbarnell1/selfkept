using System.Globalization;
using System.Text;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Formatting;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Export;

/// <summary>
/// Exports of a person's own data (docs/proposal.md, "Export"). Never another member's data, even
/// when it is shared: an export is something the person takes away and gives to someone else.
///
/// The privacy bundle is Markdown written for a model's context window. By default it carries no
/// name, an age band instead of a birth date, days counted back from the export ("day -12")
/// instead of dates, and no check-in notes; each is a switch the person turns on. A month or less
/// is shown day by day; a longer window is shown week by week, so 180 days still fits.
/// </summary>
public sealed class ExportService : IExportService
{
  public const int DefaultDays = 90;
  private const int DailyRowsUpTo = 31;

  /// <summary>The metrics a category brings, in the order they are written.</summary>
  private static readonly IReadOnlyDictionary<ShareCategory, (string Key, string Label)[]> MetricsByCategory =
    new Dictionary<ShareCategory, (string, string)[]>
    {
      [ShareCategory.Sleep] = [(MetricKeys.SleepStage, "Minutes asleep per night"), (MetricKeys.OuraSleepScore, "Oura sleep score")],
      [ShareCategory.Activity] = [(MetricKeys.Steps, "Steps"), (MetricKeys.ExerciseMin, "Exercise minutes"), (MetricKeys.ActiveEnergyKcal, "Active energy, kcal")],
      [ShareCategory.Vitals] =
      [
        (MetricKeys.RestingHrBpm, "Resting heart rate, bpm"), (MetricKeys.HrvSdnnMs, "HRV (SDNN), ms"), (MetricKeys.HrvRmssdMs, "HRV (RMSSD), ms"),
        (MetricKeys.Spo2Pct, "Blood oxygen, %"), (MetricKeys.RespiratoryRateBpm, "Breaths per minute"),
        (MetricKeys.WristTempDeltaC, "Overnight temperature change, C"), (MetricKeys.OuraReadiness, "Oura readiness"),
      ],
      [ShareCategory.Body] = [(MetricKeys.WeightKg, "Weight, kg")],
    };

  private static readonly ShareCategory[] Order =
    [ShareCategory.Sleep, ShareCategory.Activity, ShareCategory.Vitals, ShareCategory.Body, ShareCategory.Checkins, ShareCategory.Labs, ShareCategory.Insights];

  private readonly IPersonRepository _persons;
  private readonly IDailySummaryRepository _daily;
  private readonly ICheckinService _checkins;
  private readonly ILabService _labs;
  private readonly IInsightService _insights;
  private readonly ISourceService _sources;
  private readonly IAiMemoryService _memory;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;

  public ExportService(
    IPersonRepository persons,
    IDailySummaryRepository daily,
    ICheckinService checkins,
    ILabService labs,
    IInsightService insights,
    ISourceService sources,
    IAiMemoryService memory,
    IAuditService audit,
    TimeProvider clock)
  {
    _persons = persons;
    _daily = daily;
    _checkins = checkins;
    _labs = labs;
    _insights = insights;
    _sources = sources;
    _memory = memory;
    _audit = audit;
    _clock = clock;
  }

  public async Task<PrivacyBundleDto> BuildBundleAsync(Guid userId, Guid personId, Guid? householdId, PrivacyBundleRequest request, RequestInfo info, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(personId, ct) ?? throw new InvalidOperationException("The person does not exist.");
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var to = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, zone);
    var days = Math.Clamp(request.Days ?? DefaultDays, 7, 180);
    var from = to.AddDays(-(days - 1));
    var categories = (request.Categories is { Count: > 0 } chosen ? chosen : Order).Distinct().ToHashSet();

    string Label(DateOnly day) => request.KeepDates ? day.ToIso() : $"day {day.DayNumber - to.DayNumber}";

    var md = new StringBuilder();
    md.AppendLine("# Health summary").AppendLine();
    md.Append("Prepared from a self-hosted health record to ask an AI model a question. ");
    md.Append(request.IncludeName ? "The person's name is included. " : "The name was left out. ");
    md.Append(request.KeepDates ? "Dates are real dates. " : "Dates are counted back from the day of export, so day 0 is today and day -7 a week ago. ");
    md.AppendLine("Values come from the person's phone, watch, ring and blood tests; they are not a diagnosis.").AppendLine();

    if (request.IncludeName) md.Append("Name: ").Append(person.DisplayName).AppendLine("  ");
    md.Append("Sex at birth: ").Append(person.SexAtBirth == SexAtBirth.Unspecified ? "not given" : person.SexAtBirth.ToString().ToLowerInvariant()).AppendLine("  ");
    md.Append("Age: ").Append(AgeBand(person.BirthDate, to)).AppendLine("  ");
    md.Append("Period: ").Append(Label(from)).Append(" to ").Append(Label(to)).Append($" ({days} days)").AppendLine().AppendLine();

    var included = new List<string>();
    foreach (var category in Order.Where(categories.Contains))
    {
      var wrote = category switch
      {
        ShareCategory.Checkins => await WriteCheckinsAsync(md, personId, from, to, zone, request.IncludeNotes, Label, ct),
        ShareCategory.Labs => await WriteLabsAsync(md, personId, from, to, Label, ct),
        ShareCategory.Insights => await WriteInsightsAsync(md, personId, from, to, Label, ct),
        _ when MetricsByCategory.ContainsKey(category) => await WriteMetricsAsync(md, personId, category, from, to, days, Label, ct),
        _ => false,
      };
      if (wrote) included.Add(category.ToString().ToLowerInvariant());
    }
    if (included.Count == 0)
      md.AppendLine("No data in these categories for this period.");

    var text = md.ToString().TrimEnd() + "\n";
    if (request.Export)
    {
      await _audit.RecordAsync(new AuditEvent(AuditActions.ExportBundle, userId, householdId, "person", personId.ToString(),
        new { categories = included, days, request.KeepDates, request.IncludeNotes, request.IncludeName, characters = text.Length }, info.IpAddress), ct);
    }
    return new PrivacyBundleDto(text, text.Length, (int)Math.Ceiling(text.Length / 4.0), included, from, to);
  }

  private async Task<bool> WriteMetricsAsync(StringBuilder md, Guid personId, ShareCategory category, DateOnly from, DateOnly to, int days, Func<DateOnly, string> label, CancellationToken ct)
  {
    var metrics = MetricsByCategory[category];
    var rows = await _daily.GetOwnRangeAsync(personId, metrics.Select(m => m.Key).ToList(), from, to, ct);
    if (rows.Count == 0) return false;

    md.Append("## ").AppendLine(Title(category)).AppendLine();
    foreach (var (key, title) in metrics)
    {
      var series = rows.Where(r => r.MetricKey == key).OrderBy(r => r.Day).ToList();
      if (series.Count == 0) continue;
      var values = series.Select(r => r.Value).OrderBy(v => v).ToList();
      md.Append("### ").AppendLine(title);
      md.Append($"Median {Fmt(Median(values))}, lowest {Fmt(values[0])}, highest {Fmt(values[^1])}, {series.Count} days with data.").AppendLine().AppendLine();
      if (days <= DailyRowsUpTo)
      {
        md.AppendLine("| Day | Value |").AppendLine("| --- | --- |");
        foreach (var r in series) md.Append("| ").Append(label(r.Day)).Append(" | ").Append(Fmt(r.Value)).AppendLine(" |");
      }
      else
      {
        md.AppendLine("| Week starting | Median | Lowest | Highest | Days |").AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var week in series.GroupBy(r => (to.DayNumber - r.Day.DayNumber) / 7).OrderByDescending(g => g.Key))
        {
          var weekValues = week.Select(r => r.Value).OrderBy(v => v).ToList();
          var start = week.Min(r => r.Day);
          md.Append($"| {label(start)} | {Fmt(Median(weekValues))} | {Fmt(weekValues[0])} | {Fmt(weekValues[^1])} | {weekValues.Count} |").AppendLine();
        }
      }
      md.AppendLine();
    }
    return true;
  }

  private async Task<bool> WriteCheckinsAsync(StringBuilder md, Guid personId, DateOnly from, DateOnly to, TimeZoneInfo zone, bool includeNotes, Func<DateOnly, string> label, CancellationToken ct)
  {
    var (fromUtc, _) = DayBoundaries.UtcWindow(from, zone);
    var (_, toUtc) = DayBoundaries.UtcWindow(to, zone);
    var list = await _checkins.ListAsync(personId, personId, fromUtc, toUtc, ct);
    if (list is not { Count: > 0 }) return false;

    md.AppendLine("## Mood check-ins").AppendLine();
    md.AppendLine("Mood is 1 (rough) to 6 (great), as the person rated it.").AppendLine();
    md.Append(includeNotes ? "| Day | Mood | Note |\n| --- | --- | --- |\n" : "| Day | Mood |\n| --- | --- |\n");
    foreach (var c in list.OrderBy(c => c.At))
    {
      var day = DayBoundaries.LocalDay(c.At, zone);
      md.Append("| ").Append(label(day)).Append(" | ").Append(c.Mood);
      if (includeNotes) md.Append(" | ").Append(Cell(c.Note));
      md.AppendLine(" |");
    }
    md.AppendLine();
    return true;
  }

  private async Task<bool> WriteLabsAsync(StringBuilder md, Guid personId, DateOnly from, DateOnly to, Func<DateOnly, string> label, CancellationToken ct)
  {
    var markers = await _labs.GetMarkersAsync(personId, personId, ct);
    var withValues = markers?.Where(m => m.Applicable && m.LastThree.Any(p => p.Date >= from && p.Date <= to)).ToList();
    if (withValues is not { Count: > 0 }) return false;

    md.AppendLine("## Blood tests").AppendLine();
    md.AppendLine("Usual ranges are approximate adult population ranges for the person's sex; a lab's own range can differ.").AppendLine();
    md.AppendLine("| Marker | Results (oldest first) | Usual range | Latest is |").AppendLine("| --- | --- | --- | --- |");
    foreach (var m in withValues)
    {
      var results = string.Join(", ", m.LastThree.Where(p => p.Date >= from && p.Date <= to).OrderBy(p => p.Date)
        .Select(p => $"{Fmt(p.Value)} {m.Unit} ({label(p.Date)})"));
      var range = m.PopulationRange switch
      {
        { Low: { } lo, High: { } hi } => $"{Fmt(lo)} to {Fmt(hi)}",
        { High: { } hi } => $"up to {Fmt(hi)}",
        { Low: { } lo } => $"from {Fmt(lo)}",
        _ => "not set",
      };
      md.Append($"| {m.DisplayName} | {results} | {range} | {m.Position ?? "unknown"} |").AppendLine();
    }
    md.AppendLine();
    return true;
  }

  private async Task<bool> WriteInsightsAsync(StringBuilder md, Guid personId, DateOnly from, DateOnly to, Func<DateOnly, string> label, CancellationToken ct)
  {
    var list = await _insights.ListAsync(personId, personId, from, to, includeDismissed: true, ct);
    var real = list?.Where(i => i.Kind != InsightKinds.Learning).OrderBy(i => i.Day).ToList();
    if (real is not { Count: > 0 }) return false;

    md.AppendLine("## Patterns the app noticed").AppendLine();
    md.AppendLine("Raised by fixed rules over the person's own data. Severity is notice, watch, or clinician (worth raising with a clinician).").AppendLine();
    foreach (var i in real)
      md.Append("- ").Append(label(i.Day)).Append(", ").Append(i.Severity).Append(": ").AppendLine(i.Text);
    md.AppendLine();
    return true;
  }

  public async Task<object> BuildFullExportAsync(Guid userId, Guid personId, Guid? householdId, RequestInfo info, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(personId, ct) ?? throw new InvalidOperationException("The person does not exist.");
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var to = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, zone);
    var from = new DateOnly(2000, 1, 1);
    var allKeys = MetricsByCategory.Values.SelectMany(v => v).Select(m => m.Key).Append(MetricKeys.CheckinMood).Append(MetricKeys.Flights).Append(MetricKeys.DistanceM).Distinct().ToList();
    var daily = await _daily.GetOwnRangeAsync(personId, allKeys, from, to, ct);
    // The list services cap one call (a year of insights, 500 check-ins), so a full export walks
    // back a year at a time; nobody has check-ins from before the app existed, and ten years
    // covers any phone history worth importing.
    var checkins = new List<CheckinDto>();
    var insights = new List<InsightDto>();
    for (var yearEnd = to; yearEnd > to.AddYears(-10); yearEnd = yearEnd.AddDays(-365))
    {
      var yearStart = yearEnd.AddDays(-364);
      var (startUtc, _) = DayBoundaries.UtcWindow(yearStart, zone);
      var (_, endUtc) = DayBoundaries.UtcWindow(yearEnd, zone);
      checkins.AddRange(await _checkins.ListAsync(personId, personId, startUtc, endUtc, ct) ?? []);
      insights.AddRange(await _insights.ListAsync(personId, personId, yearStart, yearEnd, includeDismissed: true, ct) ?? []);
    }
    var reports = await _labs.ListReportsAsync(personId, personId, ct) ?? [];
    var sources = await _sources.ListAsync(personId, ct);
    var memory = await _memory.RecallAsync(personId, null, ct);

    await _audit.RecordAsync(new AuditEvent(AuditActions.ExportFull, userId, householdId, "person", personId.ToString(),
      new { dailyRows = daily.Count, checkins = checkins.Count, reports = reports.Count }, info.IpAddress), ct);

    return new
    {
      exportedAt = _clock.GetUtcNow().UtcDateTime,
      profile = new { person.DisplayName, sexAtBirth = person.SexAtBirth.ToString().ToLowerInvariant(), person.BirthDate, units = person.Units.ToString().ToLowerInvariant(), person.TimeZone },
      sources = sources.Select(s => new { s.Kind, s.DeviceName, s.Platform, s.LastSyncAt, s.SampleCount }),
      daily = daily.OrderBy(d => d.MetricKey).ThenBy(d => d.Day).Select(d => new { metric = d.MetricKey, d.Day, d.Value, d.Min, d.Max, d.Count }),
      checkins,
      labReports = reports,
      insights,
      memory,
    };
  }

  private static string Title(ShareCategory category) => category switch
  {
    ShareCategory.Sleep => "Sleep",
    ShareCategory.Activity => "Activity",
    ShareCategory.Vitals => "Heart, breathing and recovery",
    ShareCategory.Body => "Body",
    _ => category.ToString(),
  };

  /// <summary>An age band ("30 to 39") instead of a birth date.</summary>
  public static string AgeBand(DateOnly? birthDate, DateOnly today)
  {
    if (birthDate is not { } born) return "not given";
    var age = today.Year - born.Year - (today < born.AddYears(today.Year - born.Year) ? 1 : 0);
    var low = age / 10 * 10;
    return $"{low} to {low + 9}";
  }

  private static double Median(IReadOnlyList<double> sorted) =>
    sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

  private static string Fmt(double value) =>
    (Math.Abs(value) >= 100 ? Math.Round(value, 0) : Math.Abs(value) >= 10 ? Math.Round(value, 1) : Math.Round(value, 2)).ToString(CultureInfo.InvariantCulture);

  private static string Cell(string? text) => string.IsNullOrWhiteSpace(text) ? "" : text.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
}
