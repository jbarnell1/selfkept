using System.Text.Json;
using FluentAssertions;
using Moq;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;
using Xunit;

namespace SelfKept.UnitTests.Collect;

public class BatchValidatorTests
{
  private static readonly SeedCatalogue Catalogue = new();
  private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
  private static readonly Source Device = new() { Id = Guid.NewGuid(), PersonId = Guid.NewGuid(), Kind = "healthkit", Enabled = true };

  private static SampleInput Input(
    string? uid = "hk-1", string? metric = "heart_rate_bpm", DateTime? start = null, DateTime? end = null,
    double? value = 60, string? unit = "bpm", JsonElement? meta = null) =>
    new(uid, metric, start ?? Now.AddHours(-1), end, value, unit, meta);

  [Fact]
  public void ValidateSample_ValidInstantReading_ProducesSampleForTheSourcePerson()
  {
    var outcome = BatchValidator.ValidateSample(Input(meta: JsonDocument.Parse("""{"motion":"sedentary"}""").RootElement), Catalogue, Device, Now);

    outcome.Accepted.Should().BeTrue(outcome.Reason);
    outcome.Sample!.PersonId.Should().Be(Device.PersonId);
    outcome.Sample.SourceId.Should().Be(Device.Id);
    outcome.Sample.MetricKey.Should().Be("heart_rate_bpm");
    outcome.Sample.Unit.Should().Be("bpm");
    outcome.Sample.Meta.Should().Contain("sedentary");
    outcome.Sample.StartAt.Kind.Should().Be(DateTimeKind.Utc);
  }

  [Fact]
  public void ValidateSample_UnknownMetric_IsRejectedWithTheKey()
  {
    var outcome = BatchValidator.ValidateSample(Input(metric: "bogus_metric"), Catalogue, Device, Now);

    outcome.Accepted.Should().BeFalse();
    outcome.Reason.Should().Contain("unknown metric 'bogus_metric'");
  }

  [Fact]
  public void ValidateSample_LabMetricViaIngest_IsRejected()
  {
    var outcome = BatchValidator.ValidateSample(Input(metric: "lab.apob", unit: "mg/dL", value: 80), Catalogue, Device, Now);

    outcome.Accepted.Should().BeFalse();
    outcome.Reason.Should().Contain("labs endpoints");
  }

  [Fact]
  public void ValidateSample_CheckinMoodViaIngest_IsRejected()
  {
    var outcome = BatchValidator.ValidateSample(Input(metric: "checkin_mood", unit: "level", value: 4), Catalogue, Device, Now);

    outcome.Reason.Should().Contain("POST /api/checkins");
  }

  [Fact]
  public void ValidateSample_IntervalMetricWithoutEnd_IsRejected()
  {
    var outcome = BatchValidator.ValidateSample(Input(metric: "steps", unit: "count", value: 100), Catalogue, Device, Now);

    outcome.Accepted.Should().BeFalse();
    outcome.Reason.Should().Contain("end is required for interval metric 'steps'");
  }

  [Fact]
  public void ValidateSample_EndBeforeStart_IsRejected()
  {
    var outcome = BatchValidator.ValidateSample(Input(metric: "steps", unit: "count", value: 100, start: Now, end: Now.AddMinutes(-5)), Catalogue, Device, Now);

    outcome.Reason.Should().Be("end is before start");
  }

  [Fact]
  public void ValidateSample_UnitNotAccepted_IsRejectedNamingTheExpectedUnit()
  {
    var outcome = BatchValidator.ValidateSample(Input(metric: "weight_kg", unit: "stone", value: 11), Catalogue, Device, Now);

    outcome.Accepted.Should().BeFalse();
    outcome.Reason.Should().Contain("'stone'").And.Contain("expected 'kg'");
  }

  [Fact]
  public void ValidateSample_AllowlistedUnit_IsConvertedAndStoredInCatalogueUnit()
  {
    var outcome = BatchValidator.ValidateSample(Input(metric: "weight_kg", unit: "lb", value: 200), Catalogue, Device, Now);

    outcome.Accepted.Should().BeTrue(outcome.Reason);
    outcome.Sample!.Unit.Should().Be("kg");
    outcome.Sample.Value.Should().BeApproximately(90.718474, 0.0001);
  }

  [Theory]
  [InlineData(7)]
  [InlineData(2.5)]
  [InlineData(-1)]
  public void ValidateSample_SleepStageOutsideTheDefinedLevels_IsRejected(double value)
  {
    var outcome = BatchValidator.ValidateSample(Input(metric: "sleep_stage", unit: "stage", value: value, end: Now), Catalogue, Device, Now);

    outcome.Accepted.Should().BeFalse();
    outcome.Reason.Should().Contain("not one of the levels");
  }

  [Fact]
  public void ValidateSample_SleepStageDeep_IsAccepted()
  {
    var outcome = BatchValidator.ValidateSample(Input(metric: "sleep_stage", unit: "stage", value: 3, start: Now.AddHours(-8), end: Now.AddHours(-7)), Catalogue, Device, Now);

    outcome.Accepted.Should().BeTrue(outcome.Reason);
  }

  [Theory]
  [InlineData(null, "sourceUid is required")]
  [InlineData("", "sourceUid is required")]
  public void ValidateSample_MissingSourceUid_IsRejected(string? uid, string expected)
  {
    BatchValidator.ValidateSample(Input(uid: uid), Catalogue, Device, Now).Reason.Should().Be(expected);
  }

  [Fact]
  public void ValidateSample_MissingStart_IsRejected()
  {
    var input = new SampleInput("hk-1", "heart_rate_bpm", null, null, 60, "bpm", null);

    BatchValidator.ValidateSample(input, Catalogue, Device, Now).Reason.Should().Be("start is required");
  }

  [Theory]
  [InlineData(double.NaN)]
  [InlineData(double.PositiveInfinity)]
  public void ValidateSample_NonFiniteValue_IsRejected(double value)
  {
    BatchValidator.ValidateSample(Input(value: value), Catalogue, Device, Now).Reason.Should().Contain("finite");
  }

  [Fact]
  public void ValidateSample_MissingValue_IsRejected()
  {
    BatchValidator.ValidateSample(Input(value: null), Catalogue, Device, Now).Reason.Should().Contain("finite");
  }

  [Fact]
  public void ValidateSample_StartFarInTheFuture_IsRejected()
  {
    BatchValidator.ValidateSample(Input(start: Now.AddDays(3)), Catalogue, Device, Now).Reason.Should().Contain("future");
  }

  [Fact]
  public void ValidateWorkout_Valid_EncryptsTheRoute()
  {
    var input = new WorkoutInput("hk-w1", "Running", Now.AddHours(-2), Now.AddHours(-1), 10020, 612, 148, 171,
      JsonDocument.Parse("""{"type":"LineString","coordinates":[[0,0],[1,1]]}""").RootElement, null);

    var outcome = BatchValidator.ValidateWorkout(input, Device, Now, route => [(byte)route.Length]);

    outcome.Accepted.Should().BeTrue(outcome.Reason);
    outcome.Workout!.Type.Should().Be("running");
    outcome.Workout.RouteCipher.Should().NotBeNull();
    outcome.Workout.DistanceM.Should().Be(10020);
  }

  [Fact]
  public void ValidateWorkout_WithoutEnd_IsRejected()
  {
    var input = new WorkoutInput("hk-w1", "running", Now.AddHours(-2), null, null, null, null, null, null, null);

    BatchValidator.ValidateWorkout(input, Device, Now, _ => []).Reason.Should().Contain("end is required");
  }
}

/// <summary>The batch-level rules live in IngestService: the cap, in-batch duplicates and counter updates.</summary>
public class IngestServiceBatchTests
{
  private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
  private readonly Guid _personId = Guid.NewGuid();
  private readonly Source _source;
  private readonly Mock<ISourceRepository> _sources = new();
  private readonly Mock<ISampleRepository> _samples = new();
  private readonly Mock<IWorkoutRepository> _workouts = new();
  private readonly Mock<IPersonRepository> _persons = new();
  private readonly Mock<IDailySummaryService> _summaries = new();
  private readonly Mock<IEncryptionService> _encryption = new();
  private readonly Mock<IUnitOfWork> _unitOfWork = new();

  public IngestServiceBatchTests()
  {
    _source = new Source { Id = Guid.NewGuid(), PersonId = _personId, Kind = "healthkit", Enabled = true, SampleCount = 10 };
    _sources.Setup(s => s.GetAsync(_personId, _source.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_source);
    _persons.Setup(p => p.GetByIdAsync(_personId, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new Person { Id = _personId, TimeZone = "UTC" });
    _samples.Setup(s => s.InsertManyAsync(It.IsAny<IReadOnlyList<Sample>>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((IReadOnlyList<Sample> list, CancellationToken _) => new BulkInsertResult(list.Count, 0));
    _workouts.Setup(w => w.InsertManyAsync(It.IsAny<IReadOnlyList<Workout>>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((IReadOnlyList<Workout> list, CancellationToken _) => new BulkInsertResult(list.Count, 0));
    _unitOfWork.Setup(u => u.RunInTransactionAsync(It.IsAny<Func<Task<(BulkInsertResult, BulkInsertResult)>>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
      .Returns((Func<Task<(BulkInsertResult, BulkInsertResult)>> work, long? _, CancellationToken _) => work());
  }

  private IngestService CreateService() => new(
    _sources.Object, _samples.Object, _workouts.Object, _persons.Object, new SeedCatalogue(), _summaries.Object,
    _encryption.Object, _unitOfWork.Object, new Mock<ITodayCacheRepository>().Object, new TestClock(Now));

  private static SampleInput Hr(string uid, int minutesAgo = 60, double bpm = 60) =>
    new(uid, "heart_rate_bpm", Now.AddMinutes(-minutesAgo), null, bpm, "bpm", null);

  [Fact]
  public async Task Ingest_OverTheCap_IsRefusedBeforeTouchingTheDatabase()
  {
    var samples = Enumerable.Range(0, BatchValidator.MaxBatchItems + 1).Select(i => Hr($"hk-{i}")).ToList();

    var act = () => CreateService().IngestAsync(_personId, new IngestBatchRequest(_source.Id, null, samples, null));

    await act.Should().ThrowAsync<DomainValidationException>().WithMessage("*5,000*");
    _sources.Verify(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task Ingest_UnknownSource_IsNull()
  {
    var result = await CreateService().IngestAsync(_personId, new IngestBatchRequest(Guid.NewGuid(), null, [Hr("hk-1")], null));

    result.Should().BeNull();
  }

  [Fact]
  public async Task Ingest_DisabledSource_IsAConflict()
  {
    _source.Enabled = false;

    var act = () => CreateService().IngestAsync(_personId, new IngestBatchRequest(_source.Id, null, [Hr("hk-1")], null));

    await act.Should().ThrowAsync<ConflictException>();
  }

  [Fact]
  public async Task Ingest_MixedBatch_CountsAcceptedDuplicatesAndRejectedSeparately()
  {
    var request = new IngestBatchRequest(_source.Id, "cursor-2", new[]
    {
      Hr("hk-1"),
      Hr("hk-2", 30),
      Hr("hk-2", 30), // same sourceUid twice in one batch
      new SampleInput("hk-3", "nope", Now, null, 1, "x", null),
      new SampleInput("hk-4", "steps", Now.AddHours(-1), null, 100, "count", null), // interval without end
    }, null);

    var result = await CreateService().IngestAsync(_personId, request);

    result.Should().NotBeNull();
    result!.Accepted.Should().Be(2);
    result.Duplicates.Should().Be(1);
    result.Rejected.Should().HaveCount(2);
    result.Rejected.Select(r => r.SourceUid).Should().BeEquivalentTo(new[] { "hk-3", "hk-4" });
    result.Cursor.Should().Be("cursor-2");

    _source.SampleCount.Should().Be(12);
    _source.Cursor.Should().Be("cursor-2");
    _source.LastSyncAt.Should().Be(Now);
    _source.LastSampleAt.Should().Be(Now.AddMinutes(-30));
    _samples.Verify(s => s.EnsurePartitionsAsync(It.IsAny<IEnumerable<DateOnly>>(), It.IsAny<CancellationToken>()), Times.Once);
    _summaries.Verify(s => s.RecomputeAsync(_personId, "UTC",
      It.Is<IReadOnlyCollection<(string, DateOnly)>>(t => t.Count == 1 && t.First().Item1 == "heart_rate_bpm" && t.First().Item2 == new DateOnly(2026, 10, 29)),
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task Ingest_EverythingAlreadyStored_ReportsDuplicatesAndLeavesCountersAlone()
  {
    _samples.Setup(s => s.InsertManyAsync(It.IsAny<IReadOnlyList<Sample>>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((IReadOnlyList<Sample> list, CancellationToken _) => new BulkInsertResult(0, list.Count));

    var result = await CreateService().IngestAsync(_personId, new IngestBatchRequest(_source.Id, null, [Hr("hk-1"), Hr("hk-2")], null));

    result!.Accepted.Should().Be(0);
    result.Duplicates.Should().Be(2);
    _source.SampleCount.Should().Be(10);
    _source.LastSyncAt.Should().Be(Now, "a sync happened even though nothing was new");
    _summaries.Verify(s => s.RecomputeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<(string, DateOnly)>>(), It.IsAny<CancellationToken>()), Times.Never);
  }
}
