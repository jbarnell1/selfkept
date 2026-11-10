using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SelfKept.Core.DTOs;

// Wire shapes from docs/labs-design.md ("Endpoints"). Property names serialize camelCase.

/// <summary>The OCR lines from the phone, or pasted text on the web. One line per row of the results screen.</summary>
public sealed record ParseRequest([MaxLength(20000)] string? Text);

public sealed record LabCandidateDto(
  string MetricKey,
  string DisplayName,
  double Value,
  string? Unit,
  string? Flag,
  string RawText,
  double Confidence,
  int Line);

public sealed record ParseResultDto(IReadOnlyList<LabCandidateDto> Candidates, IReadOnlyList<string> UnmatchedLines);

public sealed record LabResultInput(
  [Required] string MetricKey,
  double Value,
  [MaxLength(32)] string? Unit,
  [MaxLength(16)] string? Flag,
  [MaxLength(200)] string? RawText,
  double? ReferenceLow,
  double? ReferenceHigh);

public sealed record CreateLabReportRequest(
  [Required, MaxLength(16)] string Provider,
  [Required] DateOnly? CollectedOn,
  DateOnly? ReceivedOn,
  [Required] IReadOnlyList<LabResultInput>? Results,
  /// <summary>The source photo as base64 (JPEG or PNG), encrypted at rest. Optional.</summary>
  string? Image,
  [MaxLength(2000)] string? Notes,
  /// <summary>How the blood was drawn. Optional; a Rythm panel without it is pre-filled as a morning draw after a fast of 8 or more hours.</summary>
  DrawConditionsInput? Conditions = null);

/// <summary>
/// The draw conditions asked when a panel is confirmed (docs/labs-design.md, "Draw conditions").
/// Every field is optional; a missing field means "not known". drawTime is local "HH:mm"; when
/// it is given, drawPeriod is worked out from it.
/// </summary>
public sealed record DrawConditionsInput(
  [MaxLength(8)] string? Fasting,
  [Range(0, 72)] int? FastingHours,
  [MaxLength(8)] string? DrawTime,
  [MaxLength(16)] string? DrawPeriod,
  bool? HardTraining,
  bool? Unwell);

/// <summary>Conditions as stored. fasting and drawPeriod are always present ("unknown" when never answered).</summary>
public sealed record DrawConditionsDto(
  string Fasting,
  int? FastingHours,
  string? DrawTime,
  string DrawPeriod,
  bool? HardTraining,
  bool? Unwell,
  /// <summary>True while these are the provider's pre-filled defaults (Rythm) and the person has not changed them.</summary>
  bool Defaulted);

public sealed record LabResultDto(
  Guid Id,
  string MetricKey,
  string DisplayName,
  double Value,
  string Unit,
  string Flag,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RawText,
  bool Derived,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? ReferenceLow,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? ReferenceHigh);

public sealed record LabReportDto(
  Guid Id,
  Guid PersonId,
  string Provider,
  DateOnly CollectedOn,
  DateOnly? ReceivedOn,
  bool HasImage,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes,
  DateTime CreatedAt,
  DateTime? ConfirmedAt,
  IReadOnlyList<LabResultDto> Results,
  DrawConditionsDto Conditions);

public sealed record LabRangeDto(double? Low, double? High);

public sealed record LabPointDto(DateOnly Date, double Value, string Flag, bool Derived, Guid ReportId);

/// <summary>One tile of the Labs screen: the marker, where it stands, and the ranges to draw behind it.</summary>
public sealed record LabMarkerDto(
  string MetricKey,
  string DisplayName,
  string Unit,
  string Group,
  string SexApplicability,
  bool Applicable,
  string GoodDirection,
  double? LatestValue,
  DateOnly? LatestDate,
  string? LatestFlag,
  double? PreviousValue,
  DateOnly? PreviousDate,
  LabRangeDto? PopulationRange,
  LabRangeDto? OptimalRange,
  string? Tier,
  /// <summary>Where the latest value sits against the population range: below, inside, above; null without a range or a value.</summary>
  string? Position,
  /// <summary>How the latest value is flagged: null inside the range or without one, else watch or clinician (the row's tier narrowed by the clinician triggers).</summary>
  string? Severity,
  IReadOnlyList<LabPointDto> LastThree);

public sealed record LabMarkerDetailDto(
  LabMarkerDto Marker,
  string Blurb,
  string WhatMovesIt,
  string? SourceUrl,
  IReadOnlyList<LabPointDto> History);
