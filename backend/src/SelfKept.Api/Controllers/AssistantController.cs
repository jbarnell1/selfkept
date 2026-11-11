using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SelfKept.Api.Extensions;
using SelfKept.Core.Auth;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// The in-app assistant (docs/assistant-design.md). Everything here is the caller's own: there is
/// no personId parameter on any route, and nothing here is shared with the household. Replies to
/// a chat message stream as server-sent events: capture, delta, replace, done.
/// </summary>
[Authorize]
public sealed class AssistantController : BaseController
{
  private readonly IAssistantService _assistant;
  private readonly ICheckinFlowService _checkin;
  private readonly IMorningBriefService _brief;
  private readonly IPlaybookService _playbook;
  private readonly INudgeService _nudges;
  private readonly IModelCheckService _modelCheck;
  private readonly IAiMemoryService _memory;
  private readonly IPersonRepository _persons;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;
  private readonly JsonSerializerOptions _json;

  public AssistantController(
    IAssistantService assistant,
    ICheckinFlowService checkin,
    IMorningBriefService brief,
    IPlaybookService playbook,
    INudgeService nudges,
    IModelCheckService modelCheck,
    IAiMemoryService memory,
    IPersonRepository persons,
    IAuditService audit,
    TimeProvider clock,
    IOptions<JsonOptions> json)
  {
    _assistant = assistant;
    _checkin = checkin;
    _brief = brief;
    _playbook = playbook;
    _nudges = nudges;
    _modelCheck = modelCheck;
    _memory = memory;
    _persons = persons;
    _audit = audit;
    _clock = clock;
    _json = json.Value.JsonSerializerOptions;
  }

  [HttpGet("status")]
  public async Task<IActionResult> Status(CancellationToken ct) => Ok(await _assistant.StatusAsync(ct));

  [HttpGet("conversations")]
  public async Task<IActionResult> Conversations(CancellationToken ct) => Ok(await _assistant.ListAsync(GetCurrentPersonId(), ct));

  [HttpPost("conversations")]
  public async Task<IActionResult> CreateConversation([FromBody] CreateConversationRequest? request, CancellationToken ct) =>
    Ok(await _assistant.CreateAsync(CurrentActor(), request ?? new CreateConversationRequest(null), ct));

  [HttpGet("conversations/{id:guid}")]
  public async Task<IActionResult> Conversation(Guid id, CancellationToken ct) =>
    HandleServiceResponse(await _assistant.GetAsync(GetCurrentPersonId(), id, ct), "No such conversation.");

  [HttpDelete("conversations/{id:guid}")]
  public async Task<IActionResult> DeleteConversation(Guid id, CancellationToken ct) =>
    await _assistant.DeleteAsync(CurrentActor(), id, ct) ? NoContent() : NotFoundProblem("No such conversation.");

  /// <summary>Saves the message and answers as text/event-stream. Errors before the stream starts are problem details.</summary>
  [HttpPost("conversations/{id:guid}/messages")]
  public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendMessageRequest request, CancellationToken ct)
  {
    var turn = await _assistant.BeginTurnAsync(CurrentActor(), id, request.Text, ct);
    if (turn == null) return NotFoundProblem("No such conversation.");

    Response.StatusCode = StatusCodes.Status200OK;
    Response.ContentType = "text/event-stream; charset=utf-8";
    Response.Headers.CacheControl = "no-cache, no-store";
    Response.Headers["X-Accel-Buffering"] = "no";
    HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

    await WriteEventAsync(AssistantStreamEvent.Capture, turn.Capture, ct);
    await foreach (var e in _assistant.ReplyAsync(turn, ct))
    {
      await WriteEventAsync(e.Event, e.Data, ct);
    }
    return new EmptyResult();
  }

  /// <summary>Undo: removes the day log entries, remembered facts and mood that one message saved.</summary>
  [HttpDelete("messages/{messageId:guid}/captures")]
  public async Task<IActionResult> UndoCaptures(Guid messageId, CancellationToken ct) =>
    HandleServiceResponse(await _assistant.UndoCapturesAsync(CurrentActor(), messageId, ct), "No such message.");

  [HttpPost("checkin")]
  public async Task<IActionResult> Checkin([FromBody] CheckinTurnRequest? request, CancellationToken ct) =>
    Ok(await _checkin.TurnAsync(CurrentActor(), request ?? new CheckinTurnRequest(null, null), ct));

  [HttpGet("checkin/today")]
  public async Task<IActionResult> CheckinToday(CancellationToken ct) => Ok(await _checkin.TodayAsync(GetCurrentPersonId(), ct));

  [HttpGet("brief")]
  public async Task<IActionResult> Brief(CancellationToken ct) =>
    HandleServiceResponse(await _brief.GetAsync(GetCurrentPersonId(), ct), "No person profile for this account.");

  [HttpGet("memory")]
  public async Task<IActionResult> Memory(CancellationToken ct) => Ok(await _memory.RecallAsync(GetCurrentPersonId(), null, ct));

  [HttpDelete("memory/{id:guid}")]
  public async Task<IActionResult> DeleteMemory(Guid id, CancellationToken ct)
  {
    if (!await _memory.DeleteAsync(GetCurrentPersonId(), id, ct)) return NotFoundProblem("No such memory.");
    await _audit.RecordAsync(new AuditEvent(Core.Entities.AuditActions.MemoryDeleted, GetCurrentUserId(), GetCurrentHouseholdId(), "ai_memory", id.ToString(),
      IpAddress: CurrentRequestInfo().IpAddress), ct);
    return NoContent();
  }

  [HttpGet("playbook")]
  public async Task<IActionResult> Playbook(CancellationToken ct)
  {
    var person = await _persons.GetByIdAsync(GetCurrentPersonId(), ct);
    if (person == null) return NotFoundProblem("No person profile for this account.");
    var zone = Infrastructure.Services.Collect.DayBoundaries.Resolve(person.TimeZone);
    var today = Infrastructure.Services.Collect.DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, zone);
    var entries = await _playbook.ApplicableAsync(person, today, ct);
    return Ok(entries.Select(p => new PlaybookEntryDto(p.Key, p.Topic, p.Title, p.Text, p.EvidenceGrade, p.Source, p.SourceUrl, p.Why, p.Cautions)).ToList());
  }

  [HttpGet("nudges")]
  public async Task<IActionResult> Nudges(CancellationToken ct) => Ok(await _nudges.GetAsync(GetCurrentPersonId(), ct));

  [HttpPut("nudges")]
  public async Task<IActionResult> SaveNudges([FromBody] SaveNudgeSettingsRequest request, CancellationToken ct) =>
    Ok(await _nudges.SaveAsync(CurrentActor(), request, ct));

  [HttpPost("nudges/test")]
  [EnableRateLimiting(RateLimitPolicies.Recompute)]
  public async Task<IActionResult> TestNudge(CancellationToken ct) => Ok(await _nudges.TestAsync(CurrentActor(), ct));

  [HttpPost("model-check")]
  [Authorize(Roles = TokenRoles.Admin)]
  [EnableRateLimiting(RateLimitPolicies.Recompute)]
  public async Task<IActionResult> ModelCheck(CancellationToken ct) => Ok(await _modelCheck.RunAsync(CurrentActor(), ct));

  private async Task WriteEventAsync(string name, object data, CancellationToken ct)
  {
    var payload = JsonSerializer.Serialize(data, data.GetType(), _json);
    var frame = Encoding.UTF8.GetBytes($"event: {name}\ndata: {payload}\n\n");
    await Response.Body.WriteAsync(frame, ct);
    await Response.Body.FlushAsync(ct);
  }
}
