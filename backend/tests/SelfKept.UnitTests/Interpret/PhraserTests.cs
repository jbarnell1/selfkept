using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret.Ai;
using Xunit;

namespace SelfKept.UnitTests.Interpret;

public class PhraserValidatorTests
{
  private const string Input = "You slept 7 h 12 min last night, a little short of your usual. HRV is below your usual range. Weight has moved down by about 1.6 kg over the month.";

  [Fact]
  public void Validate_AllowedRestatement_IsAccepted()
  {
    var output = "Last night you slept 7 hours and 12 minutes, a little under your usual, and HRV sits below its usual range. Over the month your weight is down by about 1.6 kg.";

    var result = PhraserValidator.Validate(Input, output);

    result.IsValid.Should().BeTrue(result.Reason);
    result.Text.Should().Be(output);
  }

  [Fact]
  public void Validate_InventedNumber_IsRejected()
  {
    var result = PhraserValidator.Validate(Input, "You slept 7 h 12 min, about 45 minutes short of your usual 8 hours.");

    result.IsValid.Should().BeFalse();
    result.Reason.Should().Contain("45");
  }

  [Fact]
  public void Validate_SameNumberInAnotherSpelling_IsAccepted()
  {
    PhraserValidator.Validate("Steps were 1,200 and weight 70.50 kg.", "You walked 1200 steps and weigh 70.5 kg.").IsValid.Should().BeTrue();
  }

  [Fact]
  public void Validate_TooLong_IsRejected()
  {
    var output = string.Join(" ", Enumerable.Repeat("HRV is below your usual range.", 20));
    var result = PhraserValidator.Validate(Input, output);

    result.IsValid.Should().BeFalse();
    result.Reason.Should().Be("too long");
  }

  [Fact]
  public void Validate_DiagnosticLanguage_IsRejected()
  {
    PhraserValidator.Validate(Input, "This is a diagnosis of overtraining.").IsValid.Should().BeFalse();
    PhraserValidator.Validate(Input, "It looks like you have a viral infection.").IsValid.Should().BeFalse();
    PhraserValidator.Validate(Input, "It looks like you may have the flu.").IsValid.Should().BeFalse();
    PhraserValidator.Validate(Input, "Disease names alone are fine: the note mentions nothing about illness.").IsValid.Should().BeTrue();
    PhraserValidator.Validate(Input, "You have had a short night.").IsValid.Should().BeTrue();
  }

  [Fact]
  public void Validate_EmDash_IsRejected()
  {
    PhraserValidator.Validate(Input, "HRV is below your usual range \u2014 a short night.").IsValid.Should().BeFalse();
  }

  [Fact]
  public void Validate_Empty_IsRejected()
  {
    PhraserValidator.Validate(Input, "   ").IsValid.Should().BeFalse();
    PhraserValidator.Validate(Input, null).IsValid.Should().BeFalse();
  }
}

public class InsightPhraserTests
{
  private sealed class FakeModel : IChatModel
  {
    public FakeModel(bool enabled, Func<string, string>? answer = null)
    {
      Enabled = enabled;
      Answer = answer ?? (_ => string.Empty);
    }

    public bool Enabled { get; }
    public string ModelName => "fake";
    public Func<string, string> Answer { get; }
    public int Calls { get; private set; }

    public Task<ChatCompletion> CompleteAsync(string system, string user, ChatOptions options, CancellationToken ct = default)
    {
      Calls++;
      return Task.FromResult(new ChatCompletion(Answer(user), 100, 20));
    }
  }

  private sealed class RecordingCalls : IAiCallRepository
  {
    public List<AiCall> Calls { get; } = [];

    public Task RecordAsync(AiCall call, CancellationToken ct = default)
    {
      Calls.Add(call);
      return Task.CompletedTask;
    }
  }

  private static readonly TestClock Clock = new(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));

  [Fact]
  public async Task Phrase_ValidRewording_IsReturned_AndTheCallIsRecordedWithoutText()
  {
    var model = new FakeModel(true, _ => "Last night you slept 7 h 12 min, a little under your usual.");
    var calls = new RecordingCalls();
    var phraser = new InsightPhraser(model, calls, Clock, NullLogger<InsightPhraser>.Instance);

    var text = await phraser.PhraseAsync(Guid.NewGuid(), AiPurposes.Today, "You slept 7 h 12 min last night, a little short of your usual.");

    text.Should().Be("Last night you slept 7 h 12 min, a little under your usual.");
    var call = calls.Calls.Should().ContainSingle().Which;
    call.Ok.Should().BeTrue();
    call.Purpose.Should().Be("today");
    call.PromptTokens.Should().Be(100);
    call.CompletionTokens.Should().Be(20);
    call.PromptHash.Should().HaveLength(64);
    call.Error.Should().BeNull();
  }

  [Fact]
  public async Task Phrase_InventedNumber_FallsBackToNull_AndRecordsTheRejection()
  {
    var model = new FakeModel(true, _ => "You slept 7 h 12 min, which is 48 minutes short.");
    var calls = new RecordingCalls();
    var phraser = new InsightPhraser(model, calls, Clock, NullLogger<InsightPhraser>.Instance);

    var text = await phraser.PhraseAsync(Guid.NewGuid(), AiPurposes.Insight, "You slept 7 h 12 min last night.");

    text.Should().BeNull();
    calls.Calls.Single().Ok.Should().BeFalse();
    calls.Calls.Single().Error.Should().StartWith("rejected");
  }

  [Fact]
  public async Task Phrase_ModelThrows_FallsBackToNull_AndRecordsTheFailure()
  {
    var model = new FakeModel(true, _ => throw new HttpRequestException("boom", null, System.Net.HttpStatusCode.BadGateway));
    var calls = new RecordingCalls();
    var phraser = new InsightPhraser(model, calls, Clock, NullLogger<InsightPhraser>.Instance);

    (await phraser.PhraseAsync(Guid.NewGuid(), AiPurposes.Insight, "HRV is back inside your normal range.")).Should().BeNull();
    calls.Calls.Single().Ok.Should().BeFalse();
    calls.Calls.Single().Error.Should().Contain("502");
  }

  [Fact]
  public async Task Phrase_ModelDisabled_NeverCallsIt_AndRecordsNothing()
  {
    var model = new FakeModel(false, _ => "anything");
    var calls = new RecordingCalls();
    var phraser = new InsightPhraser(model, calls, Clock, NullLogger<InsightPhraser>.Instance);

    (await phraser.PhraseAsync(Guid.NewGuid(), AiPurposes.Today, "HRV is back inside your normal range.")).Should().BeNull();
    model.Calls.Should().Be(0);
    calls.Calls.Should().BeEmpty();
  }

  [Fact]
  public void Endpoint_AppendsChatCompletions_WithOrWithoutTrailingSlash()
  {
    OpenAiCompatibleChatModel.Endpoint("http://localhost:11434/v1/").Should().Be(new Uri("http://localhost:11434/v1/chat/completions"));
    OpenAiCompatibleChatModel.Endpoint("http://localhost:11434/v1").Should().Be(new Uri("http://localhost:11434/v1/chat/completions"));
  }

  [Fact]
  public void SystemPrompt_ForbidsNewNumbersAdviceDiagnosisAndDashes()
  {
    InsightPhraser.SystemPrompt.Should().Contain("do not add any number").And.Contain("advice").And.Contain("diagnos").And.Contain("em dashes");
    InsightPhraser.SystemPrompt.Should().NotContain("\u2014");
  }
}
