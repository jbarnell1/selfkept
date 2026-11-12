using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// The numbers of a day log entry confirmed from a photo (docs/photo-design.md, "Numbers for
/// food"). Each number is also a sample (energy_intake_kcal, protein_g, carbs_g, fat_g) under the
/// person's own "Photo" source, so the day's totals, the Protein target and Progress count it.
/// Deleting the entry removes its samples and recomputes the day. Modelled on MeasurementService
/// and its "Typed in" source; the caller runs both calls inside one transaction.
/// </summary>
public sealed class NutritionSamples
{
  /// <summary>One number of an entry: its metric, its name in messages, its unit and the range a value must fall in.</summary>
  public sealed record Field(string MetricKey, string Name, string Unit, double Min, double Max);

  public static readonly Field Energy = new(MetricKeys.EnergyIntakeKcal, "Energy", "kcal", 0, 5000);
  public static readonly Field Protein = new(MetricKeys.ProteinG, "Protein", "g", 0, 400);
  public static readonly Field Carbs = new(MetricKeys.CarbsG, "Carbohydrate", "g", 0, 1000);
  public static readonly Field Fat = new(MetricKeys.FatG, "Fat", "g", 0, 500);

  public static readonly IReadOnlyList<Field> Fields = [Energy, Protein, Carbs, Fat];

  private readonly ISourceRepository _sources;
  private readonly ISampleRepository _samples;
  private readonly IDailySummaryService _summaries;

  public NutritionSamples(ISourceRepository sources, ISampleRepository samples, IDailySummaryService summaries)
  {
    _sources = sources;
    _samples = samples;
    _summaries = summaries;
  }

  /// <summary>Whether the value is inside the field's range.</summary>
  public static bool InRange(Field field, double value) => double.IsFinite(value) && value >= field.Min && value <= field.Max;

  /// <summary>
  /// Checks the numbers a person confirmed and returns them rounded to the stored tenth. Throws
  /// DomainValidationException with the contract's messages.
  /// </summary>
  public static (string Basis, decimal? Energy, decimal? Protein, decimal? Carbs, decimal? Fat) Validate(string kind, JournalNutritionDto nutrition)
  {
    if (kind is not (JournalKinds.Food or JournalKinds.Drink)) throw new DomainValidationException("Only food and drink have numbers.");
    var basis = nutrition.Basis?.Trim().ToLowerInvariant();
    if (basis == null || !NutritionBases.All.Contains(basis))
    {
      throw new DomainValidationException($"basis must be one of: {string.Join(", ", NutritionBases.All)}.");
    }
    double?[] values = [nutrition.EnergyKcal, nutrition.ProteinG, nutrition.CarbsG, nutrition.FatG];
    if (values.All(v => v == null)) throw new DomainValidationException("Give at least one number, or leave nutrition out.");
    for (var i = 0; i < Fields.Count; i++)
    {
      if (values[i] is { } value && !InRange(Fields[i], value))
      {
        var f = Fields[i];
        throw new DomainValidationException($"{f.Name} must be between {f.Min:0} and {f.Max:0} {f.Unit}.");
      }
    }
    return (basis, Round(values[0]), Round(values[1]), Round(values[2]), Round(values[3]));
  }

  /// <summary>Writes one sample per number of the entry and recomputes those metrics for its day.</summary>
  public async Task WriteAsync(JournalEntry entry, string timeZone, DateTime now, CancellationToken ct)
  {
    var numbers = Numbers(entry);
    if (numbers.Count == 0) return;
    var source = await GetOrCreateSourceAsync(entry.PersonId, now, ct);
    await _samples.EnsurePartitionsAsync([DayBoundaries.MonthStart(entry.At)], ct);
    var result = await _samples.InsertManyAsync(numbers.Select(n => new Sample
    {
      Id = Guid.CreateVersion7(),
      PersonId = entry.PersonId,
      SourceId = source.Id,
      SourceUid = SourceUid(entry.Id, n.Field.MetricKey),
      MetricKey = n.Field.MetricKey,
      StartAt = entry.At,
      EndAt = entry.At,
      Value = n.Value,
      Unit = n.Field.Unit,
      CreatedAt = now,
    }).ToList(), ct);

    source.LastSyncAt = now;
    source.LastSampleAt = source.LastSampleAt is { } last && last > entry.At ? last : entry.At;
    source.SampleCount += result.Accepted;
    await _sources.UpdateAsync(source, ct);
    await _summaries.RecomputeAsync(entry.PersonId, timeZone, numbers.Select(n => (n.Field.MetricKey, entry.Day)).ToList(), ct);
  }

  /// <summary>Removes the samples of the entry, if it had numbers, and recomputes those metrics for its day.</summary>
  public async Task RemoveAsync(JournalEntry entry, string timeZone, CancellationToken ct)
  {
    var numbers = Numbers(entry);
    if (numbers.Count == 0) return;
    var source = await _sources.FindActiveAsync(entry.PersonId, SourceKinds.Manual, SourceKinds.PhotoDeviceName, SourceKinds.CheckinPlatform, ct);
    if (source == null) return;
    var removed = 0;
    foreach (var n in numbers) removed += await _samples.DeleteBySourceUidAsync(source.Id, SourceUid(entry.Id, n.Field.MetricKey), ct);
    source.SampleCount = Math.Max(0, source.SampleCount - removed);
    await _sources.UpdateAsync(source, ct);
    await _summaries.RecomputeAsync(entry.PersonId, timeZone, numbers.Select(n => (n.Field.MetricKey, entry.Day)).ToList(), ct);
  }

  public static string SourceUid(Guid entryId, string metricKey) => $"photo:{entryId:N}:{metricKey}";

  private static decimal? Round(double? value) => value is { } v ? Math.Round((decimal)v, 1, MidpointRounding.AwayFromZero) : null;

  private static List<(Field Field, double Value)> Numbers(JournalEntry entry)
  {
    var list = new List<(Field, double)>();
    if (entry.NutritionBasis == null) return list;
    if (entry.EnergyKcal is { } e) list.Add((Energy, (double)e));
    if (entry.ProteinG is { } p) list.Add((Protein, (double)p));
    if (entry.CarbsG is { } c) list.Add((Carbs, (double)c));
    if (entry.FatG is { } f) list.Add((Fat, (double)f));
    return list;
  }

  private async Task<Source> GetOrCreateSourceAsync(Guid personId, DateTime now, CancellationToken ct)
  {
    var existing = await _sources.FindActiveAsync(personId, SourceKinds.Manual, SourceKinds.PhotoDeviceName, SourceKinds.CheckinPlatform, ct);
    if (existing != null) return existing;
    return await _sources.AddAsync(new Source
    {
      Id = Guid.CreateVersion7(),
      PersonId = personId,
      Kind = SourceKinds.Manual,
      DeviceName = SourceKinds.PhotoDeviceName,
      Platform = SourceKinds.CheckinPlatform,
      Enabled = true,
      CreatedAt = now,
    }, ct);
  }
}
