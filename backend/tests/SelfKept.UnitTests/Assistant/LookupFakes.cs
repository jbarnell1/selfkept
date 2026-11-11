using Microsoft.Extensions.Logging;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.UnitTests.Assistant;

/// <summary>The cloud lookup client, without the network: a set result, optionally held until released.</summary>
public sealed class FakeCloudLookupClient : ICloudLookupClient
{
  public const string AnswerText = "Magnesium glycinate may modestly improve sleep quality in adults, but the evidence is mixed and the trials are small.";

  public static readonly IReadOnlyList<LookupSourceDto> AnswerSources =
  [
    new("Magnesium and sleep: a review", "https://journals.example.org/magnesium-sleep"),
    new("Sleep supplements", "https://health.example.org/sleep"),
  ];

  public bool IsUsable { get; set; } = true;
  public string Provider => "Anthropic";
  public string Model => "claude-opus-5-5";
  public CloudLookupResult Result { get; set; } = new(CloudLookupOutcome.Answered, AnswerText, AnswerSources, "claude-opus-5-5", 1200, 300);
  public TaskCompletionSource? Gate { get; set; }
  public List<string> Asked { get; } = [];

  public async Task<CloudLookupResult> AskAsync(string text, CancellationToken ct = default)
  {
    Asked.Add(text);
    if (Gate != null) await Gate.Task.WaitAsync(ct);
    return Result;
  }
}

/// <summary>A local model that answers by the system prompt it is given.</summary>
public sealed class FakeLocalModel : IChatModel
{
  public bool Enabled { get; set; }
  public string ModelName => "fake-local";
  public Func<string, string, string> Answer { get; set; } = (_, _) => string.Empty;
  public int Calls { get; private set; }

  public Task<ChatCompletion> CompleteAsync(string system, string user, ChatOptions options, CancellationToken ct = default)
  {
    Calls++;
    return Task.FromResult(new ChatCompletion(Answer(system, user), 10, 10));
  }
}

public sealed class CapturingAudit : IAuditService
{
  public List<AuditEvent> Events { get; } = [];

  public Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default)
  {
    Events.Add(auditEvent);
    return Task.CompletedTask;
  }
}

public sealed class CapturingAiCalls : IAiCallRepository
{
  public List<AiCall> Calls { get; } = [];

  public Task RecordAsync(AiCall call, CancellationToken ct = default)
  {
    Calls.Add(call);
    return Task.CompletedTask;
  }
}

/// <summary>Keeps every formatted log line, so a test can check what was never written.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
  public List<string> Lines { get; } = [];

  public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
  {
    Lines.Add(formatter(state, exception) + (exception == null ? string.Empty : " " + exception));
  }
}
