using System.Globalization;
using SelfKept.Core.Entities;
using SelfKept.Core.Formatting;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Interpret.Rules;

/// <summary>
/// Shared plumbing for the rules in docs/insights-design.md. A rule is pure: it reads the
/// pre-loaded context and returns at most one candidate. The minimum-data gate (n28 >= 14 for
/// every metric used) is applied by the engine, so a rule only has to say which metrics it reads.
/// </summary>
public abstract class RuleBase : IInsightRule
{
  public abstract string Kind { get; }

  public abstract IReadOnlyList<string> MetricsUsed(RuleContext context);

  public abstract InsightCandidate? Evaluate(RuleContext context);

  protected InsightCandidate Candidate(string severity, IReadOnlyList<string> metrics, string templated, double bandDistance, params (string Key, object? Value)[] evidence)
  {
    var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
    foreach (var (key, value) in evidence) dict[key] = value;
    return new InsightCandidate(Kind, severity, metrics, dict, templated, Math.Round(bandDistance, 3));
  }

  protected static double Round(double value, int decimals = 1) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

  protected static string Format(double value, int decimals = 1) => Round(value, decimals).ToString("0.#", CultureInfo.InvariantCulture);

  protected static double[] Present(double?[] values) => values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();

  protected static string[] IsoDays(RuleContext context, IEnumerable<int> offsetsBack) =>
    offsetsBack.Select(o => context.Day.AddDays(-o).ToIso()).ToArray();

  protected static double Iqr(Baseline b) => BaselineMath.Scale(b.P25_28 ?? 0, b.P75_28 ?? 0);
}
