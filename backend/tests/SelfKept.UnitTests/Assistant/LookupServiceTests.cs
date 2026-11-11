using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services;
using SelfKept.Infrastructure.Services.Assistant;
using SelfKept.Infrastructure.Services.Assistant.Lookup;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

/// <summary>
/// The cloud lookup service over the real repositories on InMemory, with a fake provider client,
/// a fake local model and a fixed state brief: the switch, the draft, the checked send, every way
/// a send ends, the "applied locally" step, and what is never written to logs, audit or ai_call.
/// </summary>
public sealed class LookupServiceTests : IAsyncDisposable
{
  private static readonly DateTime Now = new(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);
  private const string Question = "Does magnesium help Ada sleep with restless legs? Bob takes 187.4 mg on March 3. Mail ada@example.com";
  private const string CleanText = "Does magnesium glycinate help adults with restless legs sleep?";

  private readonly SelfKeptDbContext _db = new(new DbContextOptionsBuilder<SelfKeptDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
  private readonly Guid _household = Guid.NewGuid();
  private readonly Guid _ada = Guid.NewGuid();
  private readonly Guid _bob = Guid.NewGuid();
  private readonly Guid _conversation = Guid.NewGuid();
  private readonly Guid _question = Guid.NewGuid();
  private readonly Guid _bobQuestion = Guid.NewGuid();
  private readonly FakeCloudLookupClient _client = new();
  private readonly FakeLocalModel _model = new();
  private readonly CapturingAudit _audit = new();
  private readonly CapturingAiCalls _calls = new();
  private readonly CapturingLogger<LookupService> _logger = new();
  private readonly EncryptionService _encryption = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
  {
    ["Encryption:Key"] = "unit-test-encryption-key-with-plenty-of-entropy-123456",
  }).Build());
  private readonly LookupService _service;

  public LookupServiceTests()
  {
    _db.Persons.Add(new Person { Id = _ada, DisplayName = "Ada Lovelace", BirthDate = new DateOnly(1982, 11, 11), SexAtBirth = SexAtBirth.Female, TimeZone = "UTC", CreatedAt = Now, UpdatedAt = Now });
    _db.Persons.Add(new Person { Id = _bob, DisplayName = "Bob", TimeZone = "UTC", CreatedAt = Now, UpdatedAt = Now });
    _db.Households.Add(new Household { Id = _household, Name = "Home", CreatedAt = Now, UpdatedAt = Now });
    _db.HouseholdMembers.Add(new HouseholdMember { Id = Guid.NewGuid(), HouseholdId = _household, PersonId = _ada, JoinedAt = Now });
    _db.HouseholdMembers.Add(new HouseholdMember { Id = Guid.NewGuid(), HouseholdId = _household, PersonId = _bob, JoinedAt = Now.AddMinutes(1) });
    _db.AssistantConversations.Add(new AssistantConversation { Id = _conversation, PersonId = _ada, CreatedAt = Now, UpdatedAt = Now });
    _db.AssistantMessages.Add(new AssistantMessage { Id = _question, ConversationId = _conversation, PersonId = _ada, Role = MessageRoles.User, ContentCipher = _encryption.Encrypt(Question), CreatedAt = Now });
    var bobConversation = Guid.NewGuid();
    _db.AssistantConversations.Add(new AssistantConversation { Id = bobConversation, PersonId = _bob, CreatedAt = Now, UpdatedAt = Now });
    _db.AssistantMessages.Add(new AssistantMessage { Id = _bobQuestion, ConversationId = bobConversation, PersonId = _bob, Role = MessageRoles.User, ContentCipher = _encryption.Encrypt("Is zinc good?"), CreatedAt = Now });
    _db.SaveChanges();

    var brief = StateBriefComposer.Compose(
      [
        new BriefSection("Today", ["Today is Tuesday 10 November 2026 for Ada Lovelace.", "They prefer metric units."]),
        new BriefSection(StateBriefBuilder.GoalsTitle, ["Main goal: Sleep better. Status: on track."]),
        new BriefSection(StateBriefBuilder.ReadingsTitle, ["Sleep: 6h 10m last night; usual range 7h to 8h, so below your usual range."]),
        new BriefSection(StateBriefBuilder.MedicinesTitle, ["Supplement: Magnesium glycinate, 200 mg, evening."]),
        new BriefSection("Latest blood panel", ["Collected on 1 October 2026."]),
      ],
      [],
      new BriefFacts(null, [], null, null, []));
    var briefs = new Mock<IStateBriefBuilder>();
    briefs.Setup(b => b.BuildAsync(It.IsAny<Person>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(brief);

    var clock = new TestClock(Now);
    var guard = new ReplyGuard();
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://mybox.tail1234.ts.net" }).Build();
    _service = new LookupService(
      new LookupRepository(_db),
      new AssistantRepository(_db),
      new PersonRepository(_db),
      new HouseholdRepository(_db),
      briefs.Object,
      guard,
      _model,
      new ModelRewording(_model, guard, _calls, clock, NullLogger<ModelRewording>.Instance),
      _calls,
      _client,
      _encryption,
      _audit,
      configuration,
      clock,
      _logger);
  }

  public ValueTask DisposeAsync() => _db.DisposeAsync();

  private Actor Ada => new(Guid.NewGuid(), _ada, _household, new RequestInfo("127.0.0.1"));
  private Actor Bob => new(Guid.NewGuid(), _bob, _household, new RequestInfo("127.0.0.1"));

  private Task TurnOnAsync(Actor actor) => _service.SaveAsync(actor, new SaveLookupSettingRequest(true, true));

  private static SendLookupRequest Send(Guid messageId, string text = CleanText, bool acknowledged = true, string mode = LookupModes.Anonymized) =>
    new(messageId, mode, text, acknowledged);

  private async Task<List<AssistantStreamEvent>> SendAllAsync(LookupSend send)
  {
    var events = new List<AssistantStreamEvent>();
    await foreach (var e in _service.SendAsync(send)) events.Add(e);
    return events;
  }

  private async Task<LookupSend> PreparedAsync(string text = CleanText)
  {
    await TurnOnAsync(Ada);
    return (await _service.PrepareSendAsync(Ada, Send(_question, text)))!;
  }

  // ---- the switch ----

  [Fact]
  public async Task SaveAsync_TurnOnWithoutAcknowledgement_IsRefused()
  {
    var act = () => _service.SaveAsync(Ada, new SaveLookupSettingRequest(true, false));

    await act.Should().ThrowAsync<UnprocessableException>();
    (await _service.GetAsync(_ada)).Enabled.Should().BeFalse();
  }

  [Fact]
  public async Task SaveAsync_TurnOnWithAcknowledgement_TurnsItOnAndAudits()
  {
    var status = await _service.SaveAsync(Ada, new SaveLookupSettingRequest(true, true));

    status.Should().Be(new LookupStatusDto(true, true, "Anthropic", "claude-opus-5-5", false));
    _audit.Events.Should().ContainSingle(e => e.Action == AssistantAuditActions.LookupEnabled);
    (await _service.GetAsync(_bob)).Enabled.Should().BeFalse("the switch is each person's own");
  }

  [Fact]
  public async Task SaveAsync_TurnOffWithoutAcknowledgement_TurnsItOff()
  {
    await TurnOnAsync(Ada);

    var status = await _service.SaveAsync(Ada, new SaveLookupSettingRequest(false, false));

    status.Enabled.Should().BeFalse();
    _audit.Events.Select(e => e.Action).Should().Equal(AssistantAuditActions.LookupEnabled, AssistantAuditActions.LookupDisabled);
  }

  // ---- the draft ----

  [Fact]
  public async Task DraftAsync_PersonSwitchOff_IsAConflict()
  {
    var act = () => _service.DraftAsync(Ada, new LookupDraftRequest(_question, LookupModes.Anonymized));

    await act.Should().ThrowAsync<ConflictException>();
  }

  [Fact]
  public async Task DraftAsync_Anonymized_TakesOutNamesContactsDatesAndNumbers()
  {
    await TurnOnAsync(Ada);

    var draft = await _service.DraftAsync(Ada, new LookupDraftRequest(_question, LookupModes.Anonymized));

    draft!.Text.Should().Be("Does magnesium help someone sleep with restless legs? someone takes 190 mg recently. Mail (email removed)");
    draft.Changes.Should().BeEquivalentTo(new[]
    {
      new LookupChangeDto(LookupChangeKinds.Email, 1), new LookupChangeDto(LookupChangeKinds.Name, 2),
      new LookupChangeDto(LookupChangeKinds.Date, 1), new LookupChangeDto(LookupChangeKinds.Number, 1),
    });
    draft.Reworded.Should().BeFalse();
    draft.Provider.Should().Be("Anthropic");
    _client.Asked.Should().BeEmpty("a draft sends nothing");
  }

  [Fact]
  public async Task DraftAsync_Detailed_AddsAboutMeFromTheBrief_WithoutBirthDateOrName()
  {
    await TurnOnAsync(Ada);

    var draft = await _service.DraftAsync(Ada, new LookupDraftRequest(_question, LookupModes.Detailed));

    draft!.Text.Should().StartWith("Does magnesium help someone sleep with restless legs? someone takes 187.4 mg on March 3. Mail (email removed)\n\nAbout me:\n- In my 40s.\n- Sex at birth: female.");
    draft.Text.Should().Contain("- Main goal: Sleep better.").And.Contain("- Supplement: Magnesium glycinate, 200 mg, evening.").And.Contain("- Sleep: 6h 10m last night");
    draft.Text.Should().NotContain("1982").And.NotContain("Lovelace").And.NotContain("Collected on").And.NotContain("Today is");
    draft.Changes.Should().Contain(new LookupChangeDto(LookupChangeKinds.Record, 1));
  }

  [Fact]
  public async Task DraftAsync_RedFlagMessage_IsRefused()
  {
    await TurnOnAsync(Ada);
    var redFlag = Guid.NewGuid();
    _db.AssistantMessages.Add(new AssistantMessage { Id = redFlag, ConversationId = _conversation, PersonId = _ada, Role = MessageRoles.User, ContentCipher = _encryption.Encrypt("I have crushing chest pain, what should I take?"), CreatedAt = Now });
    await _db.SaveChangesAsync();

    var act = () => _service.DraftAsync(Ada, new LookupDraftRequest(redFlag, LookupModes.Anonymized));

    await act.Should().ThrowAsync<UnprocessableException>();
  }

  [Fact]
  public async Task DraftAsync_AnotherPersonsMessage_IsNotFound()
  {
    await TurnOnAsync(Bob);

    (await _service.DraftAsync(Bob, new LookupDraftRequest(_question, LookupModes.Anonymized))).Should().BeNull();
  }

  [Fact]
  public async Task DraftAsync_UnknownMode_IsAValidationError()
  {
    await TurnOnAsync(Ada);

    var act = () => _service.DraftAsync(Ada, new LookupDraftRequest(_question, "everything"));

    await act.Should().ThrowAsync<DomainValidationException>();
  }

  [Fact]
  public async Task DraftAsync_LocalModelRewordsCleanly_KeepsItsVersion()
  {
    await TurnOnAsync(Ada);
    _model.Enabled = true;
    _model.Answer = (system, _) => system == LookupService.RewordPrompt ? "Does magnesium help adults with restless legs sleep?" : string.Empty;

    var draft = await _service.DraftAsync(Ada, new LookupDraftRequest(_question, LookupModes.Anonymized));

    draft!.Text.Should().Be("Does magnesium help adults with restless legs sleep?");
    draft.Reworded.Should().BeTrue();
    _calls.Calls.Should().ContainSingle(c => c.Purpose == AssistantAiPurposes.LookupDraft && c.Ok);
  }

  [Fact]
  public async Task DraftAsync_LocalModelAddsANumber_DropsItsVersion()
  {
    await TurnOnAsync(Ada);
    _model.Enabled = true;
    _model.Answer = (_, _) => "Does 400 mg of magnesium help adults sleep?";

    var draft = await _service.DraftAsync(Ada, new LookupDraftRequest(_question, LookupModes.Anonymized));

    draft!.Reworded.Should().BeFalse();
    draft.Text.Should().StartWith("Does magnesium help someone sleep");
    _calls.Calls.Should().ContainSingle(c => c.Purpose == AssistantAiPurposes.LookupDraft && !c.Ok && c.Error == "rejected: number");
  }

  [Fact]
  public void CheckRewording_ModelAddsAHouseholdName_IsDropped()
  {
    var context = new LookupScrubContext(["Bob"], null);

    LookupService.CheckRewording("Does zinc help with colds?", "Does zinc help Bob with colds?", context).Kept.Should().BeNull();
  }

  // ---- the send check ----

  [Fact]
  public async Task PrepareSendAsync_WithoutAcknowledgement_IsRefused()
  {
    await TurnOnAsync(Ada);

    var act = () => _service.PrepareSendAsync(Ada, Send(_question, acknowledged: false));

    await act.Should().ThrowAsync<UnprocessableException>();
  }

  [Fact]
  public async Task PrepareSendAsync_PersonSwitchOff_IsAConflict()
  {
    var act = () => _service.PrepareSendAsync(Ada, Send(_question));

    await act.Should().ThrowAsync<ConflictException>();
  }

  [Fact]
  public async Task PrepareSendAsync_EditedTextWithAHardIdentifier_IsRefusedWithFindings()
  {
    await TurnOnAsync(Ada);

    var act = () => _service.PrepareSendAsync(Ada, Send(_question, "Does magnesium help Bob? Reply to bob@example.com"));

    var thrown = await act.Should().ThrowAsync<UnprocessableException>();
    thrown.Which.Findings.Should().BeEquivalentTo(new[] { new LookupChangeDto(LookupChangeKinds.Email, 1), new LookupChangeDto(LookupChangeKinds.Name, 1) });
    _client.Asked.Should().BeEmpty();
  }

  [Fact]
  public async Task PrepareSendAsync_RedFlagInTheText_IsRefused()
  {
    await TurnOnAsync(Ada);

    var act = () => _service.PrepareSendAsync(Ada, Send(_question, "What helps with crushing chest pain?"));

    await act.Should().ThrowAsync<UnprocessableException>();
  }

  [Fact]
  public async Task PrepareSendAsync_AnotherPersonsMessage_IsNotFound()
  {
    await TurnOnAsync(Bob);

    (await _service.PrepareSendAsync(Bob, Send(_question))).Should().BeNull();
  }

  [Fact]
  public async Task PrepareSendAsync_BlankText_IsAValidationError()
  {
    await TurnOnAsync(Ada);

    var act = () => _service.PrepareSendAsync(Ada, Send(_question, "   "));

    await act.Should().ThrowAsync<DomainValidationException>();
  }

  // ---- the send ----

  [Fact]
  public async Task SendAsync_Answered_StoresTheAnswerWithSourcesAndAudits()
  {
    var send = await PreparedAsync();

    var events = await SendAllAsync(send);

    events.Select(e => e.Event).Should().Equal(LookupStreamEvents.Sent, LookupStreamEvents.Done);
    var done = (LookupDoneEventDto)events[1].Data;
    ((LookupSentEventDto)events[0].Data).LookupId.Should().Be(done.LookupId);
    done.Message.Origin.Should().Be(MessageOrigins.Cloud);
    done.Message.Content.Should().Be(FakeCloudLookupClient.AnswerText);
    done.Message.Sources.Should().Equal(FakeCloudLookupClient.AnswerSources);
    done.Message.LookupId.Should().Be(done.LookupId);
    done.Applied.Should().BeNull("there is no local model");
    _client.Asked.Should().Equal(CleanText);

    var stored = await _db.AssistantLookups.SingleAsync();
    stored.Status.Should().Be(LookupStatuses.Answered);
    stored.AnswerMessageId.Should().Be(done.Message.Id);
    stored.QuestionMessageId.Should().Be(_question);
    _encryption.Decrypt(stored.QuestionCipher).Should().Be(CleanText);
    stored.InputTokens.Should().Be(1200);
    var message = await _db.AssistantMessages.SingleAsync(m => m.Id == done.Message.Id);
    message.Meta.Should().Be($"{{\"lookupId\":\"{done.LookupId}\"}}", "a cloud answer's meta holds the lookup id and nothing else");

    var sent = _audit.Events.Single(e => e.Action == AssistantAuditActions.LookupSent);
    JsonSerializer.Serialize(sent.Details).Should().Be($"{{\"mode\":\"anonymized\",\"provider\":\"Anthropic\",\"model\":\"claude-opus-5-5\",\"characters\":{CleanText.Length}}}");
    _calls.Calls.Should().ContainSingle(c => c.Purpose == AssistantAiPurposes.Lookup && c.Ok && c.PromptTokens == 1200 && c.CompletionTokens == 300);
  }

  [Theory]
  [InlineData(CloudLookupOutcome.Refused, "refused")]
  [InlineData(CloudLookupOutcome.CutOff, "cut_off")]
  [InlineData(CloudLookupOutcome.SearchFailed, "search_failed")]
  [InlineData(CloudLookupOutcome.Unreachable, "unreachable")]
  [InlineData(CloudLookupOutcome.RateLimited, "rate_limited")]
  [InlineData(CloudLookupOutcome.KeyRejected, "key_rejected")]
  public async Task SendAsync_ProviderFails_StoresNoAnswerAndSaysWhy(CloudLookupOutcome outcome, string reason)
  {
    var send = await PreparedAsync();
    _client.Result = CloudLookupResult.Failed(outcome, "detail");

    var events = await SendAllAsync(send);

    events.Select(e => e.Event).Should().Equal(LookupStreamEvents.Sent, LookupStreamEvents.Failed);
    var failed = (LookupFailedEventDto)events[1].Data;
    failed.Reason.Should().Be(reason);
    failed.Text.Should().NotBeNullOrWhiteSpace();
    (await _db.AssistantLookups.SingleAsync()).Status.Should().Be(LookupStatuses.Failed);
    (await _db.AssistantMessages.CountAsync(m => m.Origin == MessageOrigins.Cloud)).Should().Be(0);
    _calls.Calls.Should().ContainSingle(c => c.Purpose == AssistantAiPurposes.Lookup && !c.Ok && c.Error == reason + ": detail");
  }

  [Fact]
  public async Task SendAsync_ClientThrows_IsUnreachable()
  {
    var send = await PreparedAsync();
    _client.Gate = new TaskCompletionSource();
    _client.Gate.SetException(new HttpRequestException("boom"));

    var events = await SendAllAsync(send);

    ((LookupFailedEventDto)events[^1].Data).Reason.Should().Be(LookupFailureReasons.Unreachable);
  }

  [Fact]
  public async Task SendAsync_ReaderStopsAfterSent_StillStoresTheAnswer()
  {
    var send = await PreparedAsync();
    _client.Gate = new TaskCompletionSource();

    var stream = _service.SendAsync(send).GetAsyncEnumerator();
    (await stream.MoveNextAsync()).Should().BeTrue();
    stream.Current.Event.Should().Be(LookupStreamEvents.Sent);
    // The app goes away: the reader stops. Disposing waits for the provider, which answers later.
    var disposing = stream.DisposeAsync().AsTask();
    await Task.Delay(50);
    disposing.IsCompleted.Should().BeFalse("the work outlives the reader");
    _client.Gate.SetResult();
    await disposing;

    var stored = await _db.AssistantLookups.SingleAsync();
    stored.Status.Should().Be(LookupStatuses.Answered);
    (await _db.AssistantMessages.CountAsync(m => m.Origin == MessageOrigins.Cloud && m.ConversationId == _conversation)).Should().Be(1);
  }

  [Fact]
  public async Task SendAsync_SlowProvider_SendsWaitingUntilTheAnswer()
  {
    var send = await PreparedAsync();
    _client.Gate = new TaskCompletionSource();
    _service.WaitingEvery = TimeSpan.FromMilliseconds(20);

    var events = new List<AssistantStreamEvent>();
    await foreach (var e in _service.SendAsync(send))
    {
      events.Add(e);
      if (events.Count(x => x.Event == LookupStreamEvents.Waiting) == 2) _client.Gate.TrySetResult();
    }

    events[0].Event.Should().Be(LookupStreamEvents.Sent);
    events.Skip(1).SkipLast(1).Should().OnlyContain(e => e.Event == LookupStreamEvents.Waiting).And.HaveCountGreaterThanOrEqualTo(2);
    events[^1].Event.Should().Be(LookupStreamEvents.Done);
  }

  [Fact]
  public async Task SendAsync_LocalModelOn_AddsTheAppliedMessage()
  {
    var send = await PreparedAsync();
    _model.Enabled = true;
    _model.Answer = (system, _) => system.StartsWith(LookupService.AppliedPrompt, StringComparison.Ordinal)
      ? "Your sleep was below your usual range last night, and you already take magnesium glycinate in the evening. The evidence for it is mixed."
      : string.Empty;

    var events = await SendAllAsync(send);

    var done = (LookupDoneEventDto)events[^1].Data;
    done.Applied.Should().NotBeNull();
    done.Applied!.Origin.Should().Be(MessageOrigins.Model);
    done.Applied.Sources.Should().BeEmpty();
    done.Applied.LookupId.Should().BeNull();
    (await _db.AssistantMessages.CountAsync(m => m.ConversationId == _conversation)).Should().Be(3);
    _calls.Calls.Should().ContainSingle(c => c.Purpose == AssistantAiPurposes.LookupApplied && c.Ok);
  }

  [Fact]
  public async Task SendAsync_AppliedMessageFailsTheGuard_IsDropped()
  {
    var send = await PreparedAsync();
    _model.Enabled = true;
    _model.Answer = (_, _) => "Take 900 mg of magnesium tonight and your sleep will improve.";

    var events = await SendAllAsync(send);

    var done = (LookupDoneEventDto)events[^1].Data;
    done.Applied.Should().BeNull();
    done.Message.Origin.Should().Be(MessageOrigins.Cloud);
    (await _db.AssistantMessages.CountAsync(m => m.ConversationId == _conversation)).Should().Be(2);
    _calls.Calls.Should().ContainSingle(c => c.Purpose == AssistantAiPurposes.LookupApplied && !c.Ok);
  }

  [Fact]
  public async Task SendAsync_NoLogAuditOrAiCall_HoldsTheQuestionOrTheAnswer()
  {
    var send = await PreparedAsync();
    await SendAllAsync(send);
    _client.Result = CloudLookupResult.Failed(CloudLookupOutcome.Refused, "refusal");
    await SendAllAsync((await _service.PrepareSendAsync(Ada, Send(_question)))!);

    var written = string.Join("\n", _logger.Lines)
      + "\n" + string.Join("\n", _audit.Events.Select(e => e.Action + JsonSerializer.Serialize(e.Details)))
      + "\n" + string.Join("\n", _calls.Calls.Select(c => JsonSerializer.Serialize(c)));

    _logger.Lines.Should().NotBeEmpty("the refusal is logged, by reason only");
    written.Should().NotContain("restless legs").And.NotContain("magnesium").And.NotContain("modestly");
  }

  // ---- list, delete, offer ----

  [Fact]
  public async Task ListAsync_ShowsOnlyTheCallersOwn_WithTheOutgoingText()
  {
    await SendAllAsync(await PreparedAsync());

    var mine = await _service.ListAsync(_ada);

    mine.Should().ContainSingle();
    mine[0].Text.Should().Be(CleanText);
    mine[0].Status.Should().Be(LookupStatuses.Answered);
    (await _service.ListAsync(_bob)).Should().BeEmpty();
  }

  [Fact]
  public async Task DeleteAsync_AnotherPerson_CannotDelete()
  {
    await SendAllAsync(await PreparedAsync());
    var id = (await _db.AssistantLookups.SingleAsync()).Id;

    (await _service.DeleteAsync(Bob, id)).Should().BeFalse();
    (await _db.AssistantLookups.CountAsync()).Should().Be(1);
  }

  [Fact]
  public async Task DeleteAsync_Own_RemovesTheLookupAndKeepsTheAnswer()
  {
    await SendAllAsync(await PreparedAsync());
    var id = (await _db.AssistantLookups.SingleAsync()).Id;

    (await _service.DeleteAsync(Ada, id)).Should().BeTrue();

    (await _db.AssistantLookups.CountAsync()).Should().Be(0);
    (await _db.AssistantMessages.CountAsync(m => m.Origin == MessageOrigins.Cloud)).Should().Be(1);
    _audit.Events.Should().Contain(e => e.Action == AssistantAuditActions.LookupDeleted && e.TargetId == id.ToString());
  }

  [Fact]
  public async Task OfferAsync_QuestionWithBothSwitchesOn_OffersByReplyOrigin()
  {
    await TurnOnAsync(Ada);

    (await _service.OfferAsync(_ada, "Does zinc help colds?", MessageOrigins.Model, false)).Should().Be(new LookupOfferDto(true, "question"));
    (await _service.OfferAsync(_ada, "Does zinc help colds?", MessageOrigins.Guard, false)).Should().Be(new LookupOfferDto(true, "refused"));
    (await _service.OfferAsync(_ada, "Does zinc help colds?", MessageOrigins.Template, false)).Should().Be(new LookupOfferDto(true, "model_off"));
  }

  [Fact]
  public async Task OfferAsync_RedFlagStatementOrSwitchOff_OffersNothing()
  {
    (await _service.OfferAsync(_ada, "Does zinc help colds?", MessageOrigins.Model, false)).Should().BeNull("the person switch is off");
    await TurnOnAsync(Ada);
    (await _service.OfferAsync(_ada, "What do I do about chest pain?", MessageOrigins.Guard, true)).Should().BeNull();
    (await _service.OfferAsync(_ada, "Had oats for breakfast.", MessageOrigins.Model, false)).Should().BeNull();
    _client.IsUsable = false;
    (await _service.OfferAsync(_ada, "Does zinc help colds?", MessageOrigins.Model, false)).Should().BeNull("the server switch is off");
  }
}
