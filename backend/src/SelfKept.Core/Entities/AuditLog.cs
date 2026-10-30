namespace SelfKept.Core.Entities;

/// <summary>
/// Who did what, when. Security-relevant events only (sign-up, sign-in, invites, sharing);
/// health data changes are not audited here. Append-only.
/// </summary>
public class AuditLog
{
  public Guid Id { get; set; }
  public DateTime OccurredAt { get; set; }

  /// <summary>Dotted event name, for example "auth.login" or "share.changed". See <see cref="AuditActions"/>.</summary>
  public string Action { get; set; } = string.Empty;

  public Guid? ActorUserId { get; set; }
  public Guid? HouseholdId { get; set; }
  public string? TargetType { get; set; }
  public string? TargetId { get; set; }

  /// <summary>Small JSON object with event-specific fields. Never contains secrets or health data.</summary>
  public string? Details { get; set; }

  public string? IpAddress { get; set; }
}

public static class AuditActions
{
  public const string Register = "auth.register";
  public const string Login = "auth.login";
  public const string SsoLinked = "auth.sso.linked";
  public const string SsoUnlinked = "auth.sso.unlinked";
  public const string InviteCreated = "invite.created";
  public const string InviteUsed = "invite.used";
  public const string ShareChanged = "share.changed";
  public const string SourceCreated = "source.created";
  public const string SourceDeleted = "source.deleted";
  public const string CheckinCreated = "checkin.created";
  public const string InsightReaction = "insight.reaction";
  public const string InsightDismissed = "insight.dismiss";
  public const string InterpretRecompute = "interpret.recompute";
  public const string LabReportCreated = "lab.report.created";
  public const string LabReportDeleted = "lab.report.deleted";
  public const string LabSummaryExported = "lab.summary.exported";
  public const string McpClientRegistered = "mcp.client.registered";
  public const string McpGrantApproved = "mcp.grant.approved";
  public const string McpGrantDenied = "mcp.grant.denied";
  public const string McpGrantRevoked = "mcp.grant.revoked";
  public const string McpTokenCreated = "mcp.token.created";
  public const string McpTokenRevoked = "mcp.token.revoked";
  public const string McpToolCall = "mcp.tool";
  public const string MemoryDeleted = "memory.deleted";
  public const string OuraConnected = "oura.connected";
  public const string OuraDisconnected = "oura.disconnected";
  public const string ExportBundle = "export.bundle";
  public const string ExportFull = "export.full";
  public const string IntegrationChanged = "integration.changed";
}
