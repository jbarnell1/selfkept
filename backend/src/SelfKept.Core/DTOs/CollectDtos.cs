using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using SelfKept.Core.Enums;

namespace SelfKept.Core.DTOs;

// Wire shapes from docs/ingest-contract.md. Property names serialize camelCase.

public sealed record MetricDto(
  string Key,
  string Unit,
  string Kind,
  ShareCategory Category,
  string DisplayName,
  string Blurb,
  string GoodDirection,
  string SexApplicability,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? ValueLabels);

public sealed record SourceDto(
  Guid Id,
  string Kind,
  string DeviceName,
  string Platform,
  bool Enabled,
  DateTime? LastSyncAt,
  DateTime? LastSampleAt,
  long SampleCount,
  string? Cursor);

public sealed record CreateSourceRequest(
  [Required, MaxLength(16)] string Kind,
  [Required, MinLength(1), MaxLength(80)] string DeviceName,
  [Required, MinLength(1), MaxLength(32)] string Platform,
  [MaxLength(32)] string? AppVersion);

public sealed record UpdateSourceRequest(bool? Enabled, [MaxLength(80)] string? DeviceName);

public sealed record SampleInput(
  string? SourceUid,
  string? Metric,
  DateTime? Start,
  DateTime? End,
  double? Value,
  string? Unit,
  JsonElement? Meta);

public sealed record WorkoutInput(
  string? SourceUid,
  string? Type,
  DateTime? Start,
  DateTime? End,
  double? DistanceM,
  double? EnergyKcal,
  double? AvgHr,
  double? MaxHr,
  JsonElement? RouteGeoJson,
  JsonElement? Meta);

public sealed record IngestBatchRequest(
  [Required] Guid SourceId,
  [MaxLength(4096)] string? Cursor,
  IReadOnlyList<SampleInput>? Samples,
  IReadOnlyList<WorkoutInput>? Workouts);

public sealed record RejectedItemDto(string? SourceUid, string Reason);

public sealed record IngestBatchResponse(int Accepted, int Duplicates, IReadOnlyList<RejectedItemDto> Rejected, string? Cursor);

public sealed record CheckinDto(Guid Id, Guid PersonId, DateTime At, int Mood, string? Note, IReadOnlyList<string> Tags);

public sealed record CreateCheckinRequest(
  DateTime? At,
  [Range(1, 6)] int Mood,
  [MaxLength(2000)] string? Note,
  IReadOnlyList<string>? Tags);

public sealed record DailyPointDto(
  DateOnly Day,
  double Value,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Min,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Max,
  int Count);

public sealed record DailySeriesDto(string Metric, string Unit, IReadOnlyList<DailyPointDto> Days);

public sealed record SampleDto(
  Guid Id,
  string Metric,
  DateTime Start,
  DateTime? End,
  double Value,
  string Unit,
  Guid SourceId,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Meta);

public sealed record SamplePageDto(
  IReadOnlyList<SampleDto> Items,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);

public sealed record OverviewMetricDto(string Metric, string Unit, DateTime FirstAt, DateTime LastAt, long Count, double LastValue);

public sealed record OverviewDto(IReadOnlyList<OverviewMetricDto> Metrics);
