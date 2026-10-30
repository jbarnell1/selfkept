using SelfKept.Core.DTOs;

namespace SelfKept.Core.Interfaces;

public interface ISourceService
{
  Task<IReadOnlyList<SourceDto>> ListAsync(Guid personId, CancellationToken ct = default);

  /// <summary>Returns the existing source for this (kind, deviceName, platform) or creates one.</summary>
  Task<SourceDto> UpsertAsync(Guid userId, Guid personId, Guid? householdId, CreateSourceRequest request, RequestInfo info, CancellationToken ct = default);

  /// <summary>Null when the source is not the caller's.</summary>
  Task<SourceDto?> UpdateAsync(Guid personId, Guid sourceId, UpdateSourceRequest request, CancellationToken ct = default);

  /// <summary>Soft delete. False when the source is not the caller's.</summary>
  Task<bool> DeleteAsync(Guid userId, Guid personId, Guid? householdId, Guid sourceId, RequestInfo info, CancellationToken ct = default);
}

public interface IIngestService
{
  /// <summary>Null when the source is not the caller's. Throws DomainValidationException over the batch cap.</summary>
  Task<IngestBatchResponse?> IngestAsync(Guid personId, IngestBatchRequest request, CancellationToken ct = default);
}

public interface ICheckinService
{
  Task<CheckinDto> CreateAsync(Guid userId, Guid personId, Guid? householdId, CreateCheckinRequest request, RequestInfo info, CancellationToken ct = default);

  /// <summary>Null when the viewer may not see the target's check-ins.</summary>
  Task<IReadOnlyList<CheckinDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

  /// <summary>False when the check-in is not the caller's.</summary>
  Task<bool> DeleteAsync(Guid personId, Guid checkinId, CancellationToken ct = default);
}

public interface IDataService
{
  Task<DailySeriesDto?> GetDailyAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, DateOnly? from, DateOnly? to, CancellationToken ct = default);
  Task<SamplePageDto?> GetSamplesAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, DateTime? fromUtc, DateTime? toUtc, int? limit, string? cursor, CancellationToken ct = default);
  Task<OverviewDto?> GetOverviewAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);
}

/// <summary>Recomputes daily_summary rows for the (metric, day) pairs a write touched.</summary>
public interface IDailySummaryService
{
  Task RecomputeAsync(Guid personId, string timeZone, IReadOnlyCollection<(string MetricKey, DateOnly Day)> touched, CancellationToken ct = default);
}
