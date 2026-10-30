using System.Globalization;
using System.Text.Json;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Services.Oura;

/// <summary>
/// Turns Oura API v2 documents into ingest samples (docs/connect-design.md, "Oura"). Field names
/// follow Oura's OpenAPI 1.43. Pure, so it is tested on saved documents without the network.
///
/// What comes in is what only Oura's cloud has: its daily scores, resilience, stress time,
/// temperature deviation, SpO2, and per-night sleep stages, HRV, heart rate, breathing and lowest
/// heart rate. Daytime heart rate, steps and workouts are left out on purpose: the Oura app
/// already writes those into Apple Health, so the phone brings them and a second copy would only
/// be thrown away by the per-origin daily summaries.
///
/// Every sample carries <c>{"origin":"oura-cloud"}</c> so summaries count it as one device, and a
/// sourceUid built from the Oura document id, so a re-pull of the same days changes nothing.
/// </summary>
public static class OuraMapper
{
  public const string Origin = "oura-cloud";

  /// <summary>The collections the poller reads, in order, with the query it needs.</summary>
  public static readonly IReadOnlyList<string> Collections =
    ["daily_sleep", "daily_readiness", "daily_activity", "daily_stress", "daily_resilience", "daily_spo2", "sleep"];

  private static readonly JsonElement OriginMeta = JsonSerializer.SerializeToElement(new { origin = Origin });

  private static readonly Dictionary<string, int> ResilienceLevels = new(StringComparer.Ordinal)
  {
    ["limited"] = 1, ["adequate"] = 2, ["solid"] = 3, ["strong"] = 4, ["exceptional"] = 5,
  };

  /// <summary>Oura's 5-minute phases ('1' deep, '2' light, '3' REM, '4' awake) to the catalogue's sleep stages.</summary>
  private static int? StageFor(char phase) => phase switch
  {
    '1' => SleepStages.Deep,
    '2' => SleepStages.Light,
    '3' => SleepStages.Rem,
    '4' => SleepStages.Awake,
    _ => null,
  };

  public static List<SampleInput> Map(string collection, JsonElement document)
  {
    var samples = new List<SampleInput>();
    var id = Str(document, "id");
    if (id == null) return samples;

    switch (collection)
    {
      case "daily_sleep":
        AddDaily(samples, document, $"oura:daily_sleep:{id}", MetricKeys.OuraSleepScore, Num(document, "score"), "score");
        break;

      case "daily_readiness":
        AddDaily(samples, document, $"oura:daily_readiness:{id}", MetricKeys.OuraReadiness, Num(document, "score"), "score");
        if (Num(document, "temperature_deviation") is { } deviation && Day(document) is { } readinessDay)
        {
          samples.Add(new SampleInput($"oura:daily_readiness:{id}:temp", MetricKeys.WristTempDeltaC, Noon(readinessDay), null, Math.Round(deviation, 2), "C",
            JsonSerializer.SerializeToElement(new { origin = Origin, kind = "delta" })));
        }
        break;

      case "daily_activity":
        AddDaily(samples, document, $"oura:daily_activity:{id}", "oura_activity_score", Num(document, "score"), "score");
        break;

      case "daily_stress":
        AddDaily(samples, document, $"oura:daily_stress:{id}", "oura_stress_high_s", Num(document, "stress_high"), "s");
        break;

      case "daily_resilience":
        if (Str(document, "level") is { } level && ResilienceLevels.TryGetValue(level, out var levelValue))
          AddDaily(samples, document, $"oura:daily_resilience:{id}", "oura_resilience", levelValue, "level");
        break;

      case "daily_spo2":
        if (document.TryGetProperty("spo2_percentage", out var spo2) && spo2.ValueKind == JsonValueKind.Object && Num(spo2, "average") is { } average && average > 0)
          AddDaily(samples, document, $"oura:daily_spo2:{id}", MetricKeys.Spo2Pct, Math.Round(average, 1), "%");
        break;

      case "sleep":
        MapSleepPeriod(samples, document, id);
        break;
    }
    return samples;
  }

  private static void MapSleepPeriod(List<SampleInput> samples, JsonElement doc, string id)
  {
    var type = Str(doc, "type");
    // "rest" is a rest period Oura detected while awake, "deleted" a removed one: neither is sleep.
    if (type is "rest" or "deleted") return;
    if (Time(doc, "bedtime_start") is not { } start || Time(doc, "bedtime_end") is not { } end) return;

    // Stages: one sample per run of the same phase, so a night is a few dozen rows, not a hundred.
    if (Str(doc, "sleep_phase_5_min") is { Length: > 0 } phases)
    {
      var run = 0;
      var i = 0;
      while (i < phases.Length)
      {
        var phase = phases[i];
        var j = i;
        while (j < phases.Length && phases[j] == phase) j++;
        if (StageFor(phase) is { } stage)
        {
          var segStart = start.AddMinutes(5 * i);
          var segEnd = start.AddMinutes(5 * j);
          if (segEnd > end) segEnd = end;
          if (segEnd > segStart)
            samples.Add(new SampleInput($"oura:sleep:{id}:stage:{run}", MetricKeys.SleepStage, segStart, segEnd, stage, "stage", OriginMeta));
        }
        run++;
        i = j;
      }
    }

    AddSeries(samples, doc, "hrv", $"oura:sleep:{id}:hrv", MetricKeys.HrvRmssdMs, "ms");
    AddSeries(samples, doc, "heart_rate", $"oura:sleep:{id}:hr", MetricKeys.HeartRateBpm, "bpm");

    if (Num(doc, "average_breath") is { } breath && breath > 0)
      samples.Add(new SampleInput($"oura:sleep:{id}:breath", MetricKeys.RespiratoryRateBpm, end, null, Math.Round(breath, 1), "breaths/min", OriginMeta));

    // The night's lowest heart rate stands in for resting heart rate, from the main sleep only.
    if (type == "long_sleep" && Num(doc, "lowest_heart_rate") is { } lowest && lowest > 0 && Day(doc) is { } day)
      samples.Add(new SampleInput($"oura:sleep:{id}:rhr", MetricKeys.RestingHrBpm, Noon(day), null, lowest, "bpm", OriginMeta));
  }

  /// <summary>Oura time series: { interval (seconds), items (nullable numbers), timestamp (first item) }.</summary>
  private static void AddSeries(List<SampleInput> samples, JsonElement doc, string property, string uidPrefix, string metric, string unit)
  {
    if (!doc.TryGetProperty(property, out var series) || series.ValueKind != JsonValueKind.Object) return;
    if (Num(series, "interval") is not { } interval || interval <= 0) return;
    if (Time(series, "timestamp") is not { } first) return;
    if (!series.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return;

    var index = 0;
    foreach (var item in items.EnumerateArray())
    {
      if (item.ValueKind == JsonValueKind.Number && item.GetDouble() is var value && value > 0)
        samples.Add(new SampleInput($"{uidPrefix}:{index}", metric, first.AddSeconds(interval * index), null, value, unit, OriginMeta));
      index++;
    }
  }

  private static void AddDaily(List<SampleInput> samples, JsonElement doc, string uid, string metric, double? value, string unit)
  {
    if (value is not { } v || Day(doc) is not { } day) return;
    samples.Add(new SampleInput(uid, metric, Noon(day), null, v, unit, OriginMeta));
  }

  /// <summary>Daily values sit at noon UTC: the same calendar day in every zone from UTC-11 to UTC+11.</summary>
  private static DateTime Noon(DateOnly day) => day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

  private static DateOnly? Day(JsonElement doc) =>
    Str(doc, "day") is { } text && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

  private static DateTime? Time(JsonElement doc, string name) =>
    Str(doc, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.UtcDateTime : null;

  private static string? Str(JsonElement doc, string name) =>
    doc.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

  private static double? Num(JsonElement doc, string name) =>
    doc.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
