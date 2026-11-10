namespace SelfKept.Core.Entities;

/// <summary>
/// A single change with a planned check (docs/cross-reference.md, "Trials"): "oats instead of a
/// bagel until the December panel". The title, what changed and the outcome note are the person's
/// own words and are encrypted; the dates, the linked readings and the status are plain so the
/// rules can tell when a trial is ready to read. Read under the body grant.
/// </summary>
public class Trial
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }

  public string TitleCipher { get; set; } = string.Empty;

  /// <summary>What changed, in the person's words. Encrypted. Optional.</summary>
  public string? ChangeCipher { get; set; }

  public DateOnly StartedOn { get; set; }

  /// <summary>The planned date to read the result: usually the next panel.</summary>
  public DateOnly ReadOn { get; set; }

  /// <summary>The readings expected to move: lab.* marker keys or wearable metric keys.</summary>
  public string[] MetricKeys { get; set; } = [];

  /// <summary>active, done or dropped. See <see cref="TrialStatuses"/>.</summary>
  public string Status { get; set; } = TrialStatuses.Active;

  /// <summary>What the person made of it, in their words. Encrypted. Optional.</summary>
  public string? OutcomeCipher { get; set; }

  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
  public DateTime? EndedAt { get; set; }

  public bool HasLabMarkers => MetricKeys.Any(k => k.StartsWith(Entities.MetricKeys.LabPrefix, StringComparison.Ordinal));
}

public static class TrialStatuses
{
  public const string Active = "active";
  public const string Done = "done";
  public const string Dropped = "dropped";
  public static readonly string[] All = [Active, Done, Dropped];
}
