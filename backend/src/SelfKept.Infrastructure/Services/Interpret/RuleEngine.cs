using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret.Rules;

namespace SelfKept.Infrastructure.Services.Interpret;

/// <summary>
/// Runs every rule over one context and ranks what fired. The minimum-data gate lives here: a
/// rule may only fire when every metric it reads has n28 >= 14, and learning fires only when no
/// metric has reached that. Ranking is severity (clinician, watch, notice) then distance outside
/// the band as a fraction of the interquartile range.
/// </summary>
public sealed class RuleEngine : IRuleEngine
{
  private readonly IReadOnlyList<IInsightRule> _rules;

  public RuleEngine(IEnumerable<IInsightRule> rules)
  {
    _rules = rules.ToList();
  }

  /// <summary>The v0.1 rule set in design order.</summary>
  public static IReadOnlyList<IInsightRule> DefaultRules() =>
  [
    new HrvLowRunRule(),
    new HrvRecoveredRule(),
    new RhrElevatedRule(),
    new IllnessPatternRule(),
    new SleepDebtRule(),
    new SleepGoodWeekRule(),
    new ActivityDipRule(),
    new TrainingSpikeRule(),
    new WeightTrendRule(),
    new MoodLowRunRule(),
    new MoodSleepLinkRule(),
    new Spo2LowRule(),
    new LabOutOfRangeRule(),
    new LabTrendRule(),
    new LearningRule(),
  ];

  public IReadOnlyList<InsightCandidate> Evaluate(RuleContext context)
  {
    var candidates = new List<InsightCandidate>();
    foreach (var rule in _rules)
    {
      if (rule.Kind == InsightKinds.Learning)
      {
        if (!context.StillLearning) continue;
      }
      else
      {
        if (!context.Established(rule.MetricsUsed(context).ToArray())) continue;
      }

      var candidate = rule.Evaluate(context);
      if (candidate != null) candidates.Add(candidate);
    }
    return Rank(candidates);
  }

  public static IReadOnlyList<InsightCandidate> Rank(IEnumerable<InsightCandidate> candidates) =>
    candidates
      .OrderByDescending(c => c.Rank)
      .ThenByDescending(c => c.BandDistance)
      .ThenBy(c => c.Kind, StringComparer.Ordinal)
      .ToList();
}
