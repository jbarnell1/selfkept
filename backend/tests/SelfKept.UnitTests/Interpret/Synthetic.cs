using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret;

namespace SelfKept.UnitTests.Interpret;

/// <summary>
/// Builds a RuleContext from 28-day arrays (oldest first, null for a day without data), with the
/// baselines computed the same way the nightly job computes them.
/// </summary>
public static class Synthetic
{
  public static readonly DateOnly Day = new(2026, 10, 29);
  public static readonly DateTime Computed = new(2026, 10, 30, 3, 30, 0, DateTimeKind.Utc);
  public static readonly Guid PersonId = Guid.NewGuid();

  public static RuleContext Context(Dictionary<string, double?[]> series, DateOnly? day = null)
  {
    var end = day ?? Day;
    var rows = new Dictionary<string, IReadOnlyList<DailySummary>>(StringComparer.Ordinal);
    var baselines = new Dictionary<string, Baseline>(StringComparer.Ordinal);
    foreach (var (metric, values) in series)
    {
      if (values.Length != BaselineService.WindowDays) throw new ArgumentException($"{metric}: give exactly {BaselineService.WindowDays} days.");
      var list = new List<DailySummary>();
      for (var i = 0; i < values.Length; i++)
      {
        if (values[i] is { } v)
        {
          list.Add(new DailySummary { PersonId = PersonId, MetricKey = metric, Day = end.AddDays(-(values.Length - 1 - i)), Value = v, Count = 1, ComputedAt = Computed });
        }
      }
      rows[metric] = list;
      if (BaselineService.Compute(PersonId, metric, end, list, Computed) is { } baseline) baselines[metric] = baseline;
    }
    return new RuleContext(new Person { Id = PersonId, DisplayName = "Test", TimeZone = "UTC" }, end, baselines, rows);
  }

  /// <summary>28 values: the first (28 - tail.Length) are the steady value, then the tail.</summary>
  public static double?[] Steady(double steady, params double?[] tail)
  {
    var result = new double?[BaselineService.WindowDays];
    for (var i = 0; i < result.Length; i++) result[i] = steady;
    for (var i = 0; i < tail.Length; i++) result[result.Length - tail.Length + i] = tail[i];
    return result;
  }

  /// <summary>A slow ramp around a steady value, so percentiles are not all one number.</summary>
  public static double?[] Wobble(double centre, double amplitude, params double?[] tail)
  {
    var result = new double?[BaselineService.WindowDays];
    for (var i = 0; i < result.Length; i++) result[i] = centre + amplitude * ((i % 5) - 2) / 2.0;
    for (var i = 0; i < tail.Length; i++) result[result.Length - tail.Length + i] = tail[i];
    return result;
  }

  /// <summary>Only the last <paramref name="days"/> days have data.</summary>
  public static double?[] OnlyLast(int days, double value)
  {
    var result = new double?[BaselineService.WindowDays];
    for (var i = result.Length - days; i < result.Length; i++) result[i] = value;
    return result;
  }
}
