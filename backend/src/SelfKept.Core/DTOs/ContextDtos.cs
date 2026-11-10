using System.ComponentModel.DataAnnotations;

namespace SelfKept.Core.DTOs;

// Wire shapes from docs/context-design.md ("API"). Property names serialize camelCase; days are
// yyyy-MM-dd in the target person's time zone; times are ISO 8601 UTC.

// --- context ---

public sealed record ContextEventDto(
  Guid Id,
  string Kind,
  string Label,
  DateOnly StartedOn,
  DateOnly? EndedOn,
  string Source,
  /// <summary>The person's own words. Only the person themself gets it back; null for anyone else.</summary>
  string? Note,
  /// <summary>Whether the event counts on the day the list was read for.</summary>
  bool Active);

/// <summary>StartedOn defaults to today in the caller's time zone.</summary>
public sealed record SaveContextEventRequest(
  [Required] string Kind,
  DateOnly? StartedOn,
  DateOnly? EndedOn,
  [MaxLength(500)] string? Note);

/// <summary>Every field optional: a missing field keeps the stored value. An empty note clears it; reopen clears endedOn.</summary>
public sealed record UpdateContextEventRequest(
  string? Kind,
  DateOnly? StartedOn,
  DateOnly? EndedOn,
  [MaxLength(500)] string? Note,
  bool? Reopen);

public sealed record ActiveContextDto(
  string Kind,
  string Label,
  DateOnly Since,
  DateOnly? Until,
  /// <summary>user, rule or derived.</summary>
  string Source,
  string Reason,
  Guid? EventId,
  string? Detail);

public sealed record ContextDto(
  DateOnly Day,
  DateOnly From,
  DateOnly To,
  IReadOnlyList<ActiveContextDto> Active,
  IReadOnlyList<ContextEventDto> Events);

// --- patterns ---

public sealed record PatternRangeDto(double Low, double High);

public sealed record PersonalPatternDto(
  string Tag,
  string TagLabel,
  string Metric,
  string MetricName,
  string Unit,
  int NightsWith,
  int NightsWithout,
  double MedianWith,
  double MedianWithout,
  double Difference,
  PatternRangeDto RangeWith,
  PatternRangeDto RangeWithout,
  double Overlap,
  double NoiseLine,
  int WeekendNights,
  int ContextNights,
  string Sentence);

public sealed record PatternBuildingDto(string Tag, string TagLabel, int NightsWith, int NightsWithout);

public sealed record PatternsDto(
  DateOnly Day,
  DateOnly From,
  int WindowDays,
  int MinNightsWith,
  int MinNightsWithout,
  string Note,
  IReadOnlyList<PersonalPatternDto> Patterns,
  IReadOnlyList<PatternBuildingDto> Building);

// --- read-backs ---

public sealed record SleepNightDto(
  DateOnly Day,
  DateTime Bedtime,
  DateTime WakeTime,
  double InBedMinutes,
  double AsleepMinutes,
  double? AwakeMinutes,
  double? LightMinutes,
  double? DeepMinutes,
  double? RemMinutes,
  double? UnspecifiedMinutes,
  bool HasStages);

public sealed record SleepNightsDto(DateOnly From, DateOnly To, IReadOnlyList<SleepNightDto> Nights);

public sealed record WorkoutReadDto(
  Guid Id,
  string Type,
  /// <summary>strength, cardio or mixed.</summary>
  string Group,
  DateOnly Day,
  DateTime StartAt,
  DateTime EndAt,
  double DurationMinutes,
  double? DistanceM,
  double? EnergyKcal,
  double? AvgHr,
  double? MaxHr);

public sealed record WorkoutWeekDto(DateOnly WeekOf, int Count, double Minutes, int Strength, int Cardio, int Mixed);

public sealed record WorkoutsDto(DateOnly From, DateOnly To, IReadOnlyList<WorkoutReadDto> Workouts, IReadOnlyList<WorkoutWeekDto> Weeks);

// --- systems ---

public sealed record SystemUsualDto(double Median28, double P25, double P75, int Days);

public sealed record SystemReadingDto(
  string Metric,
  string DisplayName,
  string Unit,
  string Category,
  DateOnly? LatestDay,
  double? LatestValue,
  SystemUsualDto? Usual,
  /// <summary>below, inside or above the usual range; null until there is a range.</summary>
  string? Band,
  /// <summary>The last 28 days; only on GET /api/systems/{key}, null on the list.</summary>
  IReadOnlyList<TrendPointDto>? Recent);

public sealed record SystemLabPointDto(DateOnly CollectedOn, double Value, string Flag);

public sealed record SystemLabDto(
  string Metric,
  string DisplayName,
  string Unit,
  double Value,
  DateOnly CollectedOn,
  string Flag,
  double? PreviousValue,
  DateOnly? PreviousOn,
  /// <summary>Up to the last six values, oldest first; only on GET /api/systems/{key}, null on the list.</summary>
  IReadOnlyList<SystemLabPointDto>? History);

public sealed record SystemDto(
  string Key,
  string Name,
  string State,
  IReadOnlyList<SystemReadingDto> Readings,
  IReadOnlyList<SystemLabDto> Labs,
  IReadOnlyList<InsightDto> Insights);

public sealed record LabsEntryDto(DateOnly? LatestPanelOn, int Panels, int Markers, int OutsideRange);

public sealed record SystemsDto(DateOnly Day, IReadOnlyList<SystemDto> Systems, LabsEntryDto? Labs);

// --- weekly review ---

public sealed record ReviewTargetDto(Guid TargetId, string Label, string Period, int MetDays, int KnownDays, bool? Met);

public sealed record ReviewTargetsDto(IReadOnlyList<ReviewTargetDto> Kept, IReadOnlyList<ReviewTargetDto> Missed, IReadOnlyList<ReviewTargetDto> Unknown);

public sealed record ReviewGoalDto(
  Guid GoalId,
  string Slot,
  string Kind,
  string Title,
  string? Metric,
  string? Unit,
  double? First,
  double? Last,
  double? Change,
  int Readings,
  string Summary);

public sealed record ReviewSleepDto(
  int Nights,
  double? MedianMinutes,
  double? PreviousMedianMinutes,
  int NightsSevenHoursOrMore,
  string Summary);

public sealed record ReviewStandoutDto(
  string Metric,
  string DisplayName,
  string Unit,
  double WeekMedian,
  double UsualMedian,
  double Difference,
  /// <summary>up or down.</summary>
  string Direction,
  string Summary);

public sealed record WeeklyReviewDto(
  DateOnly WeekOf,
  DateOnly WeekEnd,
  bool Complete,
  ReviewTargetsDto? Targets,
  IReadOnlyList<ReviewGoalDto>? Goals,
  ReviewSleepDto? Sleep,
  IReadOnlyList<ReviewStandoutDto>? Standouts,
  IReadOnlyList<InsightDto>? Insights,
  IReadOnlyList<PersonalPatternDto>? Patterns);
