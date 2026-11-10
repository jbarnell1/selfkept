using System.Text.Json;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Security;
using SelfKept.Infrastructure.Services.Collect;
using SelfKept.Infrastructure.Services.Interpret;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>The facts one morning brief is written from. Pure input to <see cref="MorningBriefComposer"/>.</summary>
public sealed record MorningFacts(
  string DisplayName,
  double? SleepMinutes,
  string? SleepBand,
  IReadOnlyList<MorningReading> Standouts,
  int? TargetsKept,
  int? TargetsKnown,
  string? InsightText,
  IReadOnlyList<string> EveningTags,
  string? EveningStatus);

public sealed record MorningReading(string Name, string Value, string Band);

/// <summary>
/// Code chooses what matters and the templates word it: last night's sleep against usual,
/// readings outside their wider band, yesterday's day targets, the leading insight, and what the
/// evening chat recorded. No advice and no cause, only what the record holds. Pure.
/// </summary>
public static class MorningBriefComposer
{
  public static IReadOnlyList<MorningBriefItemDto> Items(MorningFacts facts)
  {
    var items = new List<MorningBriefItemDto>();
    if (facts.SleepMinutes is { } minutes)
    {
      var band = facts.SleepBand switch
      {
        BandPositions.Below => ", below your usual range",
        BandPositions.Above => ", above your usual range",
        BandPositions.Inside => ", about your usual",
        _ => string.Empty,
      };
      items.Add(new MorningBriefItemDto("sleep", $"You slept {TodayComposer.FormatMinutes(minutes)} last night{band}."));
    }
    foreach (var reading in facts.Standouts.Take(2))
    {
      items.Add(new MorningBriefItemDto("reading", $"{reading.Name} was {reading.Value} overnight, {(reading.Band == BandPositions.Below ? "below" : "above")} your usual range."));
    }
    if (facts.TargetsKnown is > 0 && facts.TargetsKept is { } kept)
    {
      items.Add(new MorningBriefItemDto("targets", $"Yesterday you kept {kept} of the {facts.TargetsKnown} day targets that have an answer."));
    }
    if (!string.IsNullOrWhiteSpace(facts.InsightText))
    {
      items.Add(new MorningBriefItemDto("insight", facts.InsightText!.Trim()));
    }
    if (facts.EveningTags.Count > 0)
    {
      var labels = facts.EveningTags.Take(5).Select(t => JournalTags.DisplayName(t).ToLowerInvariant()).ToList();
      var lead = facts.EveningStatus == CheckinStatuses.Done ? "Last night's check-in noted" : "Yesterday's log noted";
      items.Add(new MorningBriefItemDto("evening", $"{lead}: {string.Join(", ", labels)}."));
    }
    return items;
  }

  public static string Text(MorningFacts facts, IReadOnlyList<MorningBriefItemDto> items)
  {
    var greeting = $"Good morning, {facts.DisplayName}.";
    return items.Count == 0
      ? greeting + " Nothing new in your record since yesterday."
      : greeting + " " + string.Join(" ", items.Select(i => i.Text));
  }
}

/// <summary>
/// GET /api/assistant/brief. Cached per person and local day in assistant_brief and written again
/// only when its facts change; the model may reword it, and the guard checks every number.
/// </summary>
public sealed class MorningBriefService : IMorningBriefService
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private static readonly string[] StandoutKeys = [MetricKeys.RestingHrBpm, MetricKeys.HrvRmssdMs, MetricKeys.HrvSdnnMs, MetricKeys.RespiratoryRateBpm, MetricKeys.WristTempDeltaC];

  private readonly IAssistantRepository _repository;
  private readonly IPersonRepository _persons;
  private readonly IDailySummaryRepository _summaries;
  private readonly IBaselineRepository _baselines;
  private readonly ITargetService _targets;
  private readonly IInsightRepository _insights;
  private readonly IJournalRepository _journal;
  private readonly IMetricCatalogue _catalogue;
  private readonly ModelRewording _rewording;
  private readonly IEncryptionService _encryption;
  private readonly TimeProvider _clock;

  public MorningBriefService(
    IAssistantRepository repository,
    IPersonRepository persons,
    IDailySummaryRepository summaries,
    IBaselineRepository baselines,
    ITargetService targets,
    IInsightRepository insights,
    IJournalRepository journal,
    IMetricCatalogue catalogue,
    ModelRewording rewording,
    IEncryptionService encryption,
    TimeProvider clock)
  {
    _repository = repository;
    _persons = persons;
    _summaries = summaries;
    _baselines = baselines;
    _targets = targets;
    _insights = insights;
    _journal = journal;
    _catalogue = catalogue;
    _rewording = rewording;
    _encryption = encryption;
    _clock = clock;
  }

  public async Task<MorningBriefDto?> GetAsync(Guid personId, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(personId, ct);
    if (person == null) return null;
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var now = _clock.GetUtcNow().UtcDateTime;
    var today = DayBoundaries.LocalDay(now, zone);
    var yesterday = today.AddDays(-1);

    var facts = await FactsAsync(person, today, yesterday, ct);
    var items = MorningBriefComposer.Items(facts);
    var templated = MorningBriefComposer.Text(facts, items);
    var hash = TokenCrypto.Sha256Hex(JsonSerializer.Serialize(facts, Json));

    var cached = await _repository.GetOwnBriefAsync(personId, today, ct);
    if (cached != null && cached.FactsHash == hash && _encryption.TryDecrypt(cached.TextCipher) is { } text)
    {
      return new MorningBriefDto(today, text, cached.Generated, items, cached.ComputedAt);
    }

    var reworded = items.Count == 0
      ? null
      : await _rewording.RewordAsync(personId, AssistantAiPurposes.Brief, AssistantTemplates.BriefPrompt, templated,
          GuardNumbers.Context(templated), asLine: false, maxTokens: 260, ct);
    var final = reworded ?? templated;
    await _repository.SaveOwnBriefAsync(new AssistantBrief
    {
      PersonId = personId,
      Day = today,
      FactsHash = hash,
      TextCipher = _encryption.Encrypt(final),
      Generated = reworded != null,
      ComputedAt = now,
    }, ct);
    return new MorningBriefDto(today, final, reworded != null, items, now);
  }

  private async Task<MorningFacts> FactsAsync(Person person, DateOnly today, DateOnly yesterday, CancellationToken ct)
  {
    var rows = await _summaries.GetOwnRangeAsync(person.Id, [MetricKeys.SleepStage, .. StandoutKeys], today, today, ct);
    var baselines = (await _baselines.GetLatestOwnPerMetricAsync(person.Id, ct)).ToDictionary(b => b.MetricKey, StringComparer.Ordinal);

    double? sleep = rows.FirstOrDefault(r => r.MetricKey == MetricKeys.SleepStage)?.Value;
    string? sleepBand = null;
    if (sleep is { } s && baselines.TryGetValue(MetricKeys.SleepStage, out var sb) && sb.IsEstablished) sleepBand = BandPositions.Of(s, sb.P25_28, sb.P75_28);

    var standouts = new List<MorningReading>();
    foreach (var key in StandoutKeys)
    {
      var row = rows.FirstOrDefault(r => r.MetricKey == key);
      if (row == null || !baselines.TryGetValue(key, out var b) || !b.IsEstablished) continue;
      var band = b.P10_28 is { } p10 && row.Value < p10 ? BandPositions.Below
        : b.P90_28 is { } p90 && row.Value > p90 ? BandPositions.Above
        : null;
      if (band == null) continue;
      var metric = _catalogue.Find(key);
      standouts.Add(new MorningReading(metric?.DisplayName ?? key, StateBriefBuilder.Format(row.Value) + (metric?.Unit is { Length: > 0 } unit ? " " + unit : string.Empty), band));
    }

    var tally = await _targets.TallyOwnAsync(person.Id, yesterday, ct);
    var daily = tally.Where(t => t.Target.Period == "day").ToList();
    var known = daily.Count(t => t.Met != null);
    var kept = daily.Count(t => t.Met == true);

    var insights = await _insights.ListOwnAsync(person.Id, yesterday, today, ct);
    var top = insights
      .Where(i => i.Kind != InsightKinds.Learning)
      .OrderByDescending(i => i.Severity == InsightSeverities.Clinician ? 3 : i.Severity == InsightSeverities.Watch ? 2 : 1)
      .ThenByDescending(i => i.Day)
      .ThenByDescending(InterpretMapping.BandDistanceOf)
      .ThenBy(i => i.Kind, StringComparer.Ordinal)
      .FirstOrDefault();

    var evening = await _journal.ListOwnAsync(person.Id, yesterday, yesterday, ct);
    var eveningTags = evening
      .Where(e => e.Source is JournalSources.Checkin or JournalSources.Assistant)
      .SelectMany(e => e.Tags)
      .Distinct(StringComparer.Ordinal)
      .ToList();
    var checkin = await _repository.GetOwnCheckinAsync(person.Id, yesterday, ct);
    var status = checkin == null ? null : CheckinMachine.Status(CheckinState.FromJson(checkin.State));

    return new MorningFacts(person.DisplayName, sleep, sleepBand, standouts, known > 0 ? kept : null, known > 0 ? known : null,
      top == null ? null : top.Generated ?? top.Templated, eveningTags, status);
  }
}
