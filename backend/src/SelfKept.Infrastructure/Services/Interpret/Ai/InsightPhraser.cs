using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Security;

namespace SelfKept.Infrastructure.Services.Interpret.Ai;

/// <summary>
/// Asks the model to reword templated facts into one calm paragraph, validates the answer and
/// records the call in ai_call (never the text). Any failure means the templated text ships.
/// </summary>
public sealed class InsightPhraser : IInsightPhraser
{
  public const string SystemPrompt =
    "You rewrite short health notes for a personal, self-hosted health app. " +
    "Rewrite the user's note as one calm paragraph of at most three sentences, in plain second person. " +
    "Rules you must follow: keep every fact and number exactly as given; do not add any number, date, range or unit that is not in the note; " +
    "do not add advice, causes or next steps beyond what the note already says; never use the words diagnose or diagnosis and never say the person has a condition; " +
    "no medical jargon, no exclamation marks, no bullet points, no headings, no em dashes or en dashes; use only plain hyphens and commas. " +
    "Output only the rewritten paragraph.";

  private static readonly ChatOptions Options = new(Temperature: 0.2, MaxTokens: 240);

  private readonly IChatModel _model;
  private readonly IAiCallRepository _calls;
  private readonly TimeProvider _clock;
  private readonly ILogger<InsightPhraser> _logger;

  public InsightPhraser(IChatModel model, IAiCallRepository calls, TimeProvider clock, ILogger<InsightPhraser> logger)
  {
    _model = model;
    _calls = calls;
    _clock = clock;
    _logger = logger;
  }

  public async Task<string?> PhraseAsync(Guid personId, string purpose, string templated, CancellationToken ct = default)
  {
    if (!_model.Enabled || string.IsNullOrWhiteSpace(templated)) return null;

    var user = "Note:\n" + templated.Trim();
    var call = new AiCall
    {
      Id = Guid.CreateVersion7(),
      PersonId = personId,
      Purpose = purpose,
      PromptHash = Hash(SystemPrompt + "\n" + user),
      CreatedAt = _clock.GetUtcNow().UtcDateTime,
    };
    var watch = Stopwatch.StartNew();
    string? result = null;
    try
    {
      var completion = await _model.CompleteAsync(SystemPrompt, user, Options, ct);
      call.PromptTokens = completion.PromptTokens;
      call.CompletionTokens = completion.CompletionTokens;
      var verdict = PhraserValidator.Validate(templated, completion.Content);
      if (verdict.IsValid)
      {
        call.Ok = true;
        result = verdict.Text;
      }
      else
      {
        call.Ok = false;
        call.Error = "rejected: " + verdict.Reason;
      }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      call.Ok = false;
      call.Error = ex is OperationCanceledException ? "timeout" : ex.GetType().Name + (ex is HttpRequestException { StatusCode: { } code } ? $" {(int)code}" : string.Empty);
      _logger.LogWarning("Model call for {Purpose} failed: {Error}", purpose, call.Error);
    }
    finally
    {
      call.LatencyMs = (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds);
      await _calls.RecordAsync(call, ct);
    }
    return result;
  }

  public static string Hash(string text) => TokenCrypto.Sha256Hex(text);
}
