using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Formatting;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Security;
using SelfKept.Infrastructure.Services.Connect;

namespace SelfKept.Infrastructure.Services.Oura;

/// <summary>
/// The Oura link (docs/connect-design.md, "Oura"): OAuth2 with PKCE, encrypted single-use refresh
/// tokens, and a pull that maps documents with <see cref="OuraMapper"/> and hands them to the same
/// ingest path the phones use, under a per-person "Oura" source. The first pull reaches back
/// <see cref="OuraSettings.BackfillDays"/> days; later pulls start two days before the last one,
/// because Oura revises a night once the app syncs.
/// </summary>
public sealed class OuraService : IOuraService
{
  public const string SourceDeviceName = "Oura ring";
  public const string SourcePlatform = "cloud";
  private const int IngestChunk = 4000;
  private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(15);
  private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(30);

  /// <summary>In-app paths the browser may return to after Oura; anything else means Sources.</summary>
  public static readonly IReadOnlySet<string> ReturnPaths = new HashSet<string>(StringComparer.Ordinal) { "/sources", "/welcome/steps" };
  private const string DefaultReturnPath = "/sources";

  private readonly IDbContextFactory<SelfKeptDbContext> _contextFactory;
  private readonly IOuraApi _api;
  private readonly OuraSettings _settings;
  private readonly OuraAppCredentials _credentials;
  private readonly IEncryptionService _encryption;
  private readonly IIngestService _ingest;
  private readonly IAuditService _audit;
  private readonly TimeProvider _clock;
  private readonly ILogger<OuraService> _logger;

  public OuraService(
    IDbContextFactory<SelfKeptDbContext> contextFactory,
    IOuraApi api,
    OuraSettings settings,
    OuraAppCredentials credentials,
    IEncryptionService encryption,
    IIngestService ingest,
    IAuditService audit,
    TimeProvider clock,
    ILogger<OuraService> logger)
  {
    _contextFactory = contextFactory;
    _api = api;
    _settings = settings;
    _credentials = credentials;
    _encryption = encryption;
    _ingest = ingest;
    _audit = audit;
    _clock = clock;
    _logger = logger;
  }

  public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
    await _credentials.GetUsableAsync(ct) != null;

  private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
  private DateOnly Today => DateOnly.FromDateTime(UtcNow);

  public async Task<OuraStatusDto> GetStatusAsync(Guid personId, CancellationToken ct = default)
  {
    var configured = await IsConfiguredAsync(ct);
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var link = await context.OuraConnections.AsNoTracking().FirstOrDefaultAsync(c => c.PersonId == personId, ct);
    if (link == null)
      return new OuraStatusDto(configured, false, null, null, null, 0);
    var count = await context.Sources.AsNoTracking().Where(s => s.Id == link.SourceId).Select(s => s.SampleCount).FirstOrDefaultAsync(ct);
    return new OuraStatusDto(configured, true, link.ConnectedAt, link.LastPullAt, link.LastError, count);
  }

  public async Task<OuraConnectDto> StartConnectAsync(Guid userId, Guid personId, string? returnPath = null, CancellationToken ct = default)
  {
    var app = await _credentials.GetUsableAsync(ct)
      ?? throw new DomainValidationException("Oura is not set up on this server.");

    var state = TokenCrypto.NewToken();
    var verifier = TokenCrypto.NewToken(48);
    var challenge = TokenCrypto.PkceChallenge(verifier);

    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    // Old round trips that were never finished go now; nothing else reads them.
    var now = UtcNow;
    context.OuraAuthStates.RemoveRange(await context.OuraAuthStates.Where(s => s.ExpiresAt < now).ToListAsync(ct));
    context.OuraAuthStates.Add(new OuraAuthState
    {
      StateHash = TokenCrypto.Sha256Hex(state),
      UserId = userId,
      PersonId = personId,
      VerifierCipher = _encryption.Encrypt(verifier),
      ReturnPath = returnPath != null && ReturnPaths.Contains(returnPath) ? returnPath : null,
      CreatedAt = UtcNow,
      ExpiresAt = UtcNow.Add(StateTtl),
    });
    await context.SaveChangesAsync(ct);

    var url = $"{_settings.AuthorizeUrl}?response_type=code&client_id={Uri.EscapeDataString(app.ClientId)}" +
              $"&redirect_uri={Uri.EscapeDataString(_settings.RedirectUri!)}&scope={Uri.EscapeDataString(_settings.Scope)}" +
              $"&state={state}&code_challenge={challenge}&code_challenge_method=S256";
    return new OuraConnectDto(url);
  }

  public async Task<string> CompleteConnectAsync(string? code, string? state, string? error, RequestInfo info, CancellationToken ct = default)
  {
    var app = await _credentials.GetUsableAsync(ct);
    if (app == null || string.IsNullOrWhiteSpace(state))
      return Outcome(null, connected: false);

    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var stateHash = TokenCrypto.Sha256Hex(state);
    var pending = await context.OuraAuthStates.FirstOrDefaultAsync(s => s.StateHash == stateHash, ct);
    if (pending == null)
      return Outcome(null, connected: false);
    context.OuraAuthStates.Remove(pending); // single use, whatever happens next
    await context.SaveChangesAsync(ct);
    var failed = Outcome(pending.ReturnPath, connected: false);
    if (pending.ExpiresAt < UtcNow || !string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code))
      return failed;

    OuraTokens tokens;
    try
    {
      tokens = await _api.ExchangeCodeAsync(app, code, _encryption.Decrypt(pending.VerifierCipher), ct);
    }
    catch (Exception ex) when (ex is OuraUnauthorizedException or HttpRequestException or OuraRateLimitedException)
    {
      _logger.LogWarning(ex, "Oura code exchange failed for person {PersonId}", pending.PersonId);
      return failed;
    }

    var source = await context.Sources.FirstOrDefaultAsync(s => s.PersonId == pending.PersonId && s.Kind == SourceKinds.Oura && s.DeletedAt == null, ct);
    if (source == null)
    {
      source = new Source
      {
        Id = Guid.CreateVersion7(),
        PersonId = pending.PersonId,
        Kind = SourceKinds.Oura,
        DeviceName = SourceDeviceName,
        Platform = SourcePlatform,
        Enabled = true,
        CreatedAt = UtcNow,
      };
      context.Sources.Add(source);
    }

    var link = await context.OuraConnections.FirstOrDefaultAsync(c => c.PersonId == pending.PersonId, ct);
    if (link == null)
    {
      link = new OuraConnection { PersonId = pending.PersonId, ConnectedAt = UtcNow };
      context.OuraConnections.Add(link);
    }
    link.UserId = pending.UserId;
    link.SourceId = source.Id;
    link.AccessCipher = _encryption.Encrypt(tokens.AccessToken);
    link.RefreshCipher = _encryption.Encrypt(tokens.RefreshToken);
    link.AccessExpiresAt = UtcNow.AddSeconds(tokens.ExpiresIn);
    link.Scope = tokens.Scope;
    link.LastError = null;
    link.NextPullAt = UtcNow; // the poller's next tick does the first pull
    await context.SaveChangesAsync(ct);

    var householdId = await context.Users.Where(u => u.Id == pending.UserId).Select(u => u.HouseholdId).FirstOrDefaultAsync(ct);
    await _audit.RecordAsync(new AuditEvent(AuditActions.OuraConnected, pending.UserId, householdId, "source", source.Id.ToString(), IpAddress: info.IpAddress), ct);
    return Outcome(pending.ReturnPath, connected: true);
  }

  /// <summary>The app address the browser lands on after Oura, with the result in the query.</summary>
  private string Outcome(string? returnPath, bool connected) =>
    $"{_settings.AppBaseUrl}{returnPath ?? DefaultReturnPath}?oura={(connected ? "connected" : "error")}";

  public async Task<bool> DisconnectAsync(Guid userId, Guid personId, Guid? householdId, RequestInfo info, CancellationToken ct = default)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var link = await context.OuraConnections.FirstOrDefaultAsync(c => c.PersonId == personId, ct);
    if (link == null)
      return false;

    var access = _encryption.TryDecrypt(link.AccessCipher);
    if (access != null)
    {
      try
      {
        await _api.RevokeAsync(access, ct);
      }
      catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
      {
        _logger.LogInformation("Oura revoke did not answer; the link is removed here anyway.");
      }
    }

    context.OuraConnections.Remove(link);
    // The readings already brought in stay; the source is marked removed so nothing new lands.
    var source = await context.Sources.FirstOrDefaultAsync(s => s.Id == link.SourceId, ct);
    if (source != null) source.DeletedAt = UtcNow;
    await context.SaveChangesAsync(ct);
    await _audit.RecordAsync(new AuditEvent(AuditActions.OuraDisconnected, userId, householdId, "source", link.SourceId.ToString(), IpAddress: info.IpAddress), ct);
    return true;
  }

  public async Task<bool> RequestPullAsync(Guid personId, CancellationToken ct = default)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var link = await context.OuraConnections.FirstOrDefaultAsync(c => c.PersonId == personId, ct);
    if (link == null)
      return false;
    link.NextPullAt = UtcNow;
    await context.SaveChangesAsync(ct);
    return true;
  }

  public async Task<int> PullDueAsync(CancellationToken ct = default)
  {
    var app = await _credentials.GetUsableAsync(ct);
    if (app == null)
      return 0;
    List<Guid> due;
    await using (var context = await _contextFactory.CreateDbContextAsync(ct))
    {
      var now = UtcNow;
      due = await context.OuraConnections.Where(c => c.NextPullAt != null && c.NextPullAt <= now).Select(c => c.PersonId).ToListAsync(ct);
    }
    var pulled = 0;
    foreach (var personId in due)
    {
      if (await PullAsync(app, personId, ct)) pulled++;
    }
    return pulled;
  }

  /// <summary>One person's pull. Never throws for an Oura-side problem: it is written to last_error instead.</summary>
  private async Task<bool> PullAsync(OuraApp app, Guid personId, CancellationToken ct)
  {
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var link = await context.OuraConnections.FirstOrDefaultAsync(c => c.PersonId == personId, ct);
    if (link == null)
      return false;

    try
    {
      var access = await AccessTokenAsync(app, context, link, ct);
      var start = link.LastPullThrough is { } through ? through.AddDays(-2) : Today.AddDays(-_settings.BackfillDays);
      var end = Today.AddDays(1);

      var samples = new List<SampleInput>();
      foreach (var collection in OuraMapper.Collections)
      {
        foreach (var document in await _api.ListAsync(access, collection, start, end, ct))
          samples.AddRange(OuraMapper.Map(collection, document));
      }

      var accepted = 0;
      var rejected = 0;
      foreach (var chunk in samples.Chunk(IngestChunk))
      {
        var result = await _ingest.IngestAsync(personId, new IngestBatchRequest(link.SourceId, $"oura:{Today.ToIso()}", chunk, null), ct);
        accepted += result?.Accepted ?? 0;
        rejected += result?.Rejected.Count ?? 0;
      }

      link.LastPullAt = UtcNow;
      link.LastPullThrough = Today;
      link.NextPullAt = UtcNow.Add(_settings.PollInterval);
      link.LastError = null;
      await context.SaveChangesAsync(ct);
      _logger.LogInformation("Oura pull for {PersonId}: {Read} samples read, {Accepted} new, {Rejected} not accepted ({Start} to {End})",
        personId, samples.Count, accepted, rejected, start, end);
      return true;
    }
    catch (OuraUnauthorizedException)
    {
      link.LastError = "Oura access was withdrawn. Connect Oura again to keep pulling.";
      link.NextPullAt = null;
      await context.SaveChangesAsync(ct);
      return false;
    }
    catch (ConflictException)
    {
      // The person switched the Oura source off under Sources: wait, do not hammer.
      link.LastError = "The Oura source is turned off under Sources.";
      link.NextPullAt = UtcNow.Add(_settings.PollInterval);
      await context.SaveChangesAsync(ct);
      return false;
    }
    catch (OuraRateLimitedException ex)
    {
      link.LastError = null;
      link.NextPullAt = UtcNow.Add(ex.RetryAfter);
      await context.SaveChangesAsync(ct);
      return false;
    }
    catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
    {
      _logger.LogWarning(ex, "Oura pull failed for {PersonId}", personId);
      link.LastError = "Oura did not answer. The next pull will try again.";
      link.NextPullAt = UtcNow.Add(RetryAfterError);
      await context.SaveChangesAsync(ct);
      return false;
    }
  }

  /// <summary>The access token, refreshed first when it is about to expire. Oura refresh tokens are single use.</summary>
  private async Task<string> AccessTokenAsync(OuraApp app, SelfKeptDbContext context, OuraConnection link, CancellationToken ct)
  {
    if (link.AccessExpiresAt > UtcNow.Add(RefreshMargin))
      return _encryption.Decrypt(link.AccessCipher);

    var tokens = await _api.RefreshAsync(app, _encryption.Decrypt(link.RefreshCipher), ct);
    link.AccessCipher = _encryption.Encrypt(tokens.AccessToken);
    link.RefreshCipher = _encryption.Encrypt(tokens.RefreshToken);
    link.AccessExpiresAt = UtcNow.AddSeconds(tokens.ExpiresIn);
    if (tokens.Scope != null) link.Scope = tokens.Scope;
    await context.SaveChangesAsync(ct); // write the rotated pair before using it
    return tokens.AccessToken;
  }
}

/// <summary>Pulls Oura for everyone who is due, once a minute. Job-locked: one copy pulls.</summary>
public sealed class OuraPollJob(IServiceScopeFactory scopes, ILogger<OuraPollJob> logger) : BackgroundService
{
  public static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    try
    {
      await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          using var scope = scopes.CreateScope();
          // Does nothing until an Oura application is set up.
          await scope.ServiceProvider.GetRequiredService<IOuraService>().PullDueAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          logger.LogError(ex, "Oura poll tick failed; retrying next tick");
        }
        await Task.Delay(Tick, stoppingToken);
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      // normal shutdown
    }
  }
}
