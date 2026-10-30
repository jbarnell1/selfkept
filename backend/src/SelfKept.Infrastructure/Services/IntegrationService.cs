using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Oura;

namespace SelfKept.Infrastructure.Services;

/// <summary>
/// Server-wide integrations an admin sets up from the app, so a first install needs no file
/// editing. Today that is the Oura application. Every change is audited without its values.
/// </summary>
public sealed class IntegrationService : IIntegrationService
{
  private const string EnvironmentWins = "The server's environment sets the Oura application. Change it in the .env file.";

  private readonly IHouseholdRepository _households;
  private readonly OuraAppCredentials _oura;
  private readonly OuraSettings _ouraSettings;
  private readonly IAuditService _audit;

  public IntegrationService(IHouseholdRepository households, OuraAppCredentials oura, OuraSettings ouraSettings, IAuditService audit)
  {
    _households = households;
    _oura = oura;
    _ouraSettings = ouraSettings;
    _audit = audit;
  }

  public async Task<OuraAppDto> GetOuraAppAsync(Guid userId, CancellationToken ct = default)
  {
    await RequireAdminAsync(userId, ct);
    return await OuraAppAsync(ct);
  }

  public async Task<OuraAppDto> SaveOuraAppAsync(Guid userId, SaveOuraAppRequest request, RequestInfo info, CancellationToken ct = default)
  {
    var householdId = await RequireAdminAsync(userId, ct);
    if ((await _oura.ResolveAsync(ct)).Source == CredentialSource.Environment)
      throw new ConflictException(EnvironmentWins);

    var clientId = Single(request.ClientId, "client id");
    var clientSecret = Single(request.ClientSecret, "client secret");
    await _oura.SaveAsync(clientId, clientSecret, userId, ct);
    await RecordAsync(userId, householdId, "saved", info, ct);
    return await OuraAppAsync(ct);
  }

  public async Task<OuraAppDto> ClearOuraAppAsync(Guid userId, RequestInfo info, CancellationToken ct = default)
  {
    var householdId = await RequireAdminAsync(userId, ct);
    if ((await _oura.ResolveAsync(ct)).Source == CredentialSource.Environment)
      throw new ConflictException(EnvironmentWins);

    if (await _oura.ClearAsync(ct))
      await RecordAsync(userId, householdId, "cleared", info, ct);
    return await OuraAppAsync(ct);
  }

  private async Task<OuraAppDto> OuraAppAsync(CancellationToken ct)
  {
    var (app, source) = await _oura.ResolveAsync(ct);
    var redirect = _ouraSettings.RedirectUri;
    return new OuraAppDto(app != null && redirect != null, source, app?.ClientId, redirect, OuraSettings.DeveloperUrl);
  }

  /// <summary>The admin's household id; throws for anyone else.</summary>
  private async Task<Guid> RequireAdminAsync(Guid userId, CancellationToken ct)
  {
    var membership = await _households.GetMembershipForUserAsync(userId, ct);
    if (membership?.Role != HouseholdRole.Admin)
      throw new ForbiddenException("Only the household admin can change the server's integrations.");
    return membership.HouseholdId;
  }

  /// <summary>Trimmed, and refused when it has spaces inside: a paste that caught two values or a line break.</summary>
  private static string Single(string value, string name)
  {
    var trimmed = value.Trim();
    if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace))
      throw new DomainValidationException($"The {name} is one word with no spaces. Copy it again from Oura.");
    return trimmed;
  }

  private Task RecordAsync(Guid userId, Guid householdId, string change, RequestInfo info, CancellationToken ct) =>
    _audit.RecordAsync(new AuditEvent(
      AuditActions.IntegrationChanged, userId, householdId, "integration", "oura", new { change }, info.IpAddress), ct);
}
