using SelfKept.Core.DTOs;

namespace SelfKept.Core.Interfaces;

public interface ILabService
{
  /// <summary>Matches OCR or pasted lines to catalogue markers. Nothing is stored.</summary>
  ParseResultDto Parse(string text);

  /// <summary>
  /// Stores a confirmed panel: validates every marker, converts units, derives the three ratios,
  /// encrypts the image, writes a lab.* sample per result and interprets today for the person.
  /// Throws DomainValidationException on bad input.
  /// </summary>
  Task<LabReportDto> CreateReportAsync(Guid userId, Guid personId, Guid? householdId, CreateLabReportRequest request, RequestInfo info, CancellationToken ct = default);

  /// <summary>Newest first. Null when the viewer may not see the target's labs.</summary>
  Task<IReadOnlyList<LabReportDto>?> ListReportsAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);

  /// <summary>Null when the report is missing or its owner has not shared labs with the viewer.</summary>
  Task<LabReportDto?> GetReportAsync(Guid viewerPersonId, Guid reportId, CancellationToken ct = default);

  /// <summary>Own reports only; removes the samples too. False when the report is not the caller's.</summary>
  Task<bool> DeleteReportAsync(Guid userId, Guid personId, Guid? householdId, Guid reportId, RequestInfo info, CancellationToken ct = default);

  /// <summary>One entry per lab marker in panel order. Null when the viewer may not see the target's labs.</summary>
  Task<IReadOnlyList<LabMarkerDto>?> GetMarkersAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);

  /// <summary>Full history for one marker. Null when the marker is unknown or labs are not shared.</summary>
  Task<LabMarkerDetailDto?> GetMarkerAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, CancellationToken ct = default);

  /// <summary>
  /// The clinician summary PDF. Labs follow the labs share; each trend row follows its own
  /// metric's category and is simply left out when not shared. Null when labs are not shared.
  /// </summary>
  Task<LabSummaryPdf?> ExportSummaryAsync(Guid userId, Guid viewerPersonId, Guid targetPersonId, Guid? householdId, DateOnly? from, DateOnly? to, RequestInfo info, CancellationToken ct = default);
}

public sealed record LabSummaryPdf(byte[] Content, string FileName, DateOnly From, DateOnly To);
