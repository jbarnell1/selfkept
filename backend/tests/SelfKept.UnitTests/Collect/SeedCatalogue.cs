using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;

namespace SelfKept.UnitTests.Collect;

/// <summary>An in-memory catalogue over the seed list, so tests need no database.</summary>
public sealed class SeedCatalogue : IMetricCatalogue
{
  private readonly Dictionary<string, Metric> _byKey = MetricSeed.All.ToDictionary(m => m.Key, StringComparer.Ordinal);

  public IReadOnlyList<Metric> All => MetricSeed.All;

  public Metric? Find(string key) => _byKey.TryGetValue(key, out var m) ? m : null;

  public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
}
