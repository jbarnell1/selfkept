using System.Text.Json;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Interpret;

/// <summary>
/// GET /api/today. Composes the sentence from last night's sleep, HRV and resting HR against the
/// band, the active cards and the latest check-in; the sentence is cached per person for an hour
/// and dropped by a check-in, a batch or a recompute. Cards and readiness are read fresh every
/// time so a dismissal shows at once. Access is the insights category, checked in the repositories.
/// </summary>
public sealed class TodayService : ITodayService
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private static readonly string[] ReadinessMetrics =
    [MetricKeys.SleepStage, MetricKeys.HrvRmssdMs, MetricKeys.HrvSdnnMs, MetricKeys.RestingHrBpm];

  private readonly IPersonRepository _persons;
  private readonly IBaselineRepository _baselines;
  private readonly IDailySummaryRepository _summaries;
  private readonly IInsightRepository _insights;
  private readonly ICheckinRepository _checkins;
  private readonly ITodayCacheRepository _cache;
  private readonly IInsightPhraser _phraser;
  private readonly IEncryptionService _encryption;
  private readonly TimeProvider _clock;

  public TodayService(
    IPersonRepository persons,
    IBaselineRepository baselines,
    IDailySummaryRepository summaries,
    IInsightRepository insights,
    ICheckinRepository checkins,
    ITodayCacheRepository cache,
    IInsightPhraser phraser,
    IEncryptionService encryption,
    TimeProvider clock)
  {
    _persons = persons;
    _baselines = baselines;
    _summaries = summaries;
    _insights = insights;
    _checkins = checkins;
    _cache = cache;
    _phraser = phraser;
    _encryption = encryption;
    _clock = clock;
  }

  public async Task<TodayDto?> GetAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var now = _clock.GetUtcNow().UtcDateTime;
    var today = DayBoundaries.LocalDay(now, zone);

    // The insights gate: both reads return null when the viewer has no insights grant.
    var baselines = await _baselines.GetLatestPerMetricForInsightsAsync(viewerPersonId, targetPersonId, ct);
    if (baselines == null) return null;
    var recent = await _summaries.GetRangeForInsightsAsync(viewerPersonId, targetPersonId, ReadinessMetrics, today.AddDays(-1), today, ct);
    if (recent == null) return null;
    var stored = await _insights.ListAsync(viewerPersonId, targetPersonId, today.AddDays(-1), today, includeDismissed: false, ct);
    if (stored == null) return null;

    var byMetric = baselines.ToDictionary(b => b.MetricKey, StringComparer.Ordinal);
    var learning = !byMetric.Values.Any(b => b.IsEstablished);

    // Cards: from the most recent interpreted day (today once the nightly job ran, else yesterday).
    var latestDay = stored.Count == 0 ? (DateOnly?)null : stored.Max(i => i.Day);
    var demoted = await DemotedKindsAsync(targetPersonId, today, stored.Where(i => i.Day == latestDay).Select(i => i.Kind).Distinct(), ct);
    var cards = latestDay == null ? [] : InsightRanking.SelectTodayCards(stored.Where(i => i.Day == latestDay), demoted);
    foreach (var card in cards.Where(c => c.ShownAt == null && viewerPersonId == targetPersonId))
    {
      card.ShownAt = now;
      await _insights.UpdateAsync(card, ct);
    }

    double? sleepMinutes = LastNight(recent, MetricKeys.SleepStage, today);
    var hrvKey = byMetric.ContainsKey(MetricKeys.HrvRmssdMs) || recent.Any(r => r.MetricKey == MetricKeys.HrvRmssdMs) ? MetricKeys.HrvRmssdMs : MetricKeys.HrvSdnnMs;
    var readiness = new ReadinessDto(
      Band(byMetric, hrvKey, LastNight(recent, hrvKey, today)),
      Band(byMetric, MetricKeys.RestingHrBpm, LastNight(recent, MetricKeys.RestingHrBpm, today)),
      Band(byMetric, MetricKeys.SleepStage, sleepMinutes));

    // The latest check-in follows the checkins grant; null means not shared (or none).
    var checkins = await _checkins.ListAsync(viewerPersonId, targetPersonId, now.AddDays(-30), now.AddMinutes(1), 1, ct);
    var lastCheckin = checkins is { Count: > 0 } ? checkins[0] : null;

    // The sentence is the person's own and cached for them, so its facts are read as the owner.
    var ownCheckins = viewerPersonId == targetPersonId ? checkins : await _checkins.ListAsync(targetPersonId, targetPersonId, now.AddDays(-30), now.AddMinutes(1), 1, ct);
    var ownMood = ownCheckins is { Count: > 0 } ? ownCheckins[0].Mood : (int?)null;
    var facts = new TodayFacts(
      sleepMinutes,
      readiness.Sleep,
      readiness.Hrv,
      readiness.RestingHr,
      cards.Count > 0 ? cards[0].Severity : null,
      ownMood,
      learning);

    // A viewer who may not see check-ins gets the sentence without the mood: composed from the
    // templates for them alone, never cached over the owner's and never sent to a model.
    var (sentence, generated) = lastCheckin?.Mood == ownMood
      ? await SentenceAsync(targetPersonId, today, facts, now, ct)
      : (TodayComposer.Compose(facts with { LastCheckinMood = lastCheckin?.Mood }), false);

    return new TodayDto(
      today,
      sentence,
      generated,
      sleepMinutes,
      readiness,
      cards.Select(c => c.ToDto()).ToList(),
      lastCheckin == null ? null : lastCheckin.ToDto(lastCheckin.NoteCipher == null ? null : _encryption.TryDecrypt(CollectMapping.FromCipherBytes(lastCheckin.NoteCipher))),
      learning);
  }

  private async Task<(string Sentence, bool Generated)> SentenceAsync(Guid personId, DateOnly today, TodayFacts facts, DateTime now, CancellationToken ct)
  {
    var factsJson = JsonSerializer.Serialize(facts, Json);
    var cached = await _cache.GetAsync(personId, ct);
    if (cached != null && now - cached.ComputedAt < TodayCache.Lifetime)
    {
      var payload = Payload.From(cached.Payload);
      if (payload?.Day == today && payload.Facts == factsJson)
      {
        return (cached.Sentence, payload.Generated);
      }
    }

    var templated = TodayComposer.Compose(facts);
    var phrased = await _phraser.PhraseAsync(personId, AiPurposes.Today, templated, ct);
    var sentence = phrased ?? templated;
    await _cache.SetAsync(new TodayCache
    {
      PersonId = personId,
      ComputedAt = now,
      Sentence = sentence,
      Payload = JsonSerializer.Serialize(new Payload(today, factsJson, phrased != null, templated), Json),
    }, ct);
    return (sentence, phrased != null);
  }

  private async Task<HashSet<string>> DemotedKindsAsync(Guid personId, DateOnly today, IEnumerable<string> kinds, CancellationToken ct)
  {
    var demoted = new HashSet<string>(StringComparer.Ordinal);
    var since = today.AddDays(-InsightReactions.DemotionWindowDays);
    foreach (var kind in kinds)
    {
      if (await _insights.CountReactionsAsync(personId, kind, InsightReactions.Wrong, since, ct) >= InsightReactions.DemotionCount) demoted.Add(kind);
    }
    return demoted;
  }

  /// <summary>Today's row when it exists (a night is credited to the day it ended), else yesterday's.</summary>
  private static double? LastNight(IReadOnlyList<DailySummary> rows, string metricKey, DateOnly today)
  {
    var mine = rows.Where(r => r.MetricKey == metricKey).ToList();
    return (mine.FirstOrDefault(r => r.Day == today) ?? mine.FirstOrDefault(r => r.Day == today.AddDays(-1)))?.Value;
  }

  private static string? Band(IReadOnlyDictionary<string, Baseline> baselines, string metricKey, double? value)
  {
    if (value is not { } v || !baselines.TryGetValue(metricKey, out var b) || !b.IsEstablished) return null;
    return BandPositions.Of(v, b.P25_28, b.P75_28);
  }

  private sealed record Payload(DateOnly Day, string Facts, bool Generated, string Templated)
  {
    public static Payload? From(string json)
    {
      try
      {
        return JsonSerializer.Deserialize<Payload>(json, Json);
      }
      catch (JsonException)
      {
        return null;
      }
    }
  }
}
