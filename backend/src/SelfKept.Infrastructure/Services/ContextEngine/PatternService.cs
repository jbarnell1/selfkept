using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;
using SelfKept.Infrastructure.Services.Interpret;

namespace SelfKept.Infrastructure.Services.ContextEngine;

/// <summary>
/// Personal patterns on read (docs/context-design.md, "Patterns"). Worked out on every request
/// from one read of the day log tags and the overnight figures: two queries over 120 days, cheap
/// enough that nothing is cached. An insights product, so one insights grant covers it, like Today.
/// </summary>
public sealed class PatternService : IPatternService
{
  private readonly IReadBackRepository _readBack;
  private readonly IContextEventRepository _events;
  private readonly IPersonRepository _persons;
  private readonly TimeProvider _clock;

  public PatternService(IReadBackRepository readBack, IContextEventRepository events, IPersonRepository persons, TimeProvider clock)
  {
    _readBack = readBack;
    _events = events;
    _persons = persons;
    _clock = clock;
  }

  public async Task<PatternsDto?> GetAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    var person = await _persons.GetByIdAsync(targetPersonId, ct);
    if (person == null) return null;
    var today = DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, DayBoundaries.Resolve(person.TimeZone));
    return await GetAsOfAsync(viewerPersonId, targetPersonId, today, ct);
  }

  public async Task<PatternsDto?> GetAsOfAsync(Guid viewerPersonId, Guid targetPersonId, DateOnly day, CancellationToken ct = default)
  {
    var from = day.AddDays(-PatternMath.WindowDays);
    var inputs = await _readBack.GetPatternInputsAsync(viewerPersonId, targetPersonId, PatternMath.Metrics, from, day, ct);
    if (inputs == null) return null;
    var events = await _events.ListAsync(viewerPersonId, targetPersonId, from, day, ct) ?? [];
    var stats = PatternMath.Compute(inputs.Days, PatternMath.NightsFrom(inputs.Nights.Where(n => n.Day <= day)), day, ContextLoader.ContextDays(events, inputs.Days));
    return Build(day, from, stats);
  }

  public static PatternsDto Build(DateOnly day, DateOnly from, IReadOnlyList<PatternStats> stats)
  {
    var patterns = stats
      .Where(p => p.Established)
      .OrderByDescending(p => p.NoiseLine > 0 ? Math.Abs(p.Difference) / p.NoiseLine : Math.Abs(p.Difference))
      .Select(ToDto)
      .ToList();
    var building = stats
      .GroupBy(p => p.Tag, StringComparer.Ordinal)
      .Select(g => new PatternBuildingDto(g.Key, JournalTags.DisplayName(g.Key), g.Max(p => p.NightsWith), g.Max(p => p.NightsWithout)))
      .Where(b => b.NightsWith < PatternMath.MinNightsWith || b.NightsWithout < PatternMath.MinNightsWithout)
      .OrderByDescending(b => b.NightsWith)
      .ThenBy(b => b.Tag, StringComparer.Ordinal)
      .ToList();
    return new PatternsDto(day, from, PatternMath.WindowDays, PatternMath.MinNightsWith, PatternMath.MinNightsWithout, PatternMath.Note, patterns, building);
  }

  public static PersonalPatternDto ToDto(PatternStats p) => new(
    p.Tag,
    JournalTags.DisplayName(p.Tag),
    p.MetricKey,
    MetricNames.For(p.MetricKey),
    PatternMath.Unit(p.MetricKey),
    p.NightsWith,
    p.NightsWithout,
    p.MedianWith,
    p.MedianWithout,
    p.Difference,
    new PatternRangeDto(p.WithLow, p.WithHigh),
    new PatternRangeDto(p.WithoutLow, p.WithoutHigh),
    p.Overlap,
    p.NoiseLine,
    p.WeekendNights,
    p.ContextNights,
    PatternMath.Sentence(p));
}
