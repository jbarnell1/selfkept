using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;

namespace SelfKept.Core.Interfaces;

/// <summary>
/// Trials are read under the body grant (they belong with goals and targets). Viewer reads take
/// (viewer, target) and return null on a denial; "Own" methods are the owner's writes and the
/// server's own reads for the rules.
/// </summary>
public interface ITrialRepository
{
  /// <summary>Newest start first. Null when the viewer may not see the target's body data.</summary>
  Task<IReadOnlyList<Trial>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, bool includeEnded, CancellationToken ct = default);

  /// <summary>Server-internal: the person's active trials, oldest start first.</summary>
  Task<IReadOnlyList<Trial>> ListOwnActiveAsync(Guid personId, CancellationToken ct = default);

  Task<Trial?> GetOwnAsync(Guid personId, Guid trialId, CancellationToken ct = default);
  Task<Trial> AddAsync(Trial trial, CancellationToken ct = default);
  Task UpdateAsync(Trial trial, CancellationToken ct = default);
  Task RemoveAsync(Trial trial, CancellationToken ct = default);
}

public interface ITrialService
{
  /// <summary>Null when the viewer may not see the target's body data.</summary>
  Task<IReadOnlyList<TrialDto>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, bool includeEnded, CancellationToken ct = default);

  /// <summary>Throws DomainValidationException on bad input.</summary>
  Task<TrialDto> CreateAsync(Actor actor, CreateTrialRequest request, CancellationToken ct = default);

  /// <summary>Own trials only; null when the trial is not the caller's.</summary>
  Task<TrialDto?> UpdateAsync(Actor actor, Guid trialId, UpdateTrialRequest request, CancellationToken ct = default);

  /// <summary>Own trials only; false when the trial is not the caller's.</summary>
  Task<bool> DeleteAsync(Actor actor, Guid trialId, CancellationToken ct = default);
}

/// <summary>The lab review, the timeline, the link catalogue and related readings (docs/labs-design.md, "API").</summary>
public interface ILabReviewService
{
  /// <summary>
  /// One panel against the one before it. Null when the report is missing or its owner has not
  /// shared labs with the viewer. Weight and trials need the body grant and are left out without it.
  /// </summary>
  Task<LabReviewDto?> GetReviewAsync(Guid viewerPersonId, Guid reportId, CancellationToken ct = default);

  /// <summary>Panels oldest first with weight and substances at each draw. Null without the labs grant.</summary>
  Task<LabTimelineDto?> GetTimelineAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default);

  /// <summary>The whole link catalogue. Not person data.</summary>
  IReadOnlyList<LabLinkDto> GetLinks();

  /// <summary>
  /// The readings linked to one marker with the target's latest value of each, every value gated
  /// by its own category. Null when the marker is unknown or the viewer may not see the target's labs.
  /// </summary>
  Task<RelatedReadingsDto?> GetRelatedAsync(Guid viewerPersonId, Guid targetPersonId, string metricKey, CancellationToken ct = default);
}

public interface IHouseholdOverviewService
{
  /// <summary>Every member of the caller's household, each with the summaries the caller may see. Null without a household.</summary>
  Task<HouseholdOverviewDto?> GetAsync(Guid userId, Guid viewerPersonId, CancellationToken ct = default);
}
