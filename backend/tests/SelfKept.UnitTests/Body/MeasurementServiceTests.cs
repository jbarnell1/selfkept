using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Services.Body;
using Xunit;

namespace SelfKept.UnitTests.Body;

/// <summary>MeasurementService on the InMemory provider: typed readings become samples under one "Typed in" source and feed the daily summary.</summary>
public class MeasurementServiceTests : BodyTestBase
{
  private static CreateMeasurementRequest Weight(double value = 84.2, DateTime? at = null) => new(MetricKeys.WeightKg, value, at);

  [Fact]
  public async Task Create_Weight_IsASampleUnderTheTypedInSourceWithTheDaysSummary()
  {
    var reading = await Measurements.CreateAsync(OwnerActor, Weight());

    reading.Should().BeEquivalentTo(new MeasurementDto(reading.Id, MetricKeys.WeightKg, 84.2, "kg", Now.UtcDateTime));
    var source = await Context.Sources.AsNoTracking().SingleAsync();
    source.PersonId.Should().Be(Owner.Id);
    source.Kind.Should().Be(SourceKinds.Manual);
    source.DeviceName.Should().Be(SourceKinds.TypedDeviceName);
    source.SampleCount.Should().Be(1);
    var sample = await Context.Samples.AsNoTracking().SingleAsync();
    sample.SourceUid.Should().Be(MeasurementService.SourceUid(reading.Id));
    var summary = await Context.DailySummaries.AsNoTracking().SingleAsync();
    (summary.MetricKey, summary.Day, summary.Value).Should().Be((MetricKeys.WeightKg, Today, 84.2));
  }

  [Fact]
  public async Task Create_SecondReading_ReusesTheSourceAndAveragesTheDay()
  {
    await Measurements.CreateAsync(OwnerActor, Weight(84.0, Now.UtcDateTime.AddHours(-2)));

    await Measurements.CreateAsync(OwnerActor, Weight(85.0));

    (await Context.Sources.CountAsync()).Should().Be(1);
    (await Context.DailySummaries.AsNoTracking().SingleAsync()).Value.Should().BeApproximately(84.5, 1e-9);
  }

  [Fact]
  public async Task Create_AnyReading_AuditsTheMetric()
  {
    await Measurements.CreateAsync(OwnerActor, new CreateMeasurementRequest(MetricKeys.WaistCm, 88, null));

    var written = Audit.Events.Should().ContainSingle().Subject;
    written.Action.Should().Be(AuditActions.MeasurementCreated);
    RecordingAudit.DetailsJson(written).Should().Be("""{"metric":"waist_cm"}""");
  }

  [Fact]
  public async Task Create_MetricThatCannotBeTypedIn_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Measurements.CreateAsync(OwnerActor, new CreateMeasurementRequest(MetricKeys.Steps, 9000, null)),
      "metricKey must be one of: weight_kg, body_fat_pct, waist_cm.");
  }

  [Fact]
  public async Task Create_WeightOutsideTheRange_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Measurements.CreateAsync(OwnerActor, Weight(19.9)), "Weight must be between 20 and 400 kg.");
    await ShouldBeRefusedAsync(() => Measurements.CreateAsync(OwnerActor, Weight(400.1)), "Weight must be between 20 and 400 kg.");
  }

  [Fact]
  public async Task Create_BodyFatOrWaistOutsideTheRange_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Measurements.CreateAsync(OwnerActor, new CreateMeasurementRequest(MetricKeys.BodyFatPct, 80, null)),
      "Body fat must be between 2 and 75 %.");
    await ShouldBeRefusedAsync(
      () => Measurements.CreateAsync(OwnerActor, new CreateMeasurementRequest(MetricKeys.WaistCm, 20, null)),
      "Waist must be between 30 and 250 cm.");
  }

  [Fact]
  public async Task Create_MoreThanAnHourAhead_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Measurements.CreateAsync(OwnerActor, Weight(at: Now.UtcDateTime.AddMinutes(61))), "at cannot be in the future.");
  }

  [Fact]
  public async Task Create_MoreThanTwoYearsBack_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Measurements.CreateAsync(OwnerActor, Weight(at: Now.UtcDateTime.AddDays(-731))), "at cannot be more than two years ago.");
  }

  [Fact]
  public async Task ListOwn_TypedReadings_AreNewestFirstAndLeaveOutDeviceReadings()
  {
    var older = await Measurements.CreateAsync(OwnerActor, Weight(84.6, Now.UtcDateTime.AddDays(-3)));
    var newer = await Measurements.CreateAsync(OwnerActor, Weight(84.1));
    Context.Samples.Add(new Sample
    {
      Id = Guid.NewGuid(), PersonId = Owner.Id, SourceId = Guid.NewGuid(), SourceUid = "scale:1", MetricKey = MetricKeys.WeightKg,
      StartAt = Now.UtcDateTime.AddDays(-1), Value = 84.4, Unit = "kg", CreatedAt = Now.UtcDateTime,
    });
    await Context.SaveChangesAsync();

    var list = await Measurements.ListOwnAsync(Owner.Id, MetricKeys.WeightKg);

    list.Select(m => m.Id).Should().Equal(newer.Id, older.Id);
  }

  [Fact]
  public async Task ListOwn_SomeoneElsesReadings_AreNotListed()
  {
    await Measurements.CreateAsync(MemberActor, Weight());

    (await Measurements.ListOwnAsync(Owner.Id, MetricKeys.WeightKg)).Should().BeEmpty();
  }

  [Fact]
  public async Task Delete_OwnReading_RemovesItAndRecomputesTheDay()
  {
    await Measurements.CreateAsync(OwnerActor, Weight(84.0, Now.UtcDateTime.AddHours(-2)));
    var second = await Measurements.CreateAsync(OwnerActor, Weight(85.0));

    var removed = await Measurements.DeleteAsync(OwnerActor, MetricKeys.WeightKg, second.Id);

    removed.Should().BeTrue();
    (await Context.Samples.CountAsync()).Should().Be(1);
    (await Context.DailySummaries.AsNoTracking().SingleAsync()).Value.Should().Be(84.0);
    (await Context.Sources.AsNoTracking().SingleAsync()).SampleCount.Should().Be(1);
    Audit.Events.Last().Action.Should().Be(AuditActions.MeasurementDeleted);
  }

  [Fact]
  public async Task Delete_SomeoneElsesReading_IsFalseAndKeepsIt()
  {
    var theirs = await Measurements.CreateAsync(MemberActor, Weight());

    (await Measurements.DeleteAsync(OwnerActor, MetricKeys.WeightKg, theirs.Id)).Should().BeFalse();
    (await Context.Samples.CountAsync()).Should().Be(1);
  }

  [Fact]
  public async Task Delete_UnderTheWrongMetric_IsFalse()
  {
    var reading = await Measurements.CreateAsync(OwnerActor, Weight());

    (await Measurements.DeleteAsync(OwnerActor, MetricKeys.WaistCm, reading.Id)).Should().BeFalse();
  }
}
