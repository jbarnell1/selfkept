using System.ComponentModel.DataAnnotations;

namespace SelfKept.Core.DTOs;

// Wire shapes from docs/body-design.md ("API"). Property names serialize camelCase.

public sealed record GoalDto(
  Guid Id,
  string Slot,
  string Kind,
  /// <summary>The person's own words when they gave some, else the kind's display name.</summary>
  string Title,
  string? MetricKey,
  double? StartValue,
  double? TargetValue,
  double? PacePerWeek,
  DateOnly StartedOn,
  DateOnly? TargetDate,
  string Status,
  DateTime? EndedAt);

/// <summary>Creating a goal in a slot ends the active goal already there. StartedOn defaults to today.</summary>
public sealed record SaveGoalRequest(
  [Required] string Slot,
  [Required] string Kind,
  [MaxLength(80)] string? Title,
  string? MetricKey,
  double? StartValue,
  double? TargetValue,
  double? PacePerWeek,
  DateOnly? StartedOn,
  DateOnly? TargetDate);

/// <summary>done or dropped.</summary>
public sealed record EndGoalRequest([Required] string Status);

public sealed record TargetDto(
  Guid Id,
  Guid? GoalId,
  string Label,
  string Kind,
  string? MetricKey,
  string? Tag,
  string Comparator,
  double Value,
  string Period,
  bool Active,
  int SortOrder,
  /// <summary>The unit the value is in: the metric's unit, "entries" for the day log, "days" for a week of sessions.</summary>
  string Unit);

public sealed record SaveTargetRequest(
  Guid? GoalId,
  [Required, MinLength(1), MaxLength(48)] string Label,
  [Required] string Kind,
  string? MetricKey,
  string? Tag,
  [Required] string Comparator,
  double Value,
  [Required] string Period,
  bool? Active,
  int? SortOrder);

/// <summary>
/// One target with where it stands. Met is null while it is still open (the day is not over, or
/// nothing was logged, so nobody can say). Week is the last seven days oldest first, today last.
/// </summary>
public sealed record TargetTallyDto(TargetDto Target, double? Current, bool? Met, IReadOnlyList<bool?> Week, int MetDays, int KnownDays);

public sealed record GoalTallyDto(DateOnly Day, IReadOnlyList<GoalDto> Goals, IReadOnlyList<TargetTallyDto> Targets);

public sealed record JournalEntryDto(
  Guid Id,
  DateTime At,
  DateOnly Day,
  string Kind,
  string? Text,
  IReadOnlyList<string> Tags,
  string? Portion,
  string Source,
  Guid? MessageId);

public sealed record CreateJournalEntryRequest(
  [Required] string Kind,
  [MaxLength(500)] string? Text,
  IReadOnlyList<string>? Tags,
  string? Portion,
  DateTime? At);

public sealed record JournalTagGroupDto(string Kind, IReadOnlyList<JournalTagDto> Tags);

public sealed record JournalTagDto(string Tag, string Label);

public sealed record MedicationIngredientDto([Required, MaxLength(80)] string Name, double? Amount, [MaxLength(16)] string? Unit);

public sealed record MedicationDto(
  Guid Id,
  string Kind,
  string? Brand,
  string Product,
  string? Dose,
  string? ScheduleNote,
  bool? Generic,
  string Schedule,
  IReadOnlyList<MedicationIngredientDto> Ingredients,
  IReadOnlyList<string> Substances,
  DateOnly? StartedOn,
  DateOnly? StoppedOn,
  bool Active);

public sealed record SaveMedicationRequest(
  [Required] string Kind,
  [MaxLength(80)] string? Brand,
  [Required, MinLength(1), MaxLength(120)] string Product,
  [MaxLength(80)] string? Dose,
  [MaxLength(120)] string? ScheduleNote,
  bool? Generic,
  string? Schedule,
  IReadOnlyList<MedicationIngredientDto>? Ingredients,
  DateOnly? StartedOn,
  DateOnly? StoppedOn);

public sealed record StrengthSetDto(
  Guid Id,
  DateOnly Day,
  string Lift,
  string LiftName,
  double WeightKg,
  int Reps,
  double? Rpe,
  /// <summary>An estimate from weight and reps; null above twelve reps, where the estimate stops being useful.</summary>
  double? EstimatedMaxKg);

public sealed record SaveStrengthSetRequest([Required, MaxLength(32)] string Lift, double WeightKg, int Reps, double? Rpe, DateOnly? Day);

/// <summary>One lift over time, oldest set first, with the change in estimated max across the window.</summary>
public sealed record StrengthLiftDto(string Lift, string LiftName, IReadOnlyList<StrengthSetDto> Sets, double? BestEstimatedMaxKg, double? ChangePct);

public sealed record CreateMeasurementRequest([Required] string MetricKey, double Value, DateTime? At);

public sealed record MeasurementDto(Guid Id, string MetricKey, double Value, string Unit, DateTime At);

public sealed record BodyPointDto(DateOnly Day, double Value);

/// <summary>A body series for the Progress charts. Trend is the smoothed line and is only present for weight.</summary>
public sealed record BodySeriesDto(
  string Key,
  string DisplayName,
  string Unit,
  IReadOnlyList<BodyPointDto> Points,
  IReadOnlyList<BodyPointDto>? Trend,
  double? Latest,
  double? Change);

/// <summary>
/// Where the body stands today, worked out on read. Every value is null until its inputs exist
/// (height for BMI, a body fat reading for fat and lean mass, a waist for waist to height).
/// </summary>
public sealed record BodySnapshotDto(
  double? WeightKg,
  double? TrendWeightKg,
  double? WeeklyRateKg,
  double? WeeklyRatePct,
  double? BodyFatPct,
  double? FatMassKg,
  double? LeanMassKg,
  double? Bmi,
  double? LeanMassIndex,
  double? WaistCm,
  double? WaistToHeight,
  double? LeanShareOfLoss,
  double? HeightCm,
  int ReadingsSetAside,
  DateOnly? LastWeighedOn);

public static class PaceStatuses
{
  public const string OnTrack = "on_track";
  public const string Ahead = "ahead";
  public const string Behind = "behind";
  public const string WrongWay = "wrong_way";
  public const string Reached = "reached";
  public const string Unknown = "unknown";
}

public sealed record GoalProgressDto(
  GoalDto Goal,
  string? Unit,
  double? Current,
  /// <summary>0 to 1 of the way from the start value to the target, when both are set.</summary>
  double? Fraction,
  double? ActualPacePerWeek,
  string PaceStatus,
  DateOnly? ProjectedDate,
  string Summary);

public sealed record SuggestedTargetDto(string Label, string Kind, string? MetricKey, string? Tag, string Comparator, double Value, string Period, string Unit);

/// <summary>A goal a person could pick, pre-filled from their own numbers. Why says where each figure came from.</summary>
public sealed record GoalSuggestionDto(
  string Kind,
  string Title,
  string Why,
  string? MetricKey,
  double? StartValue,
  IReadOnlyList<SuggestedTargetDto> Targets);

public sealed record ProgressDto(
  DateOnly Day,
  IReadOnlyList<GoalProgressDto> Goals,
  IReadOnlyList<TargetTallyDto> Targets,
  BodySnapshotDto Body,
  IReadOnlyList<BodySeriesDto> Series,
  IReadOnlyList<StrengthLiftDto> Strength,
  IReadOnlyList<GoalSuggestionDto> Suggestions);
