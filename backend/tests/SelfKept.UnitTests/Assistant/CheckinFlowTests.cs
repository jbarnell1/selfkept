using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services;
using SelfKept.Infrastructure.Services.Assistant;
using SelfKept.UnitTests.Collect;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

/// <summary>
/// The check-in flow over the real repositories on InMemory, with the day log save and the mood
/// check-in behind mocks so every save can be counted.
/// </summary>
public sealed class CheckinFlowTests : IAsyncDisposable
{
  private static readonly DateTime Now = new(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);
  private static readonly DateOnly Today = new(2026, 11, 10);

  private readonly SelfKeptDbContext _db = new(new DbContextOptionsBuilder<SelfKeptDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
  private readonly Guid _person = Guid.NewGuid();
  private readonly Mock<IJournalService> _journal = new();
  private readonly Mock<ICheckinService> _checkins = new();
  private readonly List<JournalDraft> _saved = [];
  private readonly CheckinFlowService _flow;

  public CheckinFlowTests()
  {
    _db.Persons.Add(new Person { Id = _person, DisplayName = "Ada", TimeZone = "UTC", CreatedAt = Now, UpdatedAt = Now });
    _db.SaveChanges();

    _journal.Setup(j => j.SaveDraftAsync(It.IsAny<Actor>(), It.IsAny<JournalDraft>(), It.IsAny<CancellationToken>()))
      .Callback<Actor, JournalDraft, CancellationToken>((_, d, _) => _saved.Add(d))
      .ReturnsAsync((Actor _, JournalDraft d, CancellationToken _) =>
        new JournalEntryDto(Guid.NewGuid(), d.At ?? Now, Today, d.Kind, d.Text, d.Tags, d.Portion, d.Source, d.MessageId));
    _checkins.Setup(c => c.CreateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CreateCheckinRequest>(), It.IsAny<RequestInfo>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((Guid _, Guid p, Guid? _, CreateCheckinRequest r, RequestInfo _, CancellationToken _) => new CheckinDto(Guid.NewGuid(), p, Now, r.Mood, null, []));

    var policy = new ShareGrantAccessPolicy(new ShareGrantRepository(_db, TimeProvider.System));
    var model = new Mock<IChatModel>();
    model.SetupGet(m => m.Enabled).Returns(false);
    var guard = new ReplyGuard();
    var clock = new TestClock(Now);
    var encryption = new EncryptionService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Encryption:Key"] = "unit-test-encryption-key-with-plenty-of-entropy-123456",
    }).Build());
    _flow = new CheckinFlowService(
      new AssistantRepository(_db),
      new PersonRepository(_db),
      new JournalRepository(_db, policy),
      new StrengthRepository(_db, policy),
      new WorkoutRepository(_db, policy),
      new DailySummaryRepository(_db, policy, new SeedCatalogue()),
      new JournalCapture(),
      new CaptureSaver(_journal.Object, Mock.Of<IAiMemoryService>(), NullLogger<CaptureSaver>.Instance),
      _checkins.Object,
      guard,
      new ModelRewording(model.Object, guard, Mock.Of<IAiCallRepository>(), clock, NullLogger<ModelRewording>.Instance),
      encryption,
      Mock.Of<IAuditService>(),
      clock);
  }

  public ValueTask DisposeAsync() => _db.DisposeAsync();

  private Actor Actor => new(Guid.NewGuid(), _person, null, new RequestInfo(null));

  /// <summary>Puts the given kinds on today's log so the check-in does not ask about them.</summary>
  private async Task LoggedAsync(params string[] kinds)
  {
    foreach (var kind in kinds)
    {
      _db.JournalEntries.Add(new JournalEntry { Id = Guid.NewGuid(), PersonId = _person, At = Now.AddHours(-8), Day = Today, Kind = kind, Tags = [], CreatedAt = Now });
    }
    await _db.SaveChangesAsync();
  }

  private Task<CheckinTurnDto> SayAsync(string? answer) => _flow.TurnAsync(Actor, new CheckinTurnRequest(answer, null));

  [Fact]
  public async Task TurnAsync_OffTopicAnswerWithAMeal_SavesItAndAsksTheMoodAgain()
  {
    await LoggedAsync(JournalKinds.Food, JournalKinds.Training, JournalKinds.Drink, JournalKinds.Symptom);
    (await SayAsync(null)).Topic.Should().Be(CheckinTopics.Mood);

    var turn = await SayAsync("I had salmon and rice for dinner");

    _saved.Should().ContainSingle().Which.Kind.Should().Be(JournalKinds.Food);
    _saved[0].Source.Should().Be(JournalSources.Checkin);
    _saved[0].MessageId.Should().Be(turn.UserMessageId, "the capture hangs on the person's message, so it can be undone from there");
    turn.Captured.Should().ContainSingle();
    turn.Topic.Should().Be(CheckinTopics.Mood, "the mood was not answered");
    turn.Reply.Should().Be(AssistantTemplates.Join(AssistantTemplates.OffTopicSaved([JournalKinds.Food], false), AssistantTemplates.Question(CheckinTopics.Mood)));
    turn.Done.Should().NotContain(CheckinTopics.Mood);
    _checkins.Verify(c => c.CreateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CreateCheckinRequest>(), It.IsAny<RequestInfo>(), It.IsAny<CancellationToken>()), Times.Never);

    var message = await _db.AssistantMessages.SingleAsync(m => m.Id == turn.UserMessageId);
    AssistantMeta.From(message.Meta).Captures.Should().ContainSingle().Which.Id.Should().Be(turn.Captured[0].Id);
  }

  [Fact]
  public async Task TurnAsync_ARunGivenForEating_IsSavedAsTrainingAndEatingIsAskedAgain()
  {
    (await SayAsync(null)).Topic.Should().Be(CheckinTopics.Eating);

    var turn = await SayAsync("Went for a 40 minute run after work, legs felt good");

    _saved.Should().ContainSingle().Which.Kind.Should().Be(JournalKinds.Training, "a run is not a meal");
    turn.Topic.Should().Be(CheckinTopics.Eating, "the eating question was not answered");
    turn.Reply.Should().Be(AssistantTemplates.Join(AssistantTemplates.OffTopicSaved([JournalKinds.Training], false), AssistantTemplates.Question(CheckinTopics.Eating)));
    turn.Done.Should().Contain(CheckinTopics.Training, "the run is on the log, so training is not asked");
  }

  [Fact]
  public async Task TurnAsync_OffTopicAnswerWithNothingToKeep_GetsTheRetryText()
  {
    await LoggedAsync(JournalKinds.Food, JournalKinds.Training, JournalKinds.Drink, JournalKinds.Symptom);
    await SayAsync(null);

    var turn = await SayAsync("hmm");

    _saved.Should().BeEmpty();
    turn.Captured.Should().BeEmpty();
    turn.Topic.Should().Be(CheckinTopics.Mood);
    turn.Reply.Should().Be(AssistantTemplates.MoodRetry);
  }

  [Fact]
  public async Task TurnAsync_OnTopicAnswer_IsSavedOnce()
  {
    await LoggedAsync(JournalKinds.Training, JournalKinds.Drink, JournalKinds.Symptom);
    (await SayAsync(null)).Topic.Should().Be(CheckinTopics.Eating);

    var turn = await SayAsync("Salmon, rice and greens");

    _saved.Should().ContainSingle().Which.Kind.Should().Be(JournalKinds.Food);
    turn.Captured.Should().ContainSingle();
    turn.Done.Should().Equal(CheckinTopics.Eating);
    turn.Topic.Should().Be(CheckinTopics.Mood);
  }

  [Fact]
  public async Task TurnAsync_AnswerThatCoversALaterTopic_DoesNotAskIt()
  {
    await LoggedAsync(JournalKinds.Training, JournalKinds.Symptom);
    (await SayAsync(null)).Topic.Should().Be(CheckinTopics.Eating);

    var turn = await SayAsync("Salmon and rice, and a glass of wine");

    _saved.Select(d => d.Kind).Should().BeEquivalentTo([JournalKinds.Food, JournalKinds.Drink]);
    turn.Done.Should().BeEquivalentTo([CheckinTopics.Eating, CheckinTopics.Drinks]);
    turn.Topic.Should().Be(CheckinTopics.Mood, "the wine already answered the drinks question");
  }
}
