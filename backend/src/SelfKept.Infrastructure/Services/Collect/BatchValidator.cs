using System.Text.Json;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Collect;

/// <summary>
/// Validates one posted sample or workout against the catalogue and the contract rules, and
/// turns a valid one into an entity. Rejections are per item with a reason the client can log;
/// the batch as a whole is never failed by one bad sample.
/// </summary>
public static class BatchValidator
{
  public const int MaxBatchItems = 5000;
  public const int MaxSourceUidLength = 128;
  public const int MaxMetaBytes = 2048;
  public const int MaxWorkoutTypeLength = 64;

  public sealed record SampleOutcome(Sample? Sample, string? Reason)
  {
    public bool Accepted => Sample != null;
  }

  public sealed record WorkoutOutcome(Workout? Workout, string? Reason)
  {
    public bool Accepted => Workout != null;
  }

  public static SampleOutcome ValidateSample(SampleInput input, IMetricCatalogue catalogue, Source source, DateTime nowUtc)
  {
    if (string.IsNullOrWhiteSpace(input.SourceUid)) return Reject("sourceUid is required");
    if (input.SourceUid.Length > MaxSourceUidLength) return Reject($"sourceUid is longer than {MaxSourceUidLength} characters");
    if (string.IsNullOrWhiteSpace(input.Metric)) return Reject("metric is required");

    var metric = catalogue.Find(input.Metric.Trim());
    if (metric == null) return Reject($"unknown metric '{input.Metric}'");
    if (metric.IsLab) return Reject($"'{metric.Key}' arrives through the labs endpoints, not ingest");
    if (metric.Key == MetricKeys.CheckinMood) return Reject("checkin_mood is written by POST /api/checkins");

    if (input.Start is not { } start) return Reject("start is required");
    var startUtc = DayBoundaries.AsUtc(start);
    if (startUtc > nowUtc.AddDays(1)) return Reject("start is more than a day in the future");
    if (startUtc.Year < 1900) return Reject("start is before 1900");

    DateTime? endUtc = input.End is { } end ? DayBoundaries.AsUtc(end) : null;
    if (endUtc != null && endUtc < startUtc) return Reject("end is before start");
    if (endUtc == null && metric.Kind == MetricKinds.Interval) return Reject($"end is required for interval metric '{metric.Key}'");

    if (input.Value is not { } rawValue || !double.IsFinite(rawValue)) return Reject("value must be a finite number");
    if (string.IsNullOrWhiteSpace(input.Unit)) return Reject($"unit is required (expected '{metric.Unit}')");

    var value = UnitConversion.Convert(metric, input.Unit, rawValue);
    if (value == null) return Reject($"unit '{input.Unit}' is not accepted for '{metric.Key}' (expected '{metric.Unit}')");

    if (metric.ValueLabels != null && !IsLabelledValue(metric, value.Value))
    {
      return Reject($"value {rawValue} is not one of the levels defined for '{metric.Key}'");
    }

    var meta = SerialiseMeta(input.Meta);
    if (meta is { Length: > MaxMetaBytes }) return Reject($"meta is larger than {MaxMetaBytes} bytes");

    return new SampleOutcome(new Sample
    {
      Id = Guid.CreateVersion7(),
      PersonId = source.PersonId,
      SourceId = source.Id,
      SourceUid = input.SourceUid.Trim(),
      MetricKey = metric.Key,
      StartAt = startUtc,
      EndAt = endUtc,
      Value = value.Value,
      Unit = metric.Unit,
      Meta = meta,
      CreatedAt = nowUtc,
    }, null);

    static SampleOutcome Reject(string reason) => new(null, reason);
  }

  public static WorkoutOutcome ValidateWorkout(WorkoutInput input, Source source, DateTime nowUtc, Func<string, byte[]> encryptRoute)
  {
    if (string.IsNullOrWhiteSpace(input.SourceUid)) return Reject("sourceUid is required");
    if (input.SourceUid.Length > MaxSourceUidLength) return Reject($"sourceUid is longer than {MaxSourceUidLength} characters");
    if (string.IsNullOrWhiteSpace(input.Type)) return Reject("type is required");
    if (input.Type.Length > MaxWorkoutTypeLength) return Reject($"type is longer than {MaxWorkoutTypeLength} characters");
    if (input.Start is not { } start) return Reject("start is required");
    if (input.End is not { } end) return Reject("end is required for a workout");
    var startUtc = DayBoundaries.AsUtc(start);
    var endUtc = DayBoundaries.AsUtc(end);
    if (endUtc < startUtc) return Reject("end is before start");
    foreach (var (name, number) in new[] { ("distanceM", input.DistanceM), ("energyKcal", input.EnergyKcal), ("avgHr", input.AvgHr), ("maxHr", input.MaxHr) })
    {
      if (number is { } n && (!double.IsFinite(n) || n < 0)) return Reject($"{name} must be a non-negative number");
    }

    var meta = SerialiseMeta(input.Meta);
    if (meta is { Length: > MaxMetaBytes }) return Reject($"meta is larger than {MaxMetaBytes} bytes");

    byte[]? routeCipher = null;
    if (input.RouteGeoJson is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } route)
    {
      routeCipher = encryptRoute(route.GetRawText());
    }

    return new WorkoutOutcome(new Workout
    {
      Id = Guid.CreateVersion7(),
      PersonId = source.PersonId,
      SourceId = source.Id,
      SourceUid = input.SourceUid.Trim(),
      Type = input.Type.Trim().ToLowerInvariant(),
      StartAt = startUtc,
      EndAt = endUtc,
      DistanceM = input.DistanceM,
      EnergyKcal = input.EnergyKcal,
      AvgHr = input.AvgHr,
      MaxHr = input.MaxHr,
      RouteCipher = routeCipher,
      Meta = meta,
      CreatedAt = nowUtc,
    }, null);

    static WorkoutOutcome Reject(string reason) => new(null, reason);
  }

  /// <summary>A categorical metric (one with value_labels) only accepts the integer levels it defines.</summary>
  private static bool IsLabelledValue(Metric metric, double value)
  {
    if (value != Math.Floor(value)) return false;
    try
    {
      using var doc = JsonDocument.Parse(metric.ValueLabels!);
      return doc.RootElement.ValueKind == JsonValueKind.Object
        && doc.RootElement.TryGetProperty(((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture), out _);
    }
    catch (JsonException)
    {
      return true;
    }
  }

  private static string? SerialiseMeta(JsonElement? meta) =>
    meta is { ValueKind: JsonValueKind.Object } m && m.EnumerateObject().Any() ? m.GetRawText() : null;
}
