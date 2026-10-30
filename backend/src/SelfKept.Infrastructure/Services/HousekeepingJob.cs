using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Services;

/// <summary>What one housekeeping pass removed.</summary>
public sealed record HousekeepingResult(int RefreshTokens, int OAuthRequests, int OAuthRefreshTokens, int Invites, int OuraStates, int SsoStates)
{
  public int Total => RefreshTokens + OAuthRequests + OAuthRefreshTokens + Invites + OuraStates + SsoStates;
}

/// <summary>
/// Removes rows nobody can use any more: expired refresh tokens (first-party and OAuth), expired
/// OAuth authorize requests (anyone can start one, so they must not pile up), invites that
/// expired unused a month ago, and abandoned Oura and provider sign-ins. A token is kept a day past its expiry,
/// longer than any reuse check looks back; health data is never touched here.
/// </summary>
public sealed class Housekeeping
{
  public static readonly TimeSpan Grace = TimeSpan.FromDays(1);
  public static readonly TimeSpan UnusedInviteGrace = TimeSpan.FromDays(30);

  private readonly IDbContextFactory<SelfKeptDbContext> _contexts;
  private readonly TimeProvider _clock;

  public Housekeeping(IDbContextFactory<SelfKeptDbContext> contexts, TimeProvider clock)
  {
    _contexts = contexts;
    _clock = clock;
  }

  public async Task<HousekeepingResult> RunAsync(CancellationToken ct = default)
  {
    await using var context = await _contexts.CreateDbContextAsync(ct);
    var now = _clock.GetUtcNow().UtcDateTime;
    var cutoff = now - Grace;
    var inviteCutoff = now - UnusedInviteGrace;
    return new HousekeepingResult(
      await DeleteAsync(context, context.RefreshTokens.Where(t => t.ExpiresAt < cutoff), ct),
      await DeleteAsync(context, context.OAuthAuthorizationRequests.Where(r => r.ExpiresAt < cutoff), ct),
      await DeleteAsync(context, context.OAuthRefreshTokens.Where(t => t.ExpiresAt < cutoff), ct),
      await DeleteAsync(context, context.HouseholdInvites.Where(i => i.UsedAt == null && i.ExpiresAt < inviteCutoff), ct),
      await DeleteAsync(context, context.OuraAuthStates.Where(s => s.ExpiresAt < cutoff), ct),
      await DeleteAsync(context, context.SsoAuthStates.Where(s => s.ExpiresAt < cutoff), ct)
        + await DeleteAsync(context, context.SsoHandoffs.Where(h => h.ExpiresAt < cutoff), ct));
  }

  /// <summary>One statement on Postgres; the InMemory provider used by tests has no bulk delete.</summary>
  private static async Task<int> DeleteAsync<T>(SelfKeptDbContext context, IQueryable<T> rows, CancellationToken ct) where T : class
  {
    if (context.Database.IsRelational())
      return await rows.ExecuteDeleteAsync(ct);
    var list = await rows.ToListAsync(ct);
    context.RemoveRange(list);
    await context.SaveChangesAsync(ct);
    return list.Count;
  }
}

/// <summary>Runs <see cref="Housekeeping"/> every hour in the copy that holds the job lock.</summary>
public sealed class HousekeepingJob : BackgroundService
{
  private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
  private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(2);

  private readonly IServiceScopeFactory _scopes;
  private readonly ILogger<HousekeepingJob> _logger;

  public HousekeepingJob(IServiceScopeFactory scopes, ILogger<HousekeepingJob> logger)
  {
    _scopes = scopes;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    try
    {
      await Task.Delay(FirstRun, stoppingToken);
      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          using var scope = _scopes.CreateScope();
          var result = await scope.ServiceProvider.GetRequiredService<Housekeeping>().RunAsync(stoppingToken);
          if (result.Total > 0)
            _logger.LogInformation("Housekeeping removed {Result}", result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Housekeeping failed; trying again next hour");
        }
        await Task.Delay(Interval, stoppingToken);
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      // normal shutdown
    }
  }
}
