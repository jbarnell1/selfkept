using SelfKept.Core.Enums;

namespace SelfKept.Infrastructure.Services.Oura;

/// <summary>The Oura API application this server signs in with.</summary>
public sealed record OuraApp(string ClientId, string ClientSecret);

/// <summary>
/// Finds the Oura application: Oura:ClientId and Oura:ClientSecret from the environment when both
/// are set, otherwise the pair an admin saved from the app. Either way the server also needs its
/// own address (App:BaseUrl), because Oura sends the browser back to it.
/// </summary>
public sealed class OuraAppCredentials
{
  public const string ClientIdKey = "oura.client_id";
  public const string ClientSecretKey = "oura.client_secret";
  private static readonly string[] Keys = [ClientIdKey, ClientSecretKey];

  private readonly OuraSettings _settings;
  private readonly ServerSettingStore _store;

  public OuraAppCredentials(OuraSettings settings, ServerSettingStore store)
  {
    _settings = settings;
    _store = store;
  }

  /// <summary>The application and where it came from, whether or not the redirect address is known.</summary>
  public async Task<(OuraApp? App, CredentialSource Source)> ResolveAsync(CancellationToken ct = default)
  {
    if (_settings.EnvironmentApp is { } fromEnvironment)
      return (fromEnvironment, CredentialSource.Environment);
    var stored = await _store.GetAsync(Keys, ct);
    return stored.TryGetValue(ClientIdKey, out var id) && stored.TryGetValue(ClientSecretKey, out var secret)
      ? (new OuraApp(id, secret), CredentialSource.App)
      : (null, CredentialSource.None);
  }

  /// <summary>The application when Oura can actually be used: credentials and a redirect address.</summary>
  public async Task<OuraApp?> GetUsableAsync(CancellationToken ct = default) =>
    _settings.RedirectUri == null ? null : (await ResolveAsync(ct)).App;

  public Task SaveAsync(string clientId, string clientSecret, Guid updatedBy, CancellationToken ct = default) =>
    _store.SetAsync(new Dictionary<string, string> { [ClientIdKey] = clientId, [ClientSecretKey] = clientSecret }, updatedBy, ct);

  public Task<bool> ClearAsync(CancellationToken ct = default) => _store.RemoveAsync(Keys, ct);
}
