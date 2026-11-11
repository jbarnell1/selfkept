using System.Text.Json;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Interpret;

public static class InterpretMapping
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static InsightDto ToDto(this Insight i) =>
    new(i.Id, i.Day, i.Kind, i.Severity, i.Metrics, CollectMapping.ParseJson(i.Evidence) ?? EmptyObject(), i.Text, i.Generated != null, i.ShownAt, i.DismissedAt, i.Reaction,
      i.Metrics.Select(MetricNames.For).ToList());

  public static TrendPointDto ToTrendPoint(this DailySummary d) => new(d.Day, d.Value, d.Min, d.Max);

  /// <summary>The rule's evidence plus bandDistance, so the archive can be re-ranked later.</summary>
  public static string EvidenceJson(InsightCandidate candidate)
  {
    var merged = new Dictionary<string, object?>(candidate.Evidence, StringComparer.Ordinal) { ["bandDistance"] = candidate.BandDistance };
    return JsonSerializer.Serialize(merged, Json);
  }

  public static double BandDistanceOf(Insight insight)
  {
    try
    {
      using var doc = JsonDocument.Parse(insight.Evidence);
      return doc.RootElement.TryGetProperty("bandDistance", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
    }
    catch (JsonException)
    {
      return 0;
    }
  }

  private static JsonElement EmptyObject()
  {
    using var doc = JsonDocument.Parse("{}");
    return doc.RootElement.Clone();
  }
}

/// <summary>
/// Which stored cards make it onto Today: not dismissed, not of a demoted kind, best two by
/// severity then distance, and never two cards that lead with the same metric (the second would
/// repeat the first; it stays in the archive).
/// </summary>
public static class InsightRanking
{
  public const int TodayCardLimit = 2;

  public static IReadOnlyList<Insight> SelectTodayCards(IEnumerable<Insight> insights, ISet<string> demotedKinds)
  {
    var ranked = insights
      .Where(i => i.DismissedAt == null && i.Kind != InsightKinds.Learning && !demotedKinds.Contains(i.Kind))
      .OrderByDescending(i => InsightSeverities.Rank(i.Severity))
      .ThenByDescending(InterpretMapping.BandDistanceOf)
      .ThenBy(i => i.Kind, StringComparer.Ordinal);

    var cards = new List<Insight>(TodayCardLimit);
    var leads = new HashSet<string>(StringComparer.Ordinal);
    foreach (var insight in ranked)
    {
      var lead = insight.Metrics is { Length: > 0 } metrics ? metrics[0] : null;
      if (lead != null && !leads.Add(lead)) continue;
      cards.Add(insight);
      if (cards.Count == TodayCardLimit) break;
    }
    return cards;
  }
}

/// <summary>
/// The name a person reads for a metric key. From the catalogue seed (the same names the metric
/// table holds), except sleep stages, which every summary and chart shows as minutes asleep.
/// </summary>
public static class MetricNames
{
  private static readonly IReadOnlyDictionary<string, string> Names =
    SelfKept.Infrastructure.Data.MetricSeed.All.ToDictionary(m => m.Key, m => m.DisplayName, StringComparer.Ordinal);

  public static string For(string key) => key switch
  {
    MetricKeys.SleepStage => "Sleep",
    _ => Names.TryGetValue(key, out var name) ? name : key,
  };

  /// <summary>
  /// The name as it reads inside a sentence: "resting heart rate", "wrist temperature change".
  /// A name that opens with an acronym or a figure ("HRV (RMSSD)", "VO2 max") keeps its capitals.
  /// </summary>
  public static string InSentence(string key) => LowerFirst(For(key));

  /// <summary>Lowers the first letter of an ordinary first word; an acronym or a word with a figure stays as it is.</summary>
  public static string LowerFirst(string name)
  {
    if (name.Length == 0) return name;
    var space = name.IndexOf(' ');
    var first = space < 0 ? name : name[..space];
    if (first.Skip(1).Any(c => char.IsUpper(c) || char.IsDigit(c))) return name;
    return char.ToLowerInvariant(name[0]) + name[1..];
  }

  /// <summary>"is" or "are" after the metric's name: "Steps are", "HRV (RMSSD) is". Sleep reads as "Sleep is".</summary>
  public static string Is(string key) => IsPlural(key) ? "are" : "is";

  /// <summary>"was" or "were" after the metric's name.</summary>
  public static string Was(string key) => IsPlural(key) ? "were" : "was";

  private static bool IsPlural(string key) => key != MetricKeys.SleepStage && SelfKept.Infrastructure.Data.MetricSeed.PluralNames.Contains(key);
}
