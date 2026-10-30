using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.Infrastructure.Repositories;

public sealed class LabReportRepository : ILabReportRepository
{
  private readonly SelfKeptDbContext _context;
  private readonly IAccessPolicy _policy;

  public LabReportRepository(SelfKeptDbContext context, IAccessPolicy policy)
  {
    _context = context;
    _policy = policy;
  }

  public async Task<LabReport> AddAsync(LabReport report, CancellationToken ct = default)
  {
    _context.LabReports.Add(report);
    await _context.SaveChangesAsync(ct);
    return report;
  }

  public Task<LabReport?> GetOwnAsync(Guid personId, Guid reportId, CancellationToken ct = default) =>
    _context.LabReports.Include(r => r.Results)
      .FirstOrDefaultAsync(r => r.Id == reportId && r.PersonId == personId, ct);

  public async Task<LabReport?> GetAsync(Guid viewerPersonId, Guid reportId, CancellationToken ct = default)
  {
    var report = await _context.LabReports.AsNoTracking().Include(r => r.Results).FirstOrDefaultAsync(r => r.Id == reportId, ct);
    if (report == null) return null;
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, report.PersonId, ShareCategory.Labs, ct)) return null;
    Sort(report);
    return report;
  }

  public async Task<IReadOnlyList<LabReport>?> ListAsync(Guid viewerPersonId, Guid targetPersonId, CancellationToken ct = default)
  {
    if (!await ViewerAccess.AllowedAsync(_policy, viewerPersonId, targetPersonId, ShareCategory.Labs, ct)) return null;
    return await ListOwnAsync(targetPersonId, int.MaxValue, ct);
  }

  public async Task<IReadOnlyList<LabReport>> ListOwnAsync(Guid personId, int limit, CancellationToken ct = default)
  {
    var rows = await _context.LabReports.AsNoTracking().Include(r => r.Results)
      .Where(r => r.PersonId == personId)
      .OrderByDescending(r => r.CollectedOn).ThenByDescending(r => r.CreatedAt)
      .Take(limit)
      .ToListAsync(ct);
    foreach (var row in rows) Sort(row);
    return rows;
  }

  public async Task RemoveAsync(LabReport report, CancellationToken ct = default)
  {
    // Results are loaded with the report (GetOwnAsync), so the provider without ON DELETE CASCADE
    // (InMemory) removes them too.
    _context.LabResults.RemoveRange(report.Results);
    _context.LabReports.Remove(report);
    await _context.SaveChangesAsync(ct);
  }

  private static void Sort(LabReport report) => report.Results.Sort((a, b) => string.CompareOrdinal(a.MetricKey, b.MetricKey));
}

/// <summary>
/// In-memory copy of lab_reference. Loaded once at startup by <see cref="LabCatalogueLoader"/>.
/// On a provider that runs no SQL migrations (the InMemory test database) the table is seeded
/// from <see cref="LabSeed"/> before loading.
/// </summary>
public sealed class LabReferenceRepository : ILabReferenceRepository
{
  private readonly IDbContextFactory<SelfKeptDbContext> _contextFactory;
  private readonly ILogger<LabReferenceRepository> _logger;
  private readonly SemaphoreSlim _loadLock = new(1, 1);
  private volatile Dictionary<string, List<LabReference>>? _byKey;
  private volatile IReadOnlyList<LabReference> _all = [];

  public LabReferenceRepository(IDbContextFactory<SelfKeptDbContext> contextFactory, ILogger<LabReferenceRepository> logger)
  {
    _contextFactory = contextFactory;
    _logger = logger;
  }

  public IReadOnlyList<LabReference> All
  {
    get
    {
      EnsureLoaded();
      return _all;
    }
  }

  public IReadOnlyList<LabReference> RowsFor(string metricKey)
  {
    EnsureLoaded();
    return _byKey!.TryGetValue(metricKey, out var rows) ? rows : [];
  }

  public LabReference? Find(Metric metric, SexAtBirth sex) => LabReferenceLookup.Resolve(RowsFor(metric.Key), metric, sex);

  public async Task ReloadAsync(CancellationToken ct = default)
  {
    await _loadLock.WaitAsync(ct);
    try
    {
      await using var context = await _contextFactory.CreateDbContextAsync(ct);
      if (!context.Database.IsRelational() && !await context.LabReferences.AnyAsync(ct))
      {
        context.LabReferences.AddRange(LabSeed.References.Select(r => new LabReference
        {
          MetricKey = r.MetricKey, Sex = r.Sex, Low = r.Low, High = r.High, OptimalLow = r.OptimalLow, OptimalHigh = r.OptimalHigh,
          GoodDirection = r.GoodDirection, Tier = r.Tier, SourceUrl = r.SourceUrl,
        }));
        await context.SaveChangesAsync(ct);
      }

      var rows = await context.LabReferences.AsNoTracking().OrderBy(r => r.MetricKey).ThenBy(r => r.Sex).ToListAsync(ct);
      _all = rows;
      _byKey = rows.GroupBy(r => r.MetricKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
      _logger.LogInformation("Lab reference table loaded: {Count} rows", rows.Count);
    }
    finally
    {
      _loadLock.Release();
    }
  }

  private void EnsureLoaded()
  {
    if (_byKey == null)
    {
      throw new InvalidOperationException("The lab reference table has not been loaded. Register LabCatalogueLoader or call ReloadAsync first.");
    }
  }
}

/// <summary>The sex resolution rule, shared with the in-memory test doubles.</summary>
public static class LabReferenceLookup
{
  /// <summary>
  /// A marker the catalogue marks female-only or male-only has no range for the other sex. A
  /// "both" row applies to anyone; a sex-specific row only to that sex. When the person has not
  /// set a sex at birth, a sex-split marker gets the combined range (the lowest low and the
  /// highest high of the sex rows), so only a value outside both is flagged: no false alarms,
  /// and a value like ferritin at 1,180 is still caught.
  /// </summary>
  public static LabReference? Resolve(IReadOnlyList<LabReference> rows, Metric metric, SexAtBirth sex)
  {
    if (!IsApplicable(metric, sex)) return null;
    var both = rows.FirstOrDefault(r => r.Sex == SexApplicabilities.Both);
    if (both != null) return both;
    var wanted = sex switch
    {
      SexAtBirth.Female => SexApplicabilities.Female,
      SexAtBirth.Male => SexApplicabilities.Male,
      // A single-sex marker (progesterone) uses its own sex's row even when the person's is unset.
      _ when metric.SexApplicability is SexApplicabilities.Female or SexApplicabilities.Male => metric.SexApplicability,
      _ => null,
    };
    return wanted == null ? Combined(rows) : rows.FirstOrDefault(r => r.Sex == wanted);
  }

  /// <summary>The widest range across the sex-specific rows, or null when there are none.</summary>
  public static LabReference? Combined(IReadOnlyList<LabReference> rows)
  {
    var sexRows = rows.Where(r => r.Sex is SexApplicabilities.Female or SexApplicabilities.Male).ToList();
    if (sexRows.Count == 0) return null;
    if (sexRows.Count == 1) return sexRows[0];

    // A bound only counts when every row has one; otherwise that side is open.
    double? low = sexRows.All(r => r.Low != null) ? sexRows.Min(r => r.Low) : null;
    double? high = sexRows.All(r => r.High != null) ? sexRows.Max(r => r.High) : null;
    var first = sexRows[0];
    return new LabReference
    {
      MetricKey = first.MetricKey,
      Sex = SexApplicabilities.Both,
      Low = low,
      High = high,
      OptimalLow = null,
      OptimalHigh = null,
      GoodDirection = first.GoodDirection,
      Tier = sexRows.Any(r => r.Tier == LabTiers.Clinician) ? LabTiers.Clinician
        : sexRows.Any(r => r.Tier == LabTiers.Watch) ? LabTiers.Watch
        : first.Tier,
      SourceUrl = first.SourceUrl,
    };
  }

  /// <summary>Whether the marker applies to the person at all (catalogue sex applicability; unknown sex sees everything).</summary>
  public static bool IsApplicable(Metric metric, SexAtBirth sex) => (metric.SexApplicability, sex) switch
  {
    (SexApplicabilities.Female, SexAtBirth.Male) => false,
    (SexApplicabilities.Male, SexAtBirth.Female) => false,
    _ => true,
  };
}

/// <summary>In-memory copy of lab_marker_alias, loaded like the reference table.</summary>
public sealed class MarkerAliasRepository : IMarkerAliasRepository
{
  private readonly IDbContextFactory<SelfKeptDbContext> _contextFactory;
  private readonly ILogger<MarkerAliasRepository> _logger;
  private readonly SemaphoreSlim _loadLock = new(1, 1);
  private volatile IReadOnlyList<LabMarkerAlias>? _all;

  public MarkerAliasRepository(IDbContextFactory<SelfKeptDbContext> contextFactory, ILogger<MarkerAliasRepository> logger)
  {
    _contextFactory = contextFactory;
    _logger = logger;
  }

  public IReadOnlyList<LabMarkerAlias> All =>
    _all ?? throw new InvalidOperationException("The marker alias table has not been loaded. Register LabCatalogueLoader or call ReloadAsync first.");

  public async Task ReloadAsync(CancellationToken ct = default)
  {
    await _loadLock.WaitAsync(ct);
    try
    {
      await using var context = await _contextFactory.CreateDbContextAsync(ct);
      if (!context.Database.IsRelational() && !await context.LabMarkerAliases.AnyAsync(ct))
      {
        context.LabMarkerAliases.AddRange(LabSeed.Aliases.Select(a => new LabMarkerAlias { MetricKey = a.MetricKey, Alias = a.Alias }));
        await context.SaveChangesAsync(ct);
      }
      var rows = await context.LabMarkerAliases.AsNoTracking().OrderBy(a => a.MetricKey).ThenBy(a => a.Alias).ToListAsync(ct);
      _all = rows;
      _logger.LogInformation("Lab marker aliases loaded: {Count} rows", rows.Count);
    }
    finally
    {
      _loadLock.Release();
    }
  }
}

/// <summary>Loads the reference and alias tables at startup, after the migrations have run.</summary>
public sealed class LabCatalogueLoader : IHostedService
{
  private readonly ILabReferenceRepository _references;
  private readonly IMarkerAliasRepository _aliases;

  public LabCatalogueLoader(ILabReferenceRepository references, IMarkerAliasRepository aliases)
  {
    _references = references;
    _aliases = aliases;
  }

  public async Task StartAsync(CancellationToken cancellationToken)
  {
    await _references.ReloadAsync(cancellationToken);
    await _aliases.ReloadAsync(cancellationToken);
  }

  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
