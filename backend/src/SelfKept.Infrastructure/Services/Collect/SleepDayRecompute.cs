using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Services.Collect;

/// <summary>What one pass of <see cref="SleepDayRecompute"/> did. Ran is false when the marker said it was already done.</summary>
public sealed record SleepDayRecomputeResult(bool Ran, int Persons, int Days);

/// <summary>
/// One-time fix after the sleep day changed from the calendar day a stage ended on to 18:00 to
/// 18:00 (<see cref="DailySummaryRules.SleepDayShift"/>). Sleep rows written before that split a
/// night across two days and nothing else recomputes them. For every person it recomputes each
/// day their sleep samples touch under the old rule or the new one, through the same
/// <see cref="IDailySummaryService"/> ingest uses, so an old-rule row is replaced or removed and a
/// day without samples (nothing to derive from) is left alone. Raw sleep samples are never folded
/// by retention (DenseMetrics is heart rate only), so every old row still has its samples.
/// The marker is written last: an interrupted run simply runs again, and the recompute is
/// idempotent.
/// </summary>
public sealed class SleepDayRecompute
{
  /// <summary>server_setting key written once the pass has finished.</summary>
  public const string MarkerKey = "maintenance.sleep_day_18h";

  /// <summary>Days recomputed per call, so one person's history is never one huge upsert.</summary>
  private const int ChunkDays = 90;

  private readonly IDbContextFactory<SelfKeptDbContext> _contexts;
  private readonly IPersonRepository _persons;
  private readonly IDailySummaryService _summaries;
  private readonly ITodayCacheRepository _todayCache;
  private readonly ServerSettingStore _settings;
  private readonly TimeProvider _clock;
  private readonly ILogger<SleepDayRecompute> _logger;

  public SleepDayRecompute(IDbContextFactory<SelfKeptDbContext> contexts, IPersonRepository persons, IDailySummaryService summaries, ITodayCacheRepository todayCache, ServerSettingStore settings, TimeProvider clock, ILogger<SleepDayRecompute> logger)
  {
    _contexts = contexts;
    _persons = persons;
    _summaries = summaries;
    _todayCache = todayCache;
    _settings = settings;
    _clock = clock;
    _logger = logger;
  }

  public async Task<SleepDayRecomputeResult> RunAsync(CancellationToken ct = default)
  {
    if ((await _settings.GetAsync([MarkerKey], ct)).ContainsKey(MarkerKey)) return new SleepDayRecomputeResult(false, 0, 0);

    var persons = 0;
    var days = 0;
    foreach (var person in await _persons.ListAllAsync(ct))
    {
      var zone = DayBoundaries.Resolve(person.TimeZone);
      await using var context = await _contexts.CreateDbContextAsync(ct);
      var instants = await context.Samples.AsNoTracking()
        .Where(s => s.PersonId == person.Id && s.MetricKey == MetricKeys.SleepStage)
        .Select(s => s.EndAt ?? s.StartAt)
        .ToListAsync(ct);
      if (instants.Count == 0) continue;

      var touched = instants
        .SelectMany(at => new[]
        {
          // Where the old rule filed it (the calendar day it ended) and where it belongs now.
          DayBoundaries.LocalDay(at, zone),
          DailySummaryRules.DayFor(new Sample { MetricKey = MetricKeys.SleepStage, StartAt = at, EndAt = at }, zone)!.Value,
        })
        .Distinct()
        .Order()
        .ToList();
      foreach (var chunk in touched.Chunk(ChunkDays))
      {
        await _summaries.RecomputeAsync(person.Id, person.TimeZone, chunk.Select(d => (MetricKeys.SleepStage, d)).ToList(), ct);
      }
      await _todayCache.InvalidateAsync(person.Id, ct);
      persons++;
      days += touched.Count;
    }

    await _settings.SetAsync(new Dictionary<string, string> { [MarkerKey] = _clock.GetUtcNow().UtcDateTime.ToString("O") }, null, ct);
    _logger.LogInformation("Sleep days recomputed from 18:00 to 18:00: {Days} days for {Persons} people", days, persons);
    return new SleepDayRecomputeResult(true, persons, days);
  }
}

/// <summary>
/// Runs <see cref="SleepDayRecompute"/> once, a minute after start, in the copy holding the job
/// lock, so it never holds up startup or the health check. A failure is retried an hour later;
/// once the marker is written every later start returns at once.
/// </summary>
public sealed class SleepDayRecomputeJob : BackgroundService
{
  private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(1);
  private static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

  private readonly IServiceScopeFactory _scopes;
  private readonly ILogger<SleepDayRecomputeJob> _logger;

  public SleepDayRecomputeJob(IServiceScopeFactory scopes, ILogger<SleepDayRecomputeJob> logger)
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
          await scope.ServiceProvider.GetRequiredService<SleepDayRecompute>().RunAsync(stoppingToken);
          return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Sleep day recompute failed; trying again in an hour");
        }
        await Task.Delay(RetryAfter, stoppingToken);
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      // normal shutdown
    }
  }
}
