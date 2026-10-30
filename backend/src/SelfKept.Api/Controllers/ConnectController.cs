using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Connect;

namespace SelfKept.Api.Controllers;

/// <summary>
/// The app's view of assistant connections (You, Connections): the MCP address to paste into an
/// assistant, connected assistants with revoke, personal tokens, and what assistants remember.
/// Everything here is the caller's own; nothing is shared.
/// </summary>
[Route("api/connect")]
[Authorize]
public sealed class ConnectController : BaseController
{
  private readonly IOAuthService _oauth;
  private readonly IPersonalTokenService _tokens;
  private readonly IAiMemoryService _memory;
  private readonly IAuditService _audit;
  private readonly McpSettings _settings;

  public ConnectController(IOAuthService oauth, IPersonalTokenService tokens, IAiMemoryService memory, IAuditService audit, McpSettings settings)
  {
    _oauth = oauth;
    _tokens = tokens;
    _memory = memory;
    _audit = audit;
    _settings = settings;
  }

  [HttpGet]
  public async Task<IActionResult> Get(CancellationToken ct)
  {
    var userId = GetCurrentUserId();
    var assistants = await _oauth.ListGrantsAsync(userId, ct);
    var tokens = await _tokens.ListAsync(userId, ct);
    return Ok(new ConnectionsDto(assistants, tokens, _settings.ResourceUri ?? string.Empty));
  }

  [HttpDelete("assistants/{clientId}")]
  public async Task<IActionResult> RevokeAssistant(string clientId, CancellationToken ct) =>
    await _oauth.RevokeGrantAsync(GetCurrentUserId(), GetCurrentHouseholdId(), clientId, CurrentRequestInfo(), ct)
      ? NoContent()
      : NotFoundProblem("No connection to that assistant.");

  [HttpPost("tokens")]
  public async Task<IActionResult> CreateToken([FromBody] CreatePersonalTokenRequest request, CancellationToken ct) =>
    Ok(await _tokens.CreateAsync(GetCurrentUserId(), GetCurrentHouseholdId(), request, CurrentRequestInfo(), ct));

  [HttpDelete("tokens/{id:guid}")]
  public async Task<IActionResult> RevokeToken(Guid id, CancellationToken ct) =>
    await _tokens.RevokeAsync(GetCurrentUserId(), GetCurrentHouseholdId(), id, CurrentRequestInfo(), ct)
      ? NoContent()
      : NotFoundProblem("No such token.");

  [HttpGet("memory")]
  public async Task<IActionResult> Memory([FromQuery] string? topic, CancellationToken ct) =>
    Ok(await _memory.RecallAsync(GetCurrentPersonId(), topic, ct));

  [HttpDelete("memory/{id:guid}")]
  public async Task<IActionResult> DeleteMemory(Guid id, CancellationToken ct)
  {
    if (!await _memory.DeleteAsync(GetCurrentPersonId(), id, ct))
      return NotFoundProblem("No such memory.");
    await _audit.RecordAsync(new AuditEvent(AuditActions.MemoryDeleted, GetCurrentUserId(), GetCurrentHouseholdId(), "ai_memory", id.ToString(),
      IpAddress: CurrentRequestInfo().IpAddress), ct);
    return NoContent();
  }
}
