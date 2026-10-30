using SelfKept.Core.Entities;
using SelfKept.Core.Enums;

namespace SelfKept.Core.Interfaces;

/// <summary>
/// Lab reports with their results. Viewer reads take (viewer, target) and check the labs share
/// category; "own" methods are the caller's own rows only. Null means not for you (404).
/// </summary>
public interface ILabReportRepository
{
  /// <summary>Adds the report and every result on it.</summary>
  Task<LabReport> AddAsync(LabReport report, CancellationToken ct = default);

  /// <summary>The person's own report with results; null when it is someone else's or missing.</summary>
  Task<LabReport?> GetOwnAsync(Guid personId, Guid reportId, CancellationToken ct = default);

  /// <summary>
  /// One report with results. The target is the report's owner: null when the report does not
  /// exist or the viewer may not see that person's labs.
  /// </summary>
  Task<LabReport?> GetAsync(Guid viewerPersonId, Guid reportId, CancellationToken ct = default);

  /// <summary>Newest collected_on first, results included. Null when the viewer may not see the target's labs.</summary>
  Task<IReadOnlyList<LabReport>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);

  /// <summary>Server-internal: the person's reports newest first, results included, for the rules. Not a viewer read.</summary>
  Task<IReadOnlyList<LabReport>> ListOwnAsync(Guid personId, int limit, CancellationToken ct = default);

  /// <summary>Removes the report and its results.</summary>
  Task RemoveAsync(LabReport report, CancellationToken ct = default);
}

/// <summary>
/// The population reference table, loaded once and held in memory. Lookups resolve the row for a
/// person's sex: a "both" row applies to anyone, a sex-specific row to that sex, and a marker the
/// catalogue marks female-only or male-only has no range for the other sex.
/// </summary>
public interface ILabReferenceRepository
{
  IReadOnlyList<LabReference> All { get; }

  /// <summary>Every row for the marker, any sex.</summary>
  IReadOnlyList<LabReference> RowsFor(string metricKey);

  /// <summary>The row that applies to this person, or null when none does (unknown marker, other sex, no range).</summary>
  LabReference? Find(Metric metric, SexAtBirth sex);

  Task ReloadAsync(CancellationToken ct = default);
}

/// <summary>The alias table, loaded once and held in memory for the parser.</summary>
public interface IMarkerAliasRepository
{
  IReadOnlyList<LabMarkerAlias> All { get; }
  Task ReloadAsync(CancellationToken ct = default);
}
