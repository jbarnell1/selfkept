using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services;
using SelfKept.Infrastructure.Services.Assistant;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

public class NudgeTests
{
  private const string Url = "https://ntfy.home.example/secret-topic-abc";
  private static readonly Guid PersonId = Guid.NewGuid();

  private sealed class FakeSender : INtfySender
  {
    public List<(string Url, string Title, string Message)> Sent { get; } = [];
    public string? Fail { get; set; }

    public Task<string?> SendAsync(string url, string? token, string title, string message, CancellationToken ct = default)
    {
      if (Fail != null) return Task.FromResult<string?>(Fail);
      Sent.Add((url, title, message));
      return Task.FromResult<string?>(null);
    }
  }

  private sealed class FakeNudges : INudgeRepository
  {
    public List<NudgeSetting> Settings { get; } = [];
    public List<NudgeLog> Logs { get; } = [];

    public Task<NudgeSetting?> GetOwnAsync(Guid personId, CancellationToken ct = default) => Task.FromResult(Settings.FirstOrDefault(s => s.PersonId == personId));
    public Task SaveOwnAsync(NudgeSetting setting, CancellationToken ct = default) { Settings.Add(setting); return Task.CompletedTask; }
    public Task<IReadOnlyList<NudgeSetting>> ListEnabledForJobAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<NudgeSetting>>(Settings.Where(s => s.Enabled).ToList());
    public Task<bool> HasOwnLogAsync(Guid personId, string kind, DateOnly day, CancellationToken ct = default) => Task.FromResult(Logs.Any(l => l.PersonId == personId && l.Kind == kind && l.Day == day));
    public Task<bool> TryAddOwnLogAsync(NudgeLog log, CancellationToken ct = default)
    {
      if (Logs.Any(l => l.PersonId == log.PersonId && l.Kind == log.Kind && l.Day == log.Day)) return Task.FromResult(false);
      Logs.Add(log);
      return Task.FromResult(true);
    }
    public Task<DateOnly?> LastOwnLogDayAsync(Guid personId, string kind, CancellationToken ct = default) => Task.FromResult(Logs.Select(l => (DateOnly?)l.Day).Max());
  }

  private sealed class ListLogger : ILogger<NudgeJob>
  {
    public List<string> Lines { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
      Lines.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
  }

  private static EncryptionService Encryption() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
  {
    ["Encryption:Key"] = "unit-test-encryption-key-0a1b2c3d4e5f60718293a4b5c6d7e8f9",
  }).Build());

  private static (NudgeJob Job, FakeSender Sender, FakeNudges Nudges, ListLogger Logger, TestClock Clock, Mock<IAssistantRepository> Conversations) Setup(DateTimeOffset now)
  {
    var encryption = Encryption();
    var nudges = new FakeNudges();
    nudges.Settings.Add(new NudgeSetting { PersonId = PersonId, Enabled = true, NtfyUrlCipher = encryption.Encrypt(Url), EveningTime = new TimeOnly(20, 0) });
    var persons = new Mock<IPersonRepository>();
    persons.Setup(p => p.GetByIdAsync(PersonId, It.IsAny<CancellationToken>())).ReturnsAsync(new Person { Id = PersonId, TimeZone = "UTC", DisplayName = "Ada" });
    var conversations = new Mock<IAssistantRepository>();
    var sender = new FakeSender();
    var services = new ServiceCollection()
      .AddSingleton<INudgeRepository>(nudges)
      .AddSingleton(persons.Object)
      .AddSingleton(conversations.Object)
      .AddSingleton<INtfySender>(sender)
      .AddSingleton<IEncryptionService>(encryption)
      .BuildServiceProvider();
    var clock = new TestClock(now);
    var logger = new ListLogger();
    return (new NudgeJob(services.GetRequiredService<IServiceScopeFactory>(), clock, logger), sender, nudges, logger, clock, conversations);
  }

  [Fact]
  public async Task TickAsync_TwiceInTheEvening_SendsOneNudge()
  {
    var (job, sender, nudges, _, clock, _) = Setup(new DateTimeOffset(2026, 11, 10, 20, 5, 0, TimeSpan.Zero));

    (await job.TickAsync(default)).Should().Be(1);
    clock.Advance(TimeSpan.FromMinutes(30));
    (await job.TickAsync(default)).Should().Be(0);

    sender.Sent.Should().ContainSingle();
    nudges.Logs.Should().ContainSingle().Which.Day.Should().Be(new DateOnly(2026, 11, 10));
  }

  [Fact]
  public async Task TickAsync_BeforeTheEveningTime_SendsNothing()
  {
    var (job, sender, _, _, _, _) = Setup(new DateTimeOffset(2026, 11, 10, 19, 55, 0, TimeSpan.Zero));

    (await job.TickAsync(default)).Should().Be(0);
    sender.Sent.Should().BeEmpty();
  }

  [Fact]
  public async Task TickAsync_CheckinAlreadyDone_SendsNothing()
  {
    var (job, sender, _, _, _, conversations) = Setup(new DateTimeOffset(2026, 11, 10, 20, 5, 0, TimeSpan.Zero));
    var done = CheckinMachine.Start([]);
    conversations.Setup(c => c.GetOwnCheckinAsync(PersonId, new DateOnly(2026, 11, 10), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new AssistantConversation { PersonId = PersonId, Kind = ConversationKinds.Checkin, State = done.ToJson() });

    (await job.TickAsync(default)).Should().Be(0);
    sender.Sent.Should().BeEmpty();
  }

  [Fact]
  public async Task TickAsync_Text_HasNoDigits()
  {
    var (job, sender, _, _, _, _) = Setup(new DateTimeOffset(2026, 11, 10, 20, 5, 0, TimeSpan.Zero));

    await job.TickAsync(default);

    var (_, title, message) = sender.Sent.Single();
    (title + message).Should().NotContainAny("0", "1", "2", "3", "4", "5", "6", "7", "8", "9");
    (AssistantTemplates.NudgeTestTitle + AssistantTemplates.NudgeTestMessage).Any(char.IsDigit).Should().BeFalse();
  }

  [Fact]
  public async Task TickAsync_SendFails_LogsWithoutTheUrl_AndRetriesNextTick()
  {
    var (job, sender, nudges, logger, _, _) = Setup(new DateTimeOffset(2026, 11, 10, 20, 5, 0, TimeSpan.Zero));
    sender.Fail = "The server answered 403.";

    (await job.TickAsync(default)).Should().Be(0);

    nudges.Logs.Should().BeEmpty();
    logger.Lines.Should().NotBeEmpty();
    logger.Lines.Should().NotContain(l => l.Contains("ntfy.home.example") || l.Contains("secret-topic"));

    sender.Fail = null;
    (await job.TickAsync(default)).Should().Be(1);
  }

  [Fact]
  public void DueDay_AfterTheWindow_IsNull()
  {
    NudgeSchedule.DueDay(new DateTime(2026, 11, 10, 23, 30, 0, DateTimeKind.Utc), TimeZoneInfo.Utc, new TimeOnly(20, 0)).Should().BeNull();
  }

  [Theory]
  [InlineData("ftp://ntfy.example/topic")]
  [InlineData("ntfy.example/topic")]
  [InlineData("https://user:pass@ntfy.example/topic")]
  [InlineData("https://ntfy.example/")]
  public void Validate_BadAddress_IsRefused(string url)
  {
    NudgeUrls.Validate(url).Should().NotBeNull();
  }

  [Fact]
  public void Mask_HidesTheTopic()
  {
    NudgeUrls.Mask(Url).Should().Be("https://ntfy.home.example/***");
  }
}
