using Microsoft.EntityFrameworkCore;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Services.Collect;

/// <summary>
/// The household admin's view of sample retention: the window, where it comes from, the folded
/// months, and a run on demand. Changing the window is audited with the old and new values.
/// </summary>
public sealed class RetentionService : IRetentionService
{
  private const string EnvironmentWins = "The server's environment sets how long raw readings are kept. Change SELFKEPT_RETENTION_MONTHS in the .env file.";

  private readonly IHouseholdRepository _households;
  private readonly RetentionPolicy _policy;
  private readonly SampleRetention _retention;
  private readonly IDbContextFactory<SelfKeptDbContext> _contexts;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;

  public RetentionService(IHouseholdRepository households, RetentionPolicy policy, SampleRetention retention, IDbContextFactory<SelfKeptDbContext> contexts, IAuditService audit, TimeProvider clock)
  {
    _households = households;
    _policy = policy;
    _retention = retention;
    _contexts = contexts;
    _audit = audit;
    _clock = clock;
  }

  public async Task<RetentionDto> GetAsync(Guid userId, CancellationToken ct = default)
  {
    await RequireAdminAsync(userId, ct);
    return await RetentionAsync(ct);
  }

  public async Task<RetentionDto> SaveAsync(Guid userId, SaveRetentionRequest request, RequestInfo info, CancellationToken ct = default)
  {
    var householdId = await RequireAdminAsync(userId, ct);
    var (current, source) = await _policy.ResolveAsync(ct);
    if (source == SettingSource.Environment) throw new ConflictException(EnvironmentWins);

    var months = request.RawMonths ?? throw new DomainValidationException("rawMonths is required.");
    if (!RetentionWindow.IsAllowed(months))
    {
      throw new DomainValidationException(
        $"rawMonths must be 0 (keep raw readings for ever) or {RetentionWindow.MinimumMonths} to {RetentionWindow.MaximumMonths}. " +
        $"Fewer than {RetentionWindow.MinimumMonths} would fold months a phone may still send again.");
    }

    await _policy.SaveAsync(months, userId, ct);
    if (months != current || source != SettingSource.App)
    {
      await _audit.RecordAsync(new AuditEvent(AuditActions.RetentionChanged, userId, householdId, "server_setting", RetentionPolicy.StoreKey,
        new { from = current, to = months }, info.IpAddress), ct);
    }
    return await RetentionAsync(ct);
  }

  public async Task<RetentionRunDto> RunAsync(Guid userId, RequestInfo info, CancellationToken ct = default)
  {
    var householdId = await RequireAdminAsync(userId, ct);
    var result = await _retention.RunAsync(userId, householdId, info, ct);
    if (result.AlreadyRunning) throw new ConflictException("Retention is already running. Try again in a few minutes.");
    return new RetentionRunDto(result.RawSince, result.Folded, result.Compacted.Select(RetentionWindow.Label).ToList());
  }

  private async Task<RetentionDto> RetentionAsync(CancellationToken ct)
  {
    var (months, source) = await _policy.ResolveAsync(ct);
    await using var context = await _contexts.CreateDbContextAsync(ct);
    var folded = await context.SampleRollupMonths.AsNoTracking().OrderByDescending(m => m.Month).ToListAsync(ct);
    return new RetentionDto(
      months,
      source,
      RetentionWindow.MinimumMonths,
      RetentionWindow.RawSince(_clock.GetUtcNow().UtcDateTime, months),
      folded.Select(m => new RetentionMonthDto(m.Month, m.RolledAt, m.RawRows, m.MinuteRows, m.CompactedAt)).ToList());
  }

  /// <summary>The admin's household id; throws for anyone else.</summary>
  private async Task<Guid> RequireAdminAsync(Guid userId, CancellationToken ct)
  {
    var membership = await _households.GetMembershipForUserAsync(userId, ct);
    if (membership?.Role != HouseholdRole.Admin)
      throw new ForbiddenException("Only the household admin can change how long readings are kept.");
    return membership.HouseholdId;
  }
}
