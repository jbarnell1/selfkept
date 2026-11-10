using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// The evening check-in (docs/assistant-design.md, "Evening check-in"): one conversation of kind
/// checkin per local day, driven by <see cref="CheckinMachine"/>. It asks only what today's record
/// is missing, harvests each answer at once (source "checkin", the topic as a hint), puts a mood
/// into the existing mood check-in, and says one line of encouragement from the bank before the
/// next question. The model may reword that line; the guard checks it. Leaving keeps the harvest.
/// </summary>
public sealed class CheckinFlowService : ICheckinFlowService
{
  private readonly IAssistantRepository _repository;
  private readonly IPersonRepository _persons;
  private readonly IJournalRepository _journal;
  private readonly IStrengthRepository _strength;
  private readonly IWorkoutRepository _workouts;
  private readonly IDailySummaryRepository _summaries;
  private readonly IJournalCapture _capture;
  private readonly CaptureSaver _saver;
  private readonly ICheckinService _checkins;
  private readonly IReplyGuard _guard;
  private readonly ModelRewording _rewording;
  private readonly IEncryptionService _encryption;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;

  public CheckinFlowService(
    IAssistantRepository repository,
    IPersonRepository persons,
    IJournalRepository journal,
    IStrengthRepository strength,
    IWorkoutRepository workouts,
    IDailySummaryRepository summaries,
    IJournalCapture capture,
    CaptureSaver saver,
    ICheckinService checkins,
    IReplyGuard guard,
    ModelRewording rewording,
    IEncryptionService encryption,
    IAuditService audit,
    TimeProvider clock)
  {
    _repository = repository;
    _persons = persons;
    _journal = journal;
    _strength = strength;
    _workouts = workouts;
    _summaries = summaries;
    _capture = capture;
    _saver = saver;
    _checkins = checkins;
    _guard = guard;
    _rewording = rewording;
    _encryption = encryption;
    _audit = audit;
    _clock = clock;
  }

  public async Task<CheckinTodayDto> TodayAsync(Guid personId, CancellationToken ct = default)
  {
    var (person, zone, today) = await TodayOfAsync(personId, ct);
    var missing = await MissingAsync(person, zone, today, ct);
    var conversation = await _repository.GetOwnCheckinAsync(personId, today, ct);
    var state = conversation == null ? null : CheckinState.FromJson(conversation.State);
    return new CheckinTodayDto(
      today,
      CheckinMachine.Status(state),
      conversation?.Id,
      state?.Left == true ? null : state?.Current,
      state?.Current is { } topic && state.Left != true ? Question(state, topic) : null,
      CheckinTopics.All.Where(missing.Contains).ToList(),
      state?.Done ?? [],
      state?.Skipped ?? []);
  }

  public async Task<CheckinTurnDto> TurnAsync(Actor actor, CheckinTurnRequest request, CancellationToken ct = default)
  {
    var (person, zone, today) = await TodayOfAsync(actor.PersonId, ct);
    var now = _clock.GetUtcNow().UtcDateTime;
    var missing = await MissingAsync(person, zone, today, ct);

    var conversation = await _repository.GetOwnCheckinAsync(actor.PersonId, today, ct);
    CheckinState state;
    var opening = false;
    if (conversation == null)
    {
      state = CheckinMachine.Start(missing);
      conversation = new AssistantConversation
      {
        Id = Guid.CreateVersion7(),
        PersonId = actor.PersonId,
        Kind = ConversationKinds.Checkin,
        Day = today,
        State = state.ToJson(),
        CreatedAt = now,
        UpdatedAt = now,
      };
      await _repository.AddOwnConversationAsync(conversation, ct);
      await _audit.RecordAsync(new AuditEvent(AssistantAuditActions.CheckinStarted, actor.UserId, actor.HouseholdId,
        "assistant_conversation", conversation.Id.ToString(), new { missing = missing.Count }, actor.Info.IpAddress), ct);
      opening = true;
    }
    else
    {
      state = CheckinState.FromJson(conversation.State);
    }

    var answer = string.IsNullOrWhiteSpace(request.Answer) ? null : request.Answer.Trim();
    var captured = new List<JournalEntryDto>();
    int? mood = null;
    Guid? userMessageId = null;
    string? encouragement = null;
    string? topicAnswered = null;
    var reworded = false;

    if (answer != null)
    {
      topicAnswered = state.Left ? null : state.Current;
      var userMessage = await AddMessageAsync(conversation, MessageRoles.User, answer, MessageOrigins.Code, new AssistantMeta(Topic: topicAnswered), now, ct);
      userMessageId = userMessage.Id;

      var flag = _guard.RedFlagReply(answer);
      if (flag != null)
      {
        CheckinMachine.Leave(state);
        await _audit.RecordAsync(new AuditEvent(AssistantAuditActions.RedFlagAnswered, actor.UserId, actor.HouseholdId,
          "assistant_message", userMessage.Id.ToString(), IpAddress: actor.Info.IpAddress), ct);
        return await ReplyAsync(conversation, state, today, null, null, flag, captured, null, userMessageId, MessageOrigins.Guard, true, now, ct);
      }

      var refs = new List<CaptureRefDto>();
      if (topicAnswered != null)
      {
        var harvest = CheckinHarvest.Interpret(_capture, topicAnswered, answer, now, zone);
        var (entries, _) = await _saver.SaveAsync(actor, new CaptureResult(harvest.Drafts, []), JournalSources.Checkin, userMessage.Id, ct);
        captured.AddRange(entries);
        refs.AddRange(CaptureSaver.Refs(entries, []));
        if (harvest.Mood is { } m)
        {
          var checkin = await _checkins.CreateAsync(actor.UserId, actor.PersonId, actor.HouseholdId, new CreateCheckinRequest(null, m, null, null), actor.Info, ct);
          mood = m;
          refs.Add(new CaptureRefDto(AssistantMeta.Mood, checkin.Id, CheckinTopics.Mood, []));
        }
        CheckinMachine.Answer(state, harvest.Outcome);
        var movedOn = state.Current != topicAnswered;
        if (harvest.Outcome == CheckinOutcome.Skipped || (harvest.Outcome == CheckinOutcome.Retry && movedOn))
        {
          encouragement = AssistantTemplates.SkippedLine;
        }
        else if (harvest.Outcome != CheckinOutcome.Retry)
        {
          (encouragement, reworded) = await EncouragementAsync(actor.PersonId, topicAnswered, harvest.Tone, ct);
        }
      }
      else
      {
        // Nothing is being asked (the day is done, or the person left): still keep what they said.
        var found = _capture.Extract(answer, now, zone);
        var (entries, remembered) = await _saver.SaveAsync(actor, found, JournalSources.Checkin, userMessage.Id, ct);
        captured.AddRange(entries);
        refs.AddRange(CaptureSaver.Refs(entries, remembered));
      }
      if (refs.Count > 0)
      {
        userMessage.Meta = new AssistantMeta(refs, Topic: topicAnswered).ToJson();
        await _repository.UpdateOwnMessageAsync(userMessage, ct);
      }
    }

    if (request.Leave == true)
    {
      CheckinMachine.Leave(state);
      return await ReplyAsync(conversation, state, today, null, encouragement, AssistantTemplates.Left, captured, mood, userMessageId,
        MessageOrigins.Code, false, now, ct);
    }

    if (answer == null && (state.Left || state.Complete) && !opening)
    {
      CheckinMachine.Resume(state, missing);
      opening = true;
    }

    string? question = null;
    string closing;
    if (state.Current is { } next)
    {
      question = Question(state, next);
      closing = opening && answer == null ? AssistantTemplates.Join(AssistantTemplates.Opening, question) : question;
    }
    else if (state.NothingMissing && topicAnswered == null && answer == null)
    {
      closing = AssistantTemplates.NothingMissing;
    }
    else if (topicAnswered == null && answer != null)
    {
      closing = AssistantTemplates.AlreadyDone;
    }
    else
    {
      closing = AssistantTemplates.Completed;
    }

    return await ReplyAsync(conversation, state, today, question, encouragement, closing, captured, mood, userMessageId,
      reworded ? MessageOrigins.Model : MessageOrigins.Template, false, now, ct);
  }

  private async Task<CheckinTurnDto> ReplyAsync(
    AssistantConversation conversation, CheckinState state, DateOnly today, string? question, string? encouragement, string closing,
    List<JournalEntryDto> captured, int? mood, Guid? userMessageId, string origin, bool redFlag, DateTime now, CancellationToken ct)
  {
    var reply = AssistantTemplates.Join(encouragement, closing);
    var message = await AddMessageAsync(conversation, MessageRoles.Assistant, reply, origin, new AssistantMeta(Topic: state.Current), now, ct);
    conversation.State = state.ToJson();
    conversation.UpdatedAt = now;
    conversation.EndedAt = state.Complete || state.Left ? now : null;
    await _repository.UpdateOwnConversationAsync(conversation, ct);
    var remaining = state.Current == null ? state.Pending.ToList() : new[] { state.Current }.Concat(state.Pending).ToList();
    return new CheckinTurnDto(
      conversation.Id,
      today,
      CheckinMachine.Status(state),
      state.Left ? null : state.Current,
      encouragement,
      state.Left ? null : question,
      reply,
      captured,
      mood,
      userMessageId,
      message.Id,
      state.Done,
      state.Skipped,
      state.Left ? [] : remaining,
      redFlag);
  }

  private async Task<(string Line, bool Reworded)> EncouragementAsync(Guid personId, string topic, string tone, CancellationToken ct)
  {
    var line = AssistantTemplates.EncouragementFor(topic, tone);
    var reworded = await _rewording.RewordAsync(personId, AssistantAiPurposes.CheckinLine, AssistantTemplates.LinePrompt, line,
      GuardNumbers.Context(line), asLine: true, maxTokens: 80, ct);
    return reworded == null ? (line, false) : (reworded, true);
  }

  private static string Question(CheckinState state, string topic) =>
    topic == CheckinTopics.Mood && state.Retries > 0 ? AssistantTemplates.MoodRetry : AssistantTemplates.Question(topic);

  private async Task<AssistantMessage> AddMessageAsync(AssistantConversation conversation, string role, string text, string origin, AssistantMeta meta, DateTime now, CancellationToken ct)
  {
    var message = new AssistantMessage
    {
      Id = Guid.CreateVersion7(),
      ConversationId = conversation.Id,
      PersonId = conversation.PersonId,
      Role = role,
      ContentCipher = _encryption.Encrypt(text),
      Origin = origin,
      Meta = meta.ToJson(),
      CreatedAt = now,
    };
    return await _repository.AddOwnMessageAsync(message, ct);
  }

  private async Task<(Person Person, TimeZoneInfo Zone, DateOnly Today)> TodayOfAsync(Guid personId, CancellationToken ct)
  {
    var person = await _persons.GetByIdAsync(personId, ct)
      ?? throw new Core.Exceptions.DomainValidationException("No person profile for this account.");
    var zone = DayBoundaries.Resolve(person.TimeZone);
    return (person, zone, DayBoundaries.LocalDay(_clock.GetUtcNow().UtcDateTime, zone));
  }

  /// <summary>The topics today's own record says nothing about yet.</summary>
  public async Task<HashSet<string>> MissingAsync(Person person, TimeZoneInfo zone, DateOnly today, CancellationToken ct)
  {
    var entries = await _journal.ListOwnAsync(person.Id, today, today, ct);
    var kinds = entries.Select(e => e.Kind).ToHashSet(StringComparer.Ordinal);
    var sets = await _strength.ListOwnAsync(person.Id, today, today, ct);
    var (start, end) = DayBoundaries.UtcWindow(today, zone);
    var workouts = await _workouts.ListOwnAsync(person.Id, start, end, ct);
    var mood = await _summaries.GetOwnRangeAsync(person.Id, [MetricKeys.CheckinMood], today, today, ct);

    var missing = new HashSet<string>(StringComparer.Ordinal);
    if (!kinds.Contains(JournalKinds.Food)) missing.Add(CheckinTopics.Eating);
    if (!kinds.Contains(JournalKinds.Training) && sets.Count == 0 && workouts.Count == 0) missing.Add(CheckinTopics.Training);
    if (!kinds.Contains(JournalKinds.Drink)) missing.Add(CheckinTopics.Drinks);
    if (!kinds.Contains(JournalKinds.Symptom)) missing.Add(CheckinTopics.Symptoms);
    if (mood.Count == 0) missing.Add(CheckinTopics.Mood);
    return missing;
  }
}
