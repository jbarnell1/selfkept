using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Collect;

/// <summary>
/// POST /api/ingest/batch. Validates every item against the catalogue, converts units, drops
/// what this source already sent (idempotent on sourceUid), updates the source's counters and
/// recomputes the daily summaries the batch touched. Not audited per batch: the source row
/// carries the counts.
/// </summary>
public sealed class IngestService : IIngestService
{
  private readonly ISourceRepository _sources;
  private readonly ISampleRepository _samples;
  private readonly IWorkoutRepository _workouts;
  private readonly IPersonRepository _persons;
  private readonly IMetricCatalogue _catalogue;
  private readonly IDailySummaryService _summaries;
  private readonly IEncryptionService _encryption;
  private readonly IUnitOfWork _unitOfWork;
  private readonly ITodayCacheRepository _todayCache;
  private readonly TimeProvider _clock;

  public IngestService(
    ISourceRepository sources,
    ISampleRepository samples,
    IWorkoutRepository workouts,
    IPersonRepository persons,
    IMetricCatalogue catalogue,
    IDailySummaryService summaries,
    IEncryptionService encryption,
    IUnitOfWork unitOfWork,
    ITodayCacheRepository todayCache,
    TimeProvider clock)
  {
    _todayCache = todayCache;
    _sources = sources;
    _samples = samples;
    _workouts = workouts;
    _persons = persons;
    _catalogue = catalogue;
    _summaries = summaries;
    _encryption = encryption;
    _unitOfWork = unitOfWork;
    _clock = clock;
  }

  public async Task<IngestBatchResponse?> IngestAsync(Guid personId, IngestBatchRequest request, CancellationToken ct = default)
  {
    var sampleInputs = request.Samples ?? [];
    var workoutInputs = request.Workouts ?? [];
    var total = sampleInputs.Count + workoutInputs.Count;
    if (total > BatchValidator.MaxBatchItems)
    {
      throw new DomainValidationException($"A batch holds at most {BatchValidator.MaxBatchItems:N0} items; this one has {total:N0}. Page the batch.");
    }

    var source = await _sources.GetAsync(personId, request.SourceId, ct);
    if (source == null) return null;
    if (!source.Enabled) throw new ConflictException("This source is disabled. Enable it before posting data.");

    var person = await _persons.GetByIdAsync(personId, ct);
    if (person == null) return null;
    var zone = DayBoundaries.Resolve(person.TimeZone);
    var now = _clock.GetUtcNow().UtcDateTime;

    var rejected = new List<RejectedItemDto>();
    var inBatchDuplicates = 0;

    var samples = new List<Sample>();
    var seenSampleUids = new HashSet<string>(StringComparer.Ordinal);
    foreach (var input in sampleInputs)
    {
      var outcome = BatchValidator.ValidateSample(input, _catalogue, source, now);
      if (!outcome.Accepted)
      {
        rejected.Add(new RejectedItemDto(input.SourceUid, outcome.Reason!));
      }
      else if (!seenSampleUids.Add(outcome.Sample!.SourceUid))
      {
        inBatchDuplicates++;
      }
      else
      {
        samples.Add(outcome.Sample!);
      }
    }

    var workouts = new List<Workout>();
    var seenWorkoutUids = new HashSet<string>(StringComparer.Ordinal);
    foreach (var input in workoutInputs)
    {
      var outcome = BatchValidator.ValidateWorkout(input, source, now, route => CollectMapping.ToCipherBytes(_encryption.Encrypt(route)));
      if (!outcome.Accepted)
      {
        rejected.Add(new RejectedItemDto(input.SourceUid, outcome.Reason!));
      }
      else if (!seenWorkoutUids.Add(outcome.Workout!.SourceUid))
      {
        inBatchDuplicates++;
      }
      else
      {
        workouts.Add(outcome.Workout!);
      }
    }

    var (sampleResult, workoutResult) = await _unitOfWork.RunInTransactionAsync(async () =>
    {
      await _samples.EnsurePartitionsAsync(samples.Select(s => DayBoundaries.MonthStart(s.StartAt)), ct);
      var sampleResult = await _samples.InsertManyAsync(samples, ct);
      var workoutResult = await _workouts.InsertManyAsync(workouts, ct);

      if (!string.IsNullOrEmpty(request.Cursor)) source.Cursor = request.Cursor;
      source.LastSyncAt = now;
      if (sampleResult.Accepted > 0)
      {
        var newest = samples.Max(s => s.StartAt);
        source.LastSampleAt = source.LastSampleAt is { } last && last > newest ? last : newest;
        source.SampleCount += sampleResult.Accepted;
      }
      await _sources.UpdateAsync(source, ct);

      if (sampleResult.Accepted > 0)
      {
        var touched = samples
          .Select(s => (s.MetricKey, Day: DailySummaryRules.DayFor(s, zone)))
          .Where(t => t.Day != null)
          .Select(t => (t.MetricKey, t.Day!.Value))
          .Distinct()
          .ToList();
        await _summaries.RecomputeAsync(personId, person.TimeZone, touched, ct);
        // New data may move last night's numbers: the Today sentence is composed afresh.
        await _todayCache.InvalidateAsync(personId, ct);
      }

      return (sampleResult, workoutResult);
    }, ct: ct);

    return new IngestBatchResponse(
      sampleResult.Accepted + workoutResult.Accepted,
      inBatchDuplicates + sampleResult.Duplicates + workoutResult.Duplicates,
      rejected,
      source.Cursor);
  }
}
