using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Services;

/// <summary>
/// Appends audit rows on their own short-lived context, so an audit write never shares the
/// request's unit of work. A failed audit write is logged, never thrown: the user's action already
/// happened and must not be reported as failed because the log could not be written.
/// </summary>
public sealed class AuditService : IAuditService
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly IDbContextFactory<SelfKeptDbContext> _contextFactory;
  private readonly TimeProvider _clock;
  private readonly ILogger<AuditService> _logger;

  public AuditService(IDbContextFactory<SelfKeptDbContext> contextFactory, TimeProvider clock, ILogger<AuditService> logger)
  {
    _contextFactory = contextFactory;
    _clock = clock;
    _logger = logger;
  }

  public async Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default)
  {
    try
    {
      await using var context = await _contextFactory.CreateDbContextAsync(ct);
      context.AuditLogs.Add(new AuditLog
      {
        Id = Guid.CreateVersion7(),
        OccurredAt = _clock.GetUtcNow().UtcDateTime,
        Action = auditEvent.Action,
        ActorUserId = auditEvent.ActorUserId,
        HouseholdId = auditEvent.HouseholdId,
        TargetType = auditEvent.TargetType,
        TargetId = auditEvent.TargetId,
        Details = auditEvent.Details == null ? null : JsonSerializer.Serialize(auditEvent.Details, JsonOptions),
        IpAddress = auditEvent.IpAddress,
      });
      await context.SaveChangesAsync(ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogError(ex, "Audit write failed for {Action}", auditEvent.Action);
    }
  }
}
