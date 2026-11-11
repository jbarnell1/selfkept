using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.DTOs;
using SelfKept.Core.Interfaces;

namespace SelfKept.Api.Controllers;

/// <summary>
/// Medicines and supplements a person takes, with brand, product and what is in it
/// (docs/body-design.md, "Medicines and supplements"). A record, never advice.
/// </summary>
[Authorize]
public sealed class MedicationsController : BaseController
{
  private const string NotShared = "No such person, or they have not shared this with you.";

  private readonly IMedicationService _medications;

  public MedicationsController(IMedicationService medications) => _medications = medications;

  /// <summary>Current items first. Needs the labs share for someone else.</summary>
  [HttpGet]
  public async Task<IActionResult> List([FromQuery] Guid? personId, CancellationToken ct) =>
    HandleServiceResponse(await _medications.ListAsync(GetCurrentPersonId(), personId ?? GetCurrentPersonId(), ct), NotShared);

  [HttpPost]
  public async Task<IActionResult> Create([FromBody] SaveMedicationRequest request, CancellationToken ct) =>
    Ok(await _medications.CreateAsync(CurrentActor(), request, ct));

  [HttpPut("{id:guid}")]
  public async Task<IActionResult> Update(Guid id, [FromBody] SaveMedicationRequest request, CancellationToken ct) =>
    HandleServiceResponse(await _medications.UpdateAsync(CurrentActor(), id, request, ct), "No such item.");

  [HttpDelete("{id:guid}")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
    await _medications.DeleteAsync(CurrentActor(), id, ct) ? NoContent() : NotFoundProblem("No such item.");
}
