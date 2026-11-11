using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret.Ai;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// One short model call that rewords a templated line or note, checked by the guard. Every call
/// is written to ai_call (purpose, prompt hash, tokens, latency, verdict) and never with its text.
/// Null means the template ships: the model is off, failed, or its answer was refused.
/// </summary>
public sealed class ModelRewording
{
  private readonly IChatModel _model;
  private readonly IReplyGuard _guard;
  private readonly IAiCallRepository _calls;
  private readonly TimeProvider _clock;
  private readonly ILogger<ModelRewording> _logger;

  public ModelRewording(IChatModel model, IReplyGuard guard, IAiCallRepository calls, TimeProvider clock, ILogger<ModelRewording> logger)
  {
    _model = model;
    _guard = guard;
    _calls = calls;
    _clock = clock;
    _logger = logger;
  }

  public bool Enabled => _model.Enabled;

  /// <param name="asLine">True for a one-line encouragement (CheckLine), false for a paragraph (CheckReply).</param>
  public async Task<string?> RewordAsync(Guid personId, string purpose, string system, string templated, GuardContext context, bool asLine, int maxTokens, CancellationToken ct)
  {
    if (!_model.Enabled || string.IsNullOrWhiteSpace(templated)) return null;
    var call = NewCall(personId, purpose, system + "\n" + templated);
    var watch = Stopwatch.StartNew();
    string? result = null;
    try
    {
      var completion = await _model.CompleteAsync(system, templated, new ChatOptions(0.3, maxTokens), ct);
      call.PromptTokens = completion.PromptTokens;
      call.CompletionTokens = completion.CompletionTokens;
      var verdict = asLine ? _guard.CheckLine(completion.Content, context) : _guard.CheckReply(completion.Content, context);
      call.Ok = verdict.Ok;
      call.Error = verdict.Ok ? null : "rejected: " + verdict.Reason;
      result = verdict.Ok ? verdict.Text : null;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      call.Ok = false;
      call.Error = Describe(ex);
      _logger.LogWarning("Model call for {Purpose} failed: {Error}", purpose, call.Error);
    }
    finally
    {
      call.LatencyMs = (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds);
      await _calls.RecordAsync(call, CancellationToken.None);
    }
    return result;
  }

  public AiCall NewCall(Guid? personId, string purpose, string prompt) => new()
  {
    Id = Guid.CreateVersion7(),
    PersonId = personId,
    Purpose = purpose,
    PromptHash = InsightPhraser.Hash(prompt),
    CreatedAt = _clock.GetUtcNow().UtcDateTime,
  };

  public static string Describe(Exception ex) =>
    ex is OperationCanceledException ? "timeout"
    : ex.GetType().Name + (ex is HttpRequestException { StatusCode: { } code } ? $" {(int)code}" : string.Empty);
}
