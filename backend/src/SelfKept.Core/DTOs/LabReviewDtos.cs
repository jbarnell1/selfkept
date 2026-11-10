using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SelfKept.Core.DTOs;

// Wire shapes for the lab review, timeline, trials, links and the household overview
// (docs/labs-design.md, "API"). Property names serialize camelCase.

/// <summary>One medicine or supplement as the review lists it: what it is and its substance tags.</summary>
public sealed record TakenItemDto(Guid Id, string Kind, string? Brand, string Product, IReadOnlyList<string> Substances);

public sealed record WeightAtDrawDto(DateOnly Day, double Kg);

/// <summary>A draw condition that differs between the two panels. now and then are the stored answers.</summary>
public sealed record ConditionDifferenceDto(string Condition, string? Now, string? Then);

/// <summary>A link from a draw condition or a substance that applies to a marker on this draw.</summary>
public sealed record ReadWithCareDto(string LinkId, string From, string Explanation, string Source);

/// <summary>Where the variation figure behind a verdict comes from.</summary>
public sealed record VariationSourceDto(double CviPercent, string Source, string SourceUrl, string? Caveat);

public sealed record LabReviewMarkerDto(
  string MetricKey,
  string DisplayName,
  string Unit,
  double Value,
  double? PreviousValue,
  double? Change,
  double? ChangePercent,
  /// <summary>Reference change value in percent: how far the marker must move before the change is probably real.</summary>
  double? RcvPercent,
  bool? BeyondRcv,
  /// <summary>up, down or none (the way it moved).</summary>
  string? Moved,
  /// <summary>up, down or none (the way that would be better).</summary>
  string Wanted,
  /// <summary>working, right_direction, no_clear_change, not_working or cannot_tell.</summary>
  string Verdict,
  IReadOnlyList<string> Reasons,
  IReadOnlyList<ReadWithCareDto> ReadWithCare,
  VariationSourceDto? Variation,
  IReadOnlyList<string> Prep,
  string? Retest);

/// <summary>A trial whose window runs between the two panels, with the verdict of each linked marker.</summary>
public sealed record LabReviewTrialDto(TrialDto Trial, IReadOnlyList<LabReviewTrialMarkerDto> Markers, bool SharedCredit);

public sealed record LabReviewTrialMarkerDto(string MetricKey, string Verdict);

public sealed record LabReviewDto(
  Guid ReportId,
  Guid PersonId,
  DateOnly CollectedOn,
  Guid? PreviousReportId,
  DateOnly? PreviousCollectedOn,
  DrawConditionsDto Conditions,
  DrawConditionsDto? PreviousConditions,
  IReadOnlyList<ConditionDifferenceDto> ConditionsDiffer,
  IReadOnlyList<LabReviewMarkerDto> Markers,
  IReadOnlyList<TakenItemDto> TakingNow,
  IReadOnlyList<TakenItemDto> TakingThen,
  IReadOnlyList<string> SubstancesNow,
  IReadOnlyList<string> SubstancesThen,
  /// <summary>Absent without the body grant; present with nulls inside when no weigh-in fell within 7 days of a draw.</summary>
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LabReviewWeightDto? Weight,
  /// <summary>Absent without the body grant.</summary>
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<LabReviewTrialDto>? Trials,
  /// <summary>How many markers got each verdict, for the summary line.</summary>
  IReadOnlyDictionary<string, int> VerdictCounts);

public sealed record LabReviewWeightDto(WeightAtDrawDto? Now, WeightAtDrawDto? Then, double? ChangeKg);

public sealed record LabTimelinePanelDto(
  Guid ReportId,
  DateOnly CollectedOn,
  string Provider,
  DrawConditionsDto Conditions,
  int MarkerCount,
  /// <summary>Null without the body grant (see weightShared) or when no weigh-in fell within 7 days.</summary>
  WeightAtDrawDto? Weight,
  IReadOnlyList<string> Substances);

public sealed record LabTimelineDto(Guid PersonId, bool WeightShared, IReadOnlyList<LabTimelinePanelDto> Panels);

// --- trials ---

public sealed record TrialDto(
  Guid Id,
  string Title,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Change,
  DateOnly StartedOn,
  DateOnly ReadOn,
  IReadOnlyList<string> MetricKeys,
  string Status,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Outcome,
  DateTime CreatedAt,
  DateTime UpdatedAt,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? EndedAt);

public sealed record CreateTrialRequest(
  [Required, MaxLength(120)] string? Title,
  [MaxLength(400)] string? Change,
  DateOnly? StartedOn,
  [Required] DateOnly? ReadOn,
  IReadOnlyList<string>? MetricKeys);

/// <summary>Every field optional: only what is sent changes. status done or dropped ends the trial; active reopens it.</summary>
public sealed record UpdateTrialRequest(
  [MaxLength(120)] string? Title,
  [MaxLength(400)] string? Change,
  DateOnly? StartedOn,
  DateOnly? ReadOn,
  IReadOnlyList<string>? MetricKeys,
  [MaxLength(16)] string? Status,
  [MaxLength(1000)] string? Outcome);

// --- links ---

public sealed record LabLinkDto(
  string Id,
  string From,
  string To,
  string Direction,
  string Explanation,
  string Source,
  string SourceUrl,
  string Evidence);

/// <summary>A reading related to a marker, with the person's latest value when the viewer may see that reading's category.</summary>
public sealed record RelatedReadingDto(
  string Key,
  string Kind,
  string DisplayName,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Unit,
  string Category,
  bool Shared,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? LatestValue,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? LatestDate,
  IReadOnlyList<LabLinkDto> Links);

public sealed record RelatedReadingsDto(string MetricKey, string DisplayName, IReadOnlyList<RelatedReadingDto> Related);

// --- household overview ---

public sealed record OverviewLabsDto(DateOnly? LatestCollectedOn, int Panels, int OutOfRange);

public sealed record OverviewBodyDto(double? WeightKg, DateOnly? WeighedOn, string? MainGoal, int ActiveTrials);

/// <summary>The latest daily value of one metric in the last 14 days; day and value are null when there is none.</summary>
public sealed record OverviewValueDto(string Metric, DateOnly? Day, double? Value, string Unit);

public sealed record OverviewInsightsDto(int Cards, string? TopSeverity);

public sealed record OverviewCheckinsDto(DateTime? LastAt, int? Mood);

/// <summary>
/// One household member as the caller may see them. A category the caller has no grant for is
/// absent from the JSON, not empty; a granted category with nothing in it is present with nulls or zeros.
/// </summary>
public sealed record HouseholdMemberOverviewDto(
  Guid PersonId,
  string DisplayName,
  bool IsSelf,
  IReadOnlyList<string> SharedCategories,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OverviewLabsDto? Labs,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OverviewBodyDto? Body,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OverviewValueDto? Sleep,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OverviewValueDto? Activity,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OverviewValueDto? Vitals,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OverviewInsightsDto? Insights,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OverviewCheckinsDto? Checkins);

public sealed record HouseholdOverviewDto(Guid HouseholdId, DateOnly Day, IReadOnlyList<HouseholdMemberOverviewDto> Members);
