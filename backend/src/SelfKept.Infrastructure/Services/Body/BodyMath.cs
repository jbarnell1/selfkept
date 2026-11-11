namespace SelfKept.Infrastructure.Services.Body;

/// <summary>One reading on one day.</summary>
public readonly record struct DayValue(DateOnly Day, double Value);

/// <summary>
/// The arithmetic behind the Progress tab (docs/body-design.md, "Body maths"). Everything here is
/// plain arithmetic on a person's own readings. The smoothing and the outlier screen are
/// engineering choices, documented as such; none of it is a medical threshold.
/// </summary>
public static class BodyMath
{
  /// <summary>How much of each new day the trend line takes up. Ten percent a day settles in about three weeks.</summary>
  public const double TrendAlpha = 0.1;

  /// <summary>The weekly rate is a straight-line fit over this many days.</summary>
  public const int RateWindowDays = 28;

  /// <summary>A rate needs readings spread over at least this many days.</summary>
  public const int RateMinSpanDays = 14;

  /// <summary>A rate needs at least this many readings.</summary>
  public const int RateMinReadings = 6;

  /// <summary>A reading this far (as a share of the median of its neighbours) is set aside as not this person's.</summary>
  public const double OutlierShare = 0.06;

  /// <summary>Neighbours on each side that the outlier screen looks at.</summary>
  public const int OutlierNeighbours = 3;

  /// <summary>Lean share of loss is only said once this much weight has gone.</summary>
  public const double LeanShareMinLossKg = 2.0;

  public static double? Bmi(double? weightKg, double? heightCm) =>
    weightKg is { } w && heightCm is { } h && h > 0 ? w / Square(h / 100.0) : null;

  public static double? FatMassKg(double? weightKg, double? bodyFatPct) =>
    weightKg is { } w && bodyFatPct is { } f ? w * f / 100.0 : null;

  public static double? LeanMassKg(double? weightKg, double? bodyFatPct) =>
    weightKg is { } w && bodyFatPct is { } f ? w * (1 - f / 100.0) : null;

  /// <summary>Lean mass over height squared, the lean counterpart of BMI.</summary>
  public static double? LeanMassIndex(double? leanMassKg, double? heightCm) =>
    leanMassKg is { } l && heightCm is { } h && h > 0 ? l / Square(h / 100.0) : null;

  public static double? WaistToHeight(double? waistCm, double? heightCm) =>
    waistCm is { } w && heightCm is { } h && h > 0 ? w / h : null;

  /// <summary>
  /// Splits a weight series into the readings to trust and the ones to set aside. A reading is set
  /// aside when it sits more than <see cref="OutlierShare"/> away from the median of the readings
  /// around it: someone else stepping on the scale, or a bag in hand. With fewer than four
  /// readings nothing is set aside, because there is nothing to compare against.
  /// </summary>
  public static (IReadOnlyList<DayValue> Kept, IReadOnlyList<DayValue> SetAside) Screen(IReadOnlyList<DayValue> readings)
  {
    if (readings.Count < 4) return (readings, []);
    var kept = new List<DayValue>(readings.Count);
    var aside = new List<DayValue>();
    for (var i = 0; i < readings.Count; i++)
    {
      var neighbours = new List<double>(OutlierNeighbours * 2);
      for (var j = Math.Max(0, i - OutlierNeighbours); j <= Math.Min(readings.Count - 1, i + OutlierNeighbours); j++)
      {
        if (j != i) neighbours.Add(readings[j].Value);
      }
      var median = Median(neighbours);
      if (median > 0 && Math.Abs(readings[i].Value - median) / median > OutlierShare) aside.Add(readings[i]);
      else kept.Add(readings[i]);
    }
    return (kept, aside);
  }

  /// <summary>
  /// The smoothed line through a series: an exponentially weighted average that takes up
  /// <see cref="TrendAlpha"/> of each day's reading. A gap of several days takes up more, as if
  /// the missing days had read the same, so the line does not lag after a holiday.
  /// </summary>
  public static IReadOnlyList<DayValue> Trend(IReadOnlyList<DayValue> readings)
  {
    var trend = new List<DayValue>(readings.Count);
    double? level = null;
    DateOnly? previous = null;
    foreach (var reading in readings)
    {
      if (level is not { } current || previous is not { } last)
      {
        level = reading.Value;
      }
      else
      {
        var gap = Math.Max(1, reading.Day.DayNumber - last.DayNumber);
        var weight = 1 - Math.Pow(1 - TrendAlpha, gap);
        level = current + weight * (reading.Value - current);
      }
      previous = reading.Day;
      trend.Add(new DayValue(reading.Day, level.Value));
    }
    return trend;
  }

  /// <summary>
  /// Change per week from a straight-line fit over the last <see cref="RateWindowDays"/> days
  /// ending on <paramref name="today"/>. Null until there are enough readings over enough days.
  /// </summary>
  public static double? WeeklyRate(IReadOnlyList<DayValue> readings, DateOnly today, int windowDays = RateWindowDays)
  {
    var from = today.AddDays(-(windowDays - 1));
    var window = readings.Where(r => r.Day >= from && r.Day <= today).ToList();
    if (window.Count < RateMinReadings) return null;
    if (window[^1].Day.DayNumber - window[0].Day.DayNumber < RateMinSpanDays) return null;
    return Slope(window) is { } slope ? slope * 7 : null;
  }

  /// <summary>Least squares slope in units per day. Null when every reading is on the same day.</summary>
  public static double? Slope(IReadOnlyList<DayValue> readings)
  {
    if (readings.Count < 2) return null;
    var x0 = readings[0].Day.DayNumber;
    var meanX = readings.Average(r => (double)(r.Day.DayNumber - x0));
    var meanY = readings.Average(r => r.Value);
    double num = 0, den = 0;
    foreach (var r in readings)
    {
      var dx = r.Day.DayNumber - x0 - meanX;
      num += dx * (r.Value - meanY);
      den += dx * dx;
    }
    return den == 0 ? null : num / den;
  }

  /// <summary>
  /// Of the weight lost between two points, the share that was lean mass (0 to 1). Null unless
  /// both points have a body fat reading and at least <see cref="LeanShareMinLossKg"/> has gone.
  /// A bathroom scale estimates body fat, so this is a rough guide and the app says so.
  /// </summary>
  public static double? LeanShareOfLoss(double startWeightKg, double startBodyFatPct, double endWeightKg, double endBodyFatPct)
  {
    var lost = startWeightKg - endWeightKg;
    if (lost < LeanShareMinLossKg) return null;
    var leanLost = LeanMassKg(startWeightKg, startBodyFatPct)!.Value - LeanMassKg(endWeightKg, endBodyFatPct)!.Value;
    return Math.Clamp(leanLost / lost, 0, 1);
  }

  public static double Median(IReadOnlyList<double> values)
  {
    if (values.Count == 0) return 0;
    var sorted = values.OrderBy(v => v).ToList();
    var mid = sorted.Count / 2;
    return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
  }

  private static double Square(double v) => v * v;
}

/// <summary>The strength log's one sum.</summary>
public static class StrengthMath
{
  /// <summary>Above this many reps the estimate stops being useful and is not given.</summary>
  public const int MaxRepsForEstimate = 12;

  /// <summary>
  /// An estimated one-rep max from a set, by the Epley formula: weight times (1 + reps / 30). One
  /// rep is the weight itself. Null for a bodyweight set (weight 0) or more than twelve reps. It
  /// is only used to compare a person's own sets over time.
  /// </summary>
  public static double? EstimatedMaxKg(double weightKg, int reps)
  {
    if (weightKg <= 0 || reps < 1 || reps > MaxRepsForEstimate) return null;
    return reps == 1 ? weightKg : weightKg * (1 + reps / 30.0);
  }
}
