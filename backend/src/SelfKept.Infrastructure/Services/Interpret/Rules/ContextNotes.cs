using SelfKept.Core.Entities;
using SelfKept.Core.Formatting;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Interpret.Rules;

/// <summary>
/// Context before alarm (docs/context-design.md, "How rules use context"). When something the
/// person logged, or the record shows, explains a reading, the card says so and steps down one
/// severity. With no context active the candidate is returned untouched, so a rule behaves exactly
/// as it did before context existed.
/// </summary>
public static class ContextNotes
{
  /// <summary>The kinds that explain a raised heart rate, a low HRV or a temperature rise.</summary>
  public static readonly string[] Recovery =
    [ContextKinds.Illness, ContextKinds.HardTraining, ContextKinds.Travel, ContextKinds.CyclePhase, ContextKinds.MedicationChange];

  /// <summary>The kinds that explain a run of short or broken nights.</summary>
  public static readonly string[] Sleep = [ContextKinds.Illness, ContextKinds.Travel];

  /// <summary>The kinds that explain a quieter week.</summary>
  public static readonly string[] Activity = [ContextKinds.Illness, ContextKinds.Travel];

  /// <summary>The kinds that explain weight moving.</summary>
  public static readonly string[] Weight =
    [ContextKinds.ScaleChange, ContextKinds.Illness, ContextKinds.CyclePhase, ContextKinds.MedicationChange, ContextKinds.NewGoal];

  /// <summary>The kinds that explain readings that look like illness.</summary>
  public static readonly string[] IllnessLike = [ContextKinds.Illness, ContextKinds.CyclePhase, ContextKinds.Travel, ContextKinds.HardTraining];

  /// <summary>
  /// Adds the context line to the card and, when <paramref name="demote"/> is set, lowers the
  /// severity one step (clinician to watch, watch to notice; notice stays notice). Evidence gains
  /// a "context" object and, when the severity moved, "demotedFrom".
  /// </summary>
  public static InsightCandidate Explain(RuleContext context, InsightCandidate candidate, bool demote, params string[] kinds)
  {
    var hit = context.Context.Explaining(kinds);
    if (hit == null) return candidate;

    var severity = demote ? Demote(candidate.Severity) : candidate.Severity;
    var evidence = new Dictionary<string, object?>(candidate.Evidence, StringComparer.Ordinal)
    {
      ["context"] = new Dictionary<string, object?>
      {
        ["kind"] = hit.Kind,
        ["since"] = hit.Since.ToIso(),
        ["source"] = hit.Source,
        ["detail"] = hit.Detail,
      },
    };
    if (severity != candidate.Severity) evidence["demotedFrom"] = candidate.Severity;
    return candidate with
    {
      Severity = severity,
      Evidence = evidence,
      Templated = $"{candidate.Templated} {Line(hit)}",
    };
  }

  /// <summary>"You logged a sore throat on Nov 3, 2026, which can explain readings like these."</summary>
  public static string Line(ActiveContext hit) => $"{hit.Reason}, which can explain readings like these.";

  public static string Demote(string severity) => severity switch
  {
    InsightSeverities.Clinician => InsightSeverities.Watch,
    _ => InsightSeverities.Notice,
  };
}
