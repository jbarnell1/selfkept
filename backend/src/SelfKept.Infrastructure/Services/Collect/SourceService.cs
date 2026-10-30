using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Collect;

public sealed class SourceService : ISourceService
{
  private readonly ISourceRepository _sources;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;

  public SourceService(ISourceRepository sources, IAuditService audit, TimeProvider clock)
  {
    _sources = sources;
    _audit = audit;
    _clock = clock;
  }

  public async Task<IReadOnlyList<SourceDto>> ListAsync(Guid personId, CancellationToken ct = default) =>
    (await _sources.ListAsync(personId, ct)).Select(s => s.ToDto()).ToList();

  public async Task<SourceDto> UpsertAsync(Guid userId, Guid personId, Guid? householdId, CreateSourceRequest request, RequestInfo info, CancellationToken ct = default)
  {
    var kind = request.Kind.Trim().ToLowerInvariant();
    if (!SourceKinds.All.Contains(kind))
    {
      throw new DomainValidationException($"Unknown source kind '{request.Kind}'. One of: {string.Join(", ", SourceKinds.All)}.");
    }
    var deviceName = request.DeviceName.Trim();
    var platform = request.Platform.Trim().ToLowerInvariant();
    if (deviceName.Length == 0 || platform.Length == 0) throw new DomainValidationException("deviceName and platform are required.");

    var now = _clock.GetUtcNow().UtcDateTime;
    var existing = await _sources.FindActiveAsync(personId, kind, deviceName, platform, ct);
    if (existing != null)
    {
      // The same device install registering again (reinstall, new app version): hand back its row
      // so the client can resume from the stored cursor.
      var appVersion = request.AppVersion?.Trim();
      if (!string.IsNullOrEmpty(appVersion) && appVersion != existing.AppVersion)
      {
        existing.AppVersion = appVersion;
        await _sources.UpdateAsync(existing, ct);
      }
      return existing.ToDto();
    }

    var source = await _sources.AddAsync(new Source
    {
      Id = Guid.CreateVersion7(),
      PersonId = personId,
      Kind = kind,
      DeviceName = deviceName,
      Platform = platform,
      AppVersion = string.IsNullOrWhiteSpace(request.AppVersion) ? null : request.AppVersion.Trim(),
      Enabled = true,
      CreatedAt = now,
    }, ct);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.SourceCreated, userId, householdId, "source", source.Id.ToString(),
      new { kind, platform }, info.IpAddress), ct);

    return source.ToDto();
  }

  public async Task<SourceDto?> UpdateAsync(Guid personId, Guid sourceId, UpdateSourceRequest request, CancellationToken ct = default)
  {
    var source = await _sources.GetAsync(personId, sourceId, ct);
    if (source == null) return null;

    if (request.Enabled is { } enabled) source.Enabled = enabled;
    if (request.DeviceName != null)
    {
      var name = request.DeviceName.Trim();
      if (name.Length == 0) throw new DomainValidationException("deviceName cannot be empty.");
      source.DeviceName = name;
    }
    await _sources.UpdateAsync(source, ct);
    return source.ToDto();
  }

  public async Task<bool> DeleteAsync(Guid userId, Guid personId, Guid? householdId, Guid sourceId, RequestInfo info, CancellationToken ct = default)
  {
    var source = await _sources.GetAsync(personId, sourceId, ct);
    if (source == null) return false;

    // Soft delete: the samples stay (they are the person's data) and keep pointing at this row.
    source.DeletedAt = _clock.GetUtcNow().UtcDateTime;
    source.Enabled = false;
    await _sources.UpdateAsync(source, ct);

    await _audit.RecordAsync(new AuditEvent(
      AuditActions.SourceDeleted, userId, householdId, "source", source.Id.ToString(),
      new { kind = source.Kind, sampleCount = source.SampleCount }, info.IpAddress), ct);
    return true;
  }
}
