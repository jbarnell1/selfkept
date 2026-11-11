using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// The journal: short notes about food, drink, symptoms, training and sleep, in the words of the
/// person writing plus coarse tags (docs/body-design.md, "Journal"). Not a calorie counter.
/// </summary>
[Authorize]
public sealed class JournalController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private readonly IJournalService _journal;

  public JournalController(IJournalService journal) => _journal = journal;

  /// <summary>Newest first. Defaults to the last fourteen days. Needs the check-ins share for someone else.</summary>
  [HttpGet]
  public async Task<IActionResult> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _journal.ListAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), from, to, ct), NotShared);

  /// <summary>The tags the app offers as chips, grouped by kind.</summary>
  [HttpGet("tags")]
  public IActionResult Tags() => Ok(_journal.TagGroups());

  [HttpPost]
  public async Task<IActionResult> Create([FromBody] CreateJournalEntryRequest request, CancellationToken ct) =>
    Ok(await _journal.CreateAsync(CurrentActor(), request, ct));

  [HttpDelete("{id:guid}")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
    await _journal.DeleteAsync(CurrentActor(), id, ct) ? NoContent() : NotFoundProblem("No such journal entry.");
}
