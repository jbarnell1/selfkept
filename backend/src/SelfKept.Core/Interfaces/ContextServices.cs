using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;

namespace SelfKept.Core.Interfaces;

// Context, patterns, systems, read-backs and the weekly review (docs/context-design.md). Reads by
// a viewer take (viewerPersonId, targetPersonId) and answer null on a denial; "Own" methods are
// the owner's writes and the server's own reads for the nightly rules, where no grant applies.

/// <summary>
/// One piece of context that is active on the interpreted day: a stored event the person logged,
/// or something worked out from the record (a new goal, a medicine start, symptom tags, a hard
/// session). Reason is the plain sentence a card quotes ("You logged a sore throat on Nov 3, 2026").
/// </summary>
public sealed record ActiveContext(
  string Kind,
  DateOnly Since,
  DateOnly? Until,
  string Source,
  string Reason,
  Guid? EventId = null,
  string? Detail = null);

/// <summary>Where one target stood over the seven days ending on a given day.</summary>
public sealed record TargetSnapshot(Guid Id, string Label, string Period, int MetDays, int KnownDays, bool? Met)
{
  public int MissedDays => KnownDays - MetDays;
}

/// <summary>One strength set's estimated one-rep max (Epley), for comparing a person's own sets over time.</summary>
public sealed record LiftEstimate(DateOnly Day, string Lift, string LiftName, double EstimatedMaxKg);

/// <summary>
/// The paired comparison behind a personal pattern: nights after a day with the tag against nights
/// after a logged day without it. Low and high are the 25th and 75th percentiles; Overlap is the
/// share of "with" nights that sit inside the "without" nights' middle half.
/// </summary>
public sealed record PatternStats(
  string Tag,
  string MetricKey,
  int NightsWith,
  int NightsWithout,
  double MedianWith,
  double MedianWithout,
  double Difference,
  double WithLow,
  double WithHigh,
  double WithoutLow,
  double WithoutHigh,
  double Overlap,
  double NoiseLine,
  int WeekendNights,
  int ContextNights,
  bool Established);

/// <summary>
/// What the context-aware rules see for one person and day, loaded once by the context loader so
/// rules stay pure. Empty when nothing is stored.
/// </summary>
public sealed class ContextState
{
  public static readonly ContextState Empty = new();

  /// <summary>Context active on the interpreted day, stored and derived, most recent first.</summary>
  public IReadOnlyList<ActiveContext> Active { get; init; } = [];

  /// <summary>Days with at least one day log entry, with the tags written that day (possibly none).</summary>
  public IReadOnlyDictionary<DateOnly, IReadOnlyList<string>> Logged { get; init; } = new Dictionary<DateOnly, IReadOnlyList<string>>();

  /// <summary>Days of the last two weeks with hard training: a strength set, a long workout, a hard session tag or a logged event.</summary>
  public IReadOnlyCollection<DateOnly> HardTrainingDays { get; init; } = [];

  /// <summary>Estimated maxima of the strength sets of the last 120 days, oldest first.</summary>
  public IReadOnlyList<LiftEstimate> Strength { get; init; } = [];

  /// <summary>Active targets over the seven days ending on the interpreted day, the day before, and a week before.</summary>
  public IReadOnlyList<TargetSnapshot> Targets { get; init; } = [];
  public IReadOnlyList<TargetSnapshot> TargetsYesterday { get; init; } = [];
  public IReadOnlyList<TargetSnapshot> TargetsWeekAgo { get; init; } = [];

  /// <summary>Personal patterns established as of the interpreted day and not on any of the seven days before.</summary>
  public IReadOnlyList<PatternStats> NewPatterns { get; init; } = [];

  public bool Has(string kind) => Active.Any(a => a.Kind == kind);

  /// <summary>The first active context among the kinds, in the order given; the most recent one of that kind.</summary>
  public ActiveContext? Explaining(params string[] kinds)
  {
    foreach (var kind in kinds)
    {
      var hit = Active.Where(a => a.Kind == kind).OrderByDescending(a => a.Since).FirstOrDefault();
      if (hit != null) return hit;
    }
    return null;
  }

  public bool LoggedOn(DateOnly day) => Logged.ContainsKey(day);

  public IReadOnlyList<string> TagsOn(DateOnly day) => Logged.TryGetValue(day, out var tags) ? tags : [];
}

/// <summary>Context events are read under the insights grant.</summary>
public interface IContextEventRepository
{
  /// <summary>Events overlapping [from, to], newest start first. Null when the viewer may not see the target's insights.</summary>
  Task<IReadOnlyList<ContextEvent>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly from, DateOnly to, CancellationToken ct = default);

  /// <summary>Server-internal: the person's own events overlapping [from, to].</summary>
  Task<IReadOnlyList<ContextEvent>> ListOwnAsync(Guid personId, DateOnly from, DateOnly to, CancellationToken ct = default);

  Task<ContextEvent?> GetOwnAsync(Guid personId, Guid eventId, CancellationToken ct = default);
  Task<int> CountOwnAsync(Guid personId, CancellationToken ct = default);
  Task<ContextEvent> AddAsync(ContextEvent contextEvent, CancellationToken ct = default);
  Task UpdateAsync(ContextEvent contextEvent, CancellationToken ct = default);
  Task RemoveAsync(ContextEvent contextEvent, CancellationToken ct = default);
}

/// <summary>A day with entries in the day log and the tags written on it.</summary>
public sealed record LoggedDay(DateOnly Day, IReadOnlyList<string> Tags);

/// <summary>What a pattern is worked out from: logged days and the overnight daily figures.</summary>
public sealed record PatternInputs(IReadOnlyList<LoggedDay> Days, IReadOnlyList<DailySummary> Nights);

/// <summary>
/// Reads for patterns, read-backs and systems. Patterns are an insights product: one read gated on
/// the insights category, like Today. Sleep nights need the sleep grant.
/// </summary>
public interface IReadBackRepository
{
  /// <summary>Logged days and the overnight figures over [from, to]. Null when the viewer may not see the target's insights.</summary>
  Task<PatternInputs?> GetPatternInputsAsync(Guid viewerPersonId, Guid targetPersonId, IReadOnlyCollection<string> metricKeys, DateOnly from, DateOnly to, CancellationToken ct = default);

  /// <summary>sleep_stage samples ending in [fromUtc, toUtc), oldest first. Null when the viewer may not see the target's sleep.</summary>
  Task<IReadOnlyList<Sample>?> GetSleepSamplesAsync(Guid viewerPersonId, Guid targetPersonId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

  /// <summary>Server-internal: the person's own workouts starting in [fromUtc, toUtc).</summary>
  Task<IReadOnlyList<Workout>> ListOwnWorkoutsAsync(Guid personId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

  /// <summary>The share categories the viewer may read for the target: every category for oneself.</summary>
  Task<IReadOnlySet<ShareCategory>> AllowedCategoriesAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);
}

/// <summary>Builds the context the rules see for one person and day. Server-internal: own data only.</summary>
public interface IContextLoader
{
  Task<ContextState> LoadOwnAsync(Person person, DateOnly day, CancellationToken ct = default);
}

public interface IContextService
{
  /// <summary>Events overlapping the range and what is active today. Null when the viewer may not see the target's insights.</summary>
  Task<ContextDto?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

  Task<ContextEventDto> CreateAsync(Actor actor, SaveContextEventRequest request, CancellationToken ct = default);

  /// <summary>Null when the event is not the caller's.</summary>
  Task<ContextEventDto?> UpdateAsync(Actor actor, Guid eventId, UpdateContextEventRequest request, CancellationToken ct = default);

  /// <summary>False when the event is not the caller's.</summary>
  Task<bool> DeleteAsync(Actor actor, Guid eventId, CancellationToken ct = default);
}

public interface IPatternService
{
  /// <summary>Patterns as of today in the target's time zone. Null when the viewer may not see the target's insights.</summary>
  Task<PatternsDto?> GetAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);

  /// <summary>Patterns as of a given day. Null when the viewer may not see the target's insights.</summary>
  Task<PatternsDto?> GetAsOfAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly day, CancellationToken ct = default);
}

public interface IReadBackService
{
  /// <summary>Night by night sleep detail. Null when the viewer may not see the target's sleep.</summary>
  Task<SleepNightsDto?> GetSleepAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

  /// <summary>Workouts and weekly totals. Null when the viewer may not see the target's activity.</summary>
  Task<WorkoutsDto?> GetWorkoutsAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
}

public interface ISystemsService
{
  /// <summary>Null when the person does not exist or the viewer may see none of their categories.</summary>
  Task<SystemsDto?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);

  /// <summary>Null when the key is unknown, the person does not exist or the viewer may see none of the system's categories.</summary>
  Task<SystemDto?> GetAsync(Guid viewerPersonId, Guid targetPersonId, string key, CancellationToken ct = default);
}

public interface IWeeklyReviewService
{
  /// <summary>Null when the person does not exist or the viewer may see none of their categories.</summary>
  Task<WeeklyReviewDto?> GetAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? weekOf, CancellationToken ct = default);
}
