using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
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
  private readonly IPhotoAnalyzer _photos;
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
    IPhotoAnalyzer photos,
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
    _photos = photos;
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
    var turn = await _assistant.BeginTurnAsync(CurrentActor(), id, request.Text, request.Photo, ct);
    if (turn == null) return NotFoundProblem("No such conversation.");

    ServerSentEvents.Start(HttpContext);
    await WriteEventAsync(AssistantStreamEvent.Capture, turn.Capture, ct);
    await foreach (var e in _assistant.ReplyAsync(turn, ct))
    {
      await WriteEventAsync(e.Event, e.Data, ct);
    }
    return new EmptyResult();
  }

  /// <summary>
  /// Reads one photo with the home model and answers with a proposal (docs/photo-design.md).
  /// multipart/form-data: image (JPEG, PNG or WebP, at most 6 MB), kind and note optional. The
  /// photo is held in memory for this request only; nothing is saved.
  /// </summary>
  [HttpPost("photos")]
  [RequestSizeLimit(PhotoRequestLimit)]
  // The memory threshold equals the size limit, so the form reader never spills a photo into a
  // temporary file: the photo stays in memory, as docs/photo-design.md promises.
  [RequestFormLimits(MultipartBodyLengthLimit = PhotoRequestLimit, MemoryBufferThreshold = (int)PhotoRequestLimit)]
  public async Task<IActionResult> Photo()
  {
    // No action parameters on purpose: binding one would read the form before this code runs, and
    // a body over the limit would be answered by the framework instead of with the message below.
    var ct = HttpContext.RequestAborted;
    if (!Request.HasFormContentType) return PhotoProblem("Attach a photo.");
    IFormCollection form;
    try
    {
      form = await Request.ReadFormAsync(ct);
    }
    catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
    {
      // The body is over the request limit: the photo is too large, whatever else came with it.
      return PhotoProblem("The photo is larger than 6 MB.");
    }

    var file = form.Files.GetFile("image");
    if (file == null || file.Length == 0) return PhotoProblem("Attach a photo.");
    if (file.Length > Infrastructure.Services.Assistant.PhotoAnalyzer.MaxBytes) return PhotoProblem("The photo is larger than 6 MB.");
    byte[] image;
    using (var buffer = new MemoryStream((int)file.Length))
    {
      await file.CopyToAsync(buffer, ct);
      image = buffer.ToArray();
    }
    var kind = form.TryGetValue("kind", out var k) ? k.ToString() : null;
    var note = form.TryGetValue("note", out var n) ? n.ToString() : null;
    return Ok(await _photos.AnalyzeAsync(CurrentActor(), image, kind, note, ct));
  }

  /// <summary>A little above 6 MB, so a photo at the limit still fits with its form fields.</summary>
  private const long PhotoRequestLimit = 6_500_000;

  private ObjectResult PhotoProblem(string detail) =>
    Problem(statusCode: StatusCodes.Status400BadRequest, title: "Validation failed", detail: detail);

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

  private Task WriteEventAsync(string name, object data, CancellationToken ct) => ServerSentEvents.WriteAsync(Response, name, data, _json, ct);
}
