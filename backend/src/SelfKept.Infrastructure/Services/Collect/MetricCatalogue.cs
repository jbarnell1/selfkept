using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Services.Collect;

/// <summary>
/// In-memory copy of the metric table, loaded once at startup by <see cref="MetricCatalogueLoader"/>;
/// reading it before then is a wiring bug and throws. On a provider that runs no SQL migrations (the
/// InMemory test database) the table is seeded from <see cref="MetricSeed"/> before loading.
/// </summary>
public sealed class MetricCatalogue : IMetricCatalogue
{
  private readonly IDbContextFactory<SelfKeptDbContext> _contextFactory;
  private readonly ILogger<MetricCatalogue> _logger;
  private readonly SemaphoreSlim _loadLock = new(1, 1);
  private volatile Dictionary<string, Metric>? _byKey;
  private volatile IReadOnlyList<Metric> _all = [];

  public MetricCatalogue(IDbContextFactory<SelfKeptDbContext> contextFactory, ILogger<MetricCatalogue> logger)
  {
    _contextFactory = contextFactory;
    _logger = logger;
  }

  public IReadOnlyList<Metric> All
  {
    get
    {
      EnsureLoaded();
      return _all;
    }
  }

  public Metric? Find(string key)
  {
    EnsureLoaded();
    return _byKey!.TryGetValue(key, out var metric) ? metric : null;
  }

  public async Task ReloadAsync(CancellationToken ct = default)
  {
    await _loadLock.WaitAsync(ct);
    try
    {
      await using var context = await _contextFactory.CreateDbContextAsync(ct);
      if (!context.Database.IsRelational() && !await context.Metrics.AnyAsync(ct))
      {
        context.Metrics.AddRange(MetricSeed.All.Select(Clone));
        await context.SaveChangesAsync(ct);
      }

      var rows = await context.Metrics.AsNoTracking().OrderBy(m => m.Key).ToListAsync(ct);
      _all = rows;
      _byKey = rows.ToDictionary(m => m.Key, StringComparer.Ordinal);
      _logger.LogInformation("Metric catalogue loaded: {Count} metrics", rows.Count);
    }
    finally
    {
      _loadLock.Release();
    }
  }

  // Hosted services start before Kestrel listens, so the loader always runs before the first
  // request. Reaching this unloaded means the loader was not registered.
  private void EnsureLoaded()
  {
    if (_byKey == null)
    {
      throw new InvalidOperationException("The metric catalogue has not been loaded. Register MetricCatalogueLoader or call ReloadAsync first.");
    }
  }

  private static Metric Clone(Metric m) => new()
  {
    Key = m.Key, Unit = m.Unit, Kind = m.Kind, Category = m.Category, DisplayName = m.DisplayName, Blurb = m.Blurb,
    GoodDirection = m.GoodDirection, Loinc = m.Loinc, ValueLabels = m.ValueLabels, SexApplicability = m.SexApplicability,
  };
}

/// <summary>Loads the catalogue at startup, after the migrations have run.</summary>
public sealed class MetricCatalogueLoader : IHostedService
{
  private readonly IMetricCatalogue _catalogue;

  public MetricCatalogueLoader(IMetricCatalogue catalogue)
  {
    _catalogue = catalogue;
  }

  public Task StartAsync(CancellationToken cancellationToken) => _catalogue.ReloadAsync(cancellationToken);

  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
