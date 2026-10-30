using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Labs;

public static class LabMapping
{
  public static LabReportDto ToDto(this LabReport r, IMetricCatalogue catalogue, string? notes) =>
    new(r.Id, r.PersonId, r.Provider, r.CollectedOn, r.ReceivedOn, r.SourceImageCipher != null, notes, r.CreatedAt, r.ConfirmedAt,
      r.Results.OrderBy(x => LabPanel.OrderOf(x.MetricKey)).Select(x => x.ToDto(catalogue)).ToList());

  public static LabResultDto ToDto(this LabResult x, IMetricCatalogue catalogue) =>
    new(x.Id, x.MetricKey, catalogue.Find(x.MetricKey)?.DisplayName ?? x.MetricKey, x.Value, x.Unit, x.Flag, x.RawText, x.Derived, x.ReferenceLow, x.ReferenceHigh);

  public static LabPointDto ToPoint(this LabResult x, DateOnly collectedOn) => new(collectedOn, x.Value, x.Flag, x.Derived, x.ReportId);

  public static LabRangeDto? PopulationRange(LabReference? reference) =>
    reference is { HasRange: true } ? new LabRangeDto(reference.Low, reference.High) : null;

  public static LabRangeDto? OptimalRange(LabReference? reference) =>
    reference != null && (reference.OptimalLow != null || reference.OptimalHigh != null) ? new LabRangeDto(reference.OptimalLow, reference.OptimalHigh) : null;
}
