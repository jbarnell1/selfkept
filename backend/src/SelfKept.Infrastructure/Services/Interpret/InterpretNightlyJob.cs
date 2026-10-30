using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Interpret;

/// <summary>
/// When a person's nightly run is due: from 03:30 in their own time zone, yesterday's day is
/// interpreted once. Pure so the clock arithmetic is tested without a host.
/// </summary>
public static class InterpretSchedule
{
  public static readonly TimeOnly RunAt = new(3, 30);

  /// <summary>The day to interpret (yesterday, local) when it is past 03:30 local; null before that.</summary>
  public static DateOnly? DueDay(DateTime utcNow, TimeZoneInfo zone)
  {
    var local = TimeZoneInfo.ConvertTimeFromUtc(DayBoundaries.AsUtc(utcNow), zone);
    if (TimeOnly.FromDateTime(local) < RunAt) return null;
    return DateOnly.FromDateTime(local).AddDays(-1);
  }
}

/// <summary>
/// Job-locked hosted service: every 15 minutes, finds the persons past 03:30 in their zone whose
/// yesterday has not been interpreted and runs them. Idempotent per (person, day): a day already
/// holding baselines or insights is skipped, and a set in memory saves the lookups between ticks.
/// </summary>
public sealed class InterpretNightlyJob : BackgroundService
{
  public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
  private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

  private readonly IServiceScopeFactory _scopes;
  private readonly TimeProvider _clock;
  private readonly ILogger<InterpretNightlyJob> _logger;
  private readonly HashSet<(Guid PersonId, DateOnly Day)> _done = [];

  public InterpretNightlyJob(IServiceScopeFactory scopes, TimeProvider clock, ILogger<InterpretNightlyJob> logger)
  {
    _scopes = scopes;
    _clock = clock;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("Interpret nightly job started: checks every {Minutes} minutes for persons past {Time} local", CheckInterval.TotalMinutes, InterpretSchedule.RunAt);
    try
    {
      await Task.Delay(InitialDelay, stoppingToken);
      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          await TickAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Interpret nightly tick failed; retrying next tick");
        }
        await Task.Delay(CheckInterval, stoppingToken);
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      // normal shutdown
    }
  }

  /// <summary>One pass over every person. Public so a test can drive it without the timer.</summary>
  public async Task TickAsync(CancellationToken ct)
  {
    using var scope = _scopes.CreateScope();
    var persons = scope.ServiceProvider.GetRequiredService<IPersonRepository>();
    var runner = scope.ServiceProvider.GetRequiredService<IInterpretRunner>();
    var now = _clock.GetUtcNow().UtcDateTime;

    foreach (var person in await persons.ListAllAsync(ct))
    {
      var zone = DayBoundaries.Resolve(person.TimeZone);
      if (InterpretSchedule.DueDay(now, zone) is not { } day) continue;
      if (_done.Contains((person.Id, day))) continue;
      try
      {
        if (!await runner.HasRunAsync(person.Id, day, ct))
        {
          await runner.RunForPersonAsync(person.Id, day, ct);
        }
        _done.Add((person.Id, day));
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Interpret failed for person {PersonId} on {Day}; will retry next tick", person.Id, day);
      }
    }

    // Forget runs older than three days so the set does not grow for ever.
    _done.RemoveWhere(d => d.Day < DateOnly.FromDateTime(now).AddDays(-3));
  }
}
