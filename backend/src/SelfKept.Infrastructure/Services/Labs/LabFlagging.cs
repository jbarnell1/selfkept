using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Services.Labs;

/// <summary>Where a value sits against a reference row, and how seriously that is taken.</summary>
public sealed record LabAssessment(string Position, string? Severity, double Distance)
{
  public bool OutOfRange => Position != LabPositions.Inside;
}

public static class LabPositions
{
  public const string Below = "below";
  public const string Inside = "inside";
  public const string Above = "above";
}

/// <summary>
/// Compares a value with its population range (docs/lab-ranges.md). The row's tier is the base
/// severity for an out-of-range value; the "Flag tiers" table narrows a clinician row to the
/// values that should prompt a clinician conversation, and leaves the rest at watch. A row
/// without a tier (the LDL to ApoB ratio) is never flagged.
/// </summary>
public static class LabFlagging
{
  /// <summary>The clinician triggers from the flag tiers table: (metric, value, row) to whether the clinician tier applies.</summary>
  private static readonly Dictionary<string, Func<double, LabReference, bool>> ClinicianTriggers = new(StringComparer.Ordinal)
  {
    [LabMetricKeys.Tsh] = (v, _) => v < 0.1 || v > 10,
    [LabMetricKeys.FreeT4] = (v, r) => IsOut(v, r),
    [LabMetricKeys.Egfr] = (v, _) => v < 60,
    [LabMetricKeys.Creatinine] = (v, r) => r.High is { } high && v > high,
    [LabMetricKeys.Hemoglobin] = (v, r) => r.Low is { } low && v < low,
    [LabMetricKeys.Hematocrit] = (v, r) => r.Low is { } low && v < low,
    [LabMetricKeys.HsCrp] = (v, _) => v > 10,
    [LabMetricKeys.Fructosamine] = (v, r) => r.High is { } high && v > high,
    [LabMetricKeys.Cortisol] = (v, _) => v < 5 || v > 25,
    [LabMetricKeys.Albumin] = (v, r) => r.Low is { } low && v < low,
    [LabMetricKeys.Ferritin] = (v, _) => v < 15 || v > 1000,
    [LabMetricKeys.VitaminD] = (v, _) => v < 12 || v > 80,
    [LabMetricKeys.Triglycerides] = (v, _) => v >= 500,
    [LabMetricKeys.Ldl] = (v, _) => v >= 190,
    [LabMetricKeys.UricAcid] = (v, _) => v > 9,
  };

  /// <summary>Null when the row has no range to compare with.</summary>
  public static LabAssessment? Assess(LabReference? reference, double value)
  {
    if (reference == null || !reference.HasRange) return null;
    var position = Position(reference, value);
    if (position == LabPositions.Inside) return new LabAssessment(position, null, 0);

    var severity = reference.Tier switch
    {
      LabTiers.None => null,
      LabTiers.Clinician when ClinicianTriggers.TryGetValue(reference.MetricKey, out var trigger) && !trigger(value, reference) => InsightSeverities.Watch,
      LabTiers.Clinician => InsightSeverities.Clinician,
      _ => ClinicianTriggers.TryGetValue(reference.MetricKey, out var trigger) && trigger(value, reference) ? InsightSeverities.Clinician : InsightSeverities.Watch,
    };
    return new LabAssessment(position, severity, Distance(reference, value));
  }

  public static string Position(LabReference reference, double value)
  {
    if (reference.Low is { } low && value < low) return LabPositions.Below;
    if (reference.High is { } high && value > high) return LabPositions.Above;
    return LabPositions.Inside;
  }

  /// <summary>
  /// How far outside the range, as a fraction of the range width (or of the one bound when the
  /// row has a single bound). Ranks markers against each other on a card.
  /// </summary>
  public static double Distance(LabReference reference, double value)
  {
    var width = reference.Low is { } l && reference.High is { } h && h > l ? h - l : Math.Abs(reference.High ?? reference.Low ?? 1);
    if (width <= 0) width = 1;
    if (reference.Low is { } low && value < low) return (low - value) / width;
    if (reference.High is { } high && value > high) return (value - high) / width;
    return 0;
  }

  private static bool IsOut(double value, LabReference reference) => Position(reference, value) != LabPositions.Inside;

  /// <summary>
  /// Whether a move from old to new is in the direction of worse for the marker: up for a
  /// lower-is-better marker, down for higher-is-better, and away from the centre of the range
  /// (or past the single bound) for a range marker.
  /// </summary>
  public static bool IsWorse(LabReference reference, double previous, double latest)
  {
    switch (reference.GoodDirection)
    {
      case GoodDirections.Down:
        return latest > previous;
      case GoodDirections.Up:
        return latest < previous;
      default:
        if (reference.Low is { } low && reference.High is { } high)
        {
          var mid = (low + high) / 2;
          return Math.Abs(latest - mid) > Math.Abs(previous - mid);
        }
        if (reference.High is { } onlyHigh) return latest > previous && latest > onlyHigh * 0.5;
        if (reference.Low is { } onlyLow) return latest < previous && latest < onlyLow * 1.5;
        return false;
    }
  }
}
