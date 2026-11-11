using Microsoft.Extensions.Logging;
using SelfKept.Core.DTOs;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// Saves what the capture found: day log drafts through IJournalService.SaveDraftAsync (with the
/// message id, so Undo can take them back) and durable facts through ai_memory. A draft the
/// journal refuses is skipped and logged without its content, so one odd sentence never blocks
/// the reply.
/// </summary>
public sealed class CaptureSaver
{
  public const string MemorySource = "In-app assistant";

  private readonly IJournalService _journal;
  private readonly IAiMemoryService _memory;
  private readonly ILogger<CaptureSaver> _logger;

  public CaptureSaver(IJournalService journal, IAiMemoryService memory, ILogger<CaptureSaver> logger)
  {
    _journal = journal;
    _memory = memory;
    _logger = logger;
  }

  public async Task<(List<JournalEntryDto> Entries, List<AiMemoryDto> Remembered)> SaveAsync(
    Actor actor, CaptureResult result, string source, Guid messageId, CancellationToken ct)
  {
    var entries = new List<JournalEntryDto>();
    foreach (var draft in result.Drafts)
    {
      try
      {
        entries.Add(await _journal.SaveDraftAsync(actor, new JournalDraft(draft.Kind, draft.Text, draft.Tags, draft.Portion, draft.At, source, messageId), ct));
      }
      catch (DomainValidationException ex)
      {
        _logger.LogWarning("A captured {Kind} entry was not saved: {Reason}", draft.Kind, ex.Message);
      }
    }
    var remembered = new List<AiMemoryDto>();
    foreach (var fact in result.Facts)
    {
      try
      {
        // display_text is a plain column, so it stays empty: the value is the encrypted one.
        remembered.Add(await _memory.RememberAsync(actor.PersonId, fact.Type, fact.Key, fact.Value, null, MemorySource, ct));
      }
      catch (DomainValidationException ex)
      {
        _logger.LogWarning("A remembered fact was not saved: {Reason}", ex.Message);
      }
    }
    return (entries, remembered);
  }

  public static List<CaptureRefDto> Refs(IEnumerable<JournalEntryDto> entries, IEnumerable<AiMemoryDto> remembered) =>
    entries.Select(e => new CaptureRefDto(AssistantMeta.Journal, e.Id, e.Kind, e.Tags))
      .Concat(remembered.Select(m => new CaptureRefDto(AssistantMeta.Memory, m.Id, m.Type, [m.Key])))
      .ToList();
}
