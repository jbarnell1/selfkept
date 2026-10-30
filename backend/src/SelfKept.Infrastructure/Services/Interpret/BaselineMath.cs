namespace SelfKept.Infrastructure.Services.Interpret;

/// <summary>
/// The arithmetic behind a baseline row (docs/insights-design.md, "Baselines"). Pure functions
/// over the values present in a window; gaps are simply absent, never zero.
/// </summary>
public static class BaselineMath
{
  /// <summary>Median of the values; the mean of the two middle values for an even count. Null when empty.</summary>
  public static double? Median(IReadOnlyList<double> values) => Percentile(values, 50);

  /// <summary>
  /// Percentile with linear interpolation between the two nearest ranks (the "R-7" definition
  /// spreadsheets use): rank = p / 100 * (n - 1), interpolated between floor and ceil. Null when empty.
  /// </summary>
  public static double? Percentile(IReadOnlyList<double> values, double percentile)
  {
    if (values.Count == 0) return null;
    if (percentile is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percentile), "A percentile lies between 0 and 100.");
    var sorted = values.OrderBy(v => v).ToArray();
    if (sorted.Length == 1) return sorted[0];
    var rank = percentile / 100 * (sorted.Length - 1);
    var lower = (int)Math.Floor(rank);
    var upper = (int)Math.Ceiling(rank);
    if (lower == upper) return sorted[lower];
    var weight = rank - lower;
    return sorted[lower] + (sorted[upper] - sorted[lower]) * weight;
  }

  /// <summary>
  /// Least-squares slope per day of value against day index (so a missing day keeps its distance).
  /// Null with fewer than two points or when every point falls on one day.
  /// </summary>
  public static double? Slope(IReadOnlyList<(int DayIndex, double Value)> points)
  {
    if (points.Count < 2) return null;
    var meanX = points.Average(p => p.DayIndex);
    var meanY = points.Average(p => p.Value);
    double sxx = 0, sxy = 0;
    foreach (var (x, y) in points)
    {
      sxx += (x - meanX) * (x - meanX);
      sxy += (x - meanX) * (y - meanY);
    }
    if (sxx == 0) return null;
    return sxy / sxx;
  }

  /// <summary>Sample standard deviation (n - 1). Null with fewer than two values.</summary>
  public static double? StandardDeviation(IReadOnlyList<double> values)
  {
    if (values.Count < 2) return null;
    var mean = values.Average();
    var sum = values.Sum(v => (v - mean) * (v - mean));
    return Math.Sqrt(sum / (values.Count - 1));
  }

  /// <summary>
  /// How far a value sits outside [low, high] as a fraction of the interquartile range, 0 when
  /// inside. See <see cref="Scale"/> for the floor under the IQR.
  /// </summary>
  public static double BandDistance(double value, double low, double high, double p25, double p75)
  {
    var scale = Scale(p25, p75);
    if (value < low) return (low - value) / scale;
    if (value > high) return (value - high) / scale;
    return 0;
  }

  /// <summary>
  /// The unit distances are measured in: the interquartile range, floored at 5 percent of p75 so
  /// a metric that barely moves (28 identical nights) does not turn a small excursion into a
  /// huge number, and never zero.
  /// </summary>
  public static double Scale(double p25, double p75) => Math.Max(p75 - p25, Math.Max(Math.Abs(p75) * 0.05, 1e-9));
}
