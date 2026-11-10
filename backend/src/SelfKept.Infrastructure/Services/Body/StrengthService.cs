using System.Text.RegularExpressions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// The strength log (docs/body-design.md, "Strength"): one top set per lift per day. Enough to see
/// whether strength is holding while weight changes, without turning the app into a gym tracker.
/// </summary>
public sealed partial class StrengthService : IStrengthService
{
  public const int MaxLiftLength = 32;
  public const double MaxWeightKg = 1000;
  public const int MaxReps = 100;
  public const int DefaultWindowDays = 120;
  private const int MaxAgeYears = 5;

  private readonly IStrengthRepository _strength;
  private readonly IPersonRepository _persons;
  private readonly IAuditService _audit;
  private readonly ITodayCacheRepository _todayCache;
  private readonly TimeProvider _clock;

  public StrengthService(
    IStrengthRepository strength,
    IPersonRepository persons,
    IAuditService audit,
    ITodayCacheRepository todayCache,
    TimeProvider clock)
  {
    _strength = strength;
    _persons = persons;
    _audit = audit;
    _todayCache = todayCache;
    _clock = clock;
  }

  public async Task<IReadOnlyList<StrengthLiftDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
  {
    var today = await TodayAsync(targetPersonId, ct);
    var last = to ?? today;
    var first = from ?? last.AddDays(-(DefaultWindowDays - 1));
    if (first > last) throw new DomainValidationException("from must not be after to.");

    var sets = await _strength.ListAsync(viewerPersonId, targetPersonId, first, last, ct);
    return sets == null ? null : Group(sets);
  }

  public async Task<StrengthSetDto> SaveAsync(Actor actor, SaveStrengthSetRequest request, CancellationToken ct = default)
  {
    var lift = NormaliseLift(request.Lift);
    if (!double.IsFinite(request.WeightKg) || request.WeightKg < 0 || request.WeightKg > MaxWeightKg)
    {
      throw new DomainValidationException($"weightKg must be between 0 and {MaxWeightKg:0}.");
    }
    if (request.Reps is < 1 or > MaxReps) throw new DomainValidationException($"reps must be between 1 and {MaxReps}.");
    if (request.Rpe is { } rpe && (!double.IsFinite(rpe) || rpe < 1 || rpe > 10)) throw new DomainValidationException("rpe must be between 1 and 10.");

    var today = await TodayAsync(actor.PersonId, ct);
    var day = request.Day ?? today;
    if (day > today) throw new DomainValidationException("day cannot be in the future.");
    if (day < today.AddYears(-MaxAgeYears)) throw new DomainValidationException($"day cannot be more than {MaxAgeYears} years ago.");

    // One top set per lift per day: saving again replaces it.
    var set = await _strength.FindOwnAsync(actor.PersonId, day, lift, ct);
    if (set == null)
    {
      set = new StrengthSet
      {
        Id = Guid.CreateVersion7(),
        PersonId = actor.PersonId,
        Day = day,
        Lift = lift,
        CreatedAt = _clock.GetUtcNow().UtcDateTime,
      };
      Apply(set, request);
      await _strength.AddAsync(set, ct);
    }
    else
    {
      Apply(set, request);
      await _strength.UpdateAsync(set, ct);
    }
    await _todayCache.InvalidateAsync(actor.PersonId, ct);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.StrengthSaved, actor.UserId, actor.HouseholdId, "strength_set", set.Id.ToString(),
      new { lift }, actor.Info.IpAddress), ct);
    return set.ToDto();
  }

  public async Task<bool> DeleteAsync(Actor actor, Guid setId, CancellationToken ct = default)
  {
    var set = await _strength.GetOwnAsync(actor.PersonId, setId, ct);
    if (set == null) return false;
    await _strength.RemoveAsync(set, ct);
    await _todayCache.InvalidateAsync(actor.PersonId, ct);
    await _audit.RecordAsync(new AuditEvent(
      AuditActions.StrengthDeleted, actor.UserId, actor.HouseholdId, "strength_set", set.Id.ToString(),
      new { lift = set.Lift }, actor.Info.IpAddress), ct);
    return true;
  }

  /// <summary>
  /// Sets grouped per lift, the common lifts first. The change compares the estimated one-rep
  /// max of the first and the last set in the window that have one (twelve reps or fewer).
  /// </summary>
  public static IReadOnlyList<StrengthLiftDto> Group(IReadOnlyList<StrengthSet> sets) =>
    sets
      .GroupBy(s => s.Lift, StringComparer.Ordinal)
      .Select(g =>
      {
        var ordered = g.OrderBy(s => s.Day).Select(s => s.ToDto()).ToList();
        var estimates = ordered.Where(s => s.EstimatedMaxKg != null).Select(s => s.EstimatedMaxKg!.Value).ToList();
        double? change = estimates.Count >= 2 && estimates[0] > 0 ? (estimates[^1] - estimates[0]) / estimates[0] * 100 : null;
        return new StrengthLiftDto(g.Key, Lifts.DisplayName(g.Key), ordered, estimates.Count > 0 ? estimates.Max() : null, change);
      })
      .OrderBy(l => Array.IndexOf(Lifts.Common, l.Lift) is var i && i < 0 ? int.MaxValue : i)
      .ThenBy(l => l.Lift, StringComparer.Ordinal)
      .ToList();

  public static string NormaliseLift(string? lift)
  {
    var cleaned = (lift ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
    if (cleaned.Length is 0 or > MaxLiftLength || !LiftShape().IsMatch(cleaned))
    {
      throw new DomainValidationException($"lift is 1 to {MaxLiftLength} letters, digits or underscores.");
    }
    return cleaned;
  }

  private static void Apply(StrengthSet set, SaveStrengthSetRequest request)
  {
    set.WeightKg = request.WeightKg;
    set.Reps = (short)request.Reps;
    set.Rpe = request.Rpe;
  }

  private async Task<DateOnly> TodayAsync(Guid personId, CancellationToken ct)
  {
    var person = await _persons.GetByIdAsync(personId, ct);
    return DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person?.TimeZone));
  }

  [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.CultureInvariant)]
  private static partial Regex LiftShape();
}
