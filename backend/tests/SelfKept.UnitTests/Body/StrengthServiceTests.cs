using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using Xunit;

namespace SelfKept.UnitTests.Body;

/// <summary>StrengthService on the InMemory provider: one set per lift per day, validation and the grouped list.</summary>
public class StrengthServiceTests : BodyTestBase
{
  private static SaveStrengthSetRequest Squat(double weightKg = 100, int reps = 5, DateOnly? day = null, double? rpe = null) =>
    new(Lifts.Squat, weightKg, reps, rpe, day);

  [Fact]
  public async Task Save_NoDay_IsToday()
  {
    var set = await Strength.SaveAsync(OwnerActor, Squat());

    set.Day.Should().Be(Today);
    set.LiftName.Should().Be("Squat");
    set.EstimatedMaxKg.Should().BeApproximately(116.67, 0.01);
  }

  [Fact]
  public async Task Save_SameLiftSameDay_ReplacesTheSetAndKeepsItsId()
  {
    var first = await Strength.SaveAsync(OwnerActor, Squat(100, 5));

    var second = await Strength.SaveAsync(OwnerActor, Squat(110, 3));

    second.Id.Should().Be(first.Id);
    var row = await Context.StrengthSets.AsNoTracking().SingleAsync();
    row.WeightKg.Should().Be(110);
    row.Reps.Should().Be(3);
  }

  [Fact]
  public async Task Save_SameLiftAnotherDay_AddsASet()
  {
    await Strength.SaveAsync(OwnerActor, Squat(day: Today.AddDays(-1)));

    await Strength.SaveAsync(OwnerActor, Squat());

    (await Context.StrengthSets.CountAsync()).Should().Be(2);
  }

  [Fact]
  public async Task Save_SameLiftSameDayForSomeoneElse_KeepsBothSets()
  {
    await Strength.SaveAsync(MemberActor, Squat());

    await Strength.SaveAsync(OwnerActor, Squat());

    (await Context.StrengthSets.CountAsync()).Should().Be(2);
  }

  [Fact]
  public async Task Save_LiftWrittenLoosely_IsNormalised()
  {
    var set = await Strength.SaveAsync(OwnerActor, Squat() with { Lift = " Bench-Press " });
    var own = await Strength.SaveAsync(OwnerActor, Squat() with { Lift = "Hip thrust" });

    set.Lift.Should().Be(Lifts.BenchPress);
    set.LiftName.Should().Be("Bench press");
    own.Lift.Should().Be("hip_thrust");
    own.LiftName.Should().Be("Hip thrust");
  }

  [Fact]
  public async Task Save_BodyweightSet_HasNoEstimate()
  {
    var set = await Strength.SaveAsync(OwnerActor, Squat(0, 10) with { Lift = Lifts.PullUp });

    set.EstimatedMaxKg.Should().BeNull();
  }

  [Fact]
  public async Task Save_AnySet_AuditsTheLift()
  {
    await Strength.SaveAsync(OwnerActor, Squat());

    var written = Audit.Events.Should().ContainSingle().Subject;
    written.Action.Should().Be(AuditActions.StrengthSaved);
    RecordingAudit.DetailsJson(written).Should().Be("""{"lift":"squat"}""");
  }

  [Fact]
  public async Task Save_BadLift_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Strength.SaveAsync(OwnerActor, Squat() with { Lift = "squat!" }), "lift is 1 to 32 letters, digits or underscores.");
    await ShouldBeRefusedAsync(() => Strength.SaveAsync(OwnerActor, Squat() with { Lift = new string('a', 33) }), "lift is 1 to 32 letters, digits or underscores.");
  }

  [Fact]
  public async Task Save_WeightOutsideTheRange_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Strength.SaveAsync(OwnerActor, Squat(-1)), "weightKg must be between 0 and 1000.");
    await ShouldBeRefusedAsync(() => Strength.SaveAsync(OwnerActor, Squat(1000.5)), "weightKg must be between 0 and 1000.");
  }

  [Fact]
  public async Task Save_RepsOutsideTheRange_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Strength.SaveAsync(OwnerActor, Squat(reps: 0)), "reps must be between 1 and 100.");
    await ShouldBeRefusedAsync(() => Strength.SaveAsync(OwnerActor, Squat(reps: 101)), "reps must be between 1 and 100.");
  }

  [Fact]
  public async Task Save_RpeOutsideTheRange_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Strength.SaveAsync(OwnerActor, Squat(rpe: 11)), "rpe must be between 1 and 10.");
  }

  [Fact]
  public async Task Save_Tomorrow_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Strength.SaveAsync(OwnerActor, Squat(day: Today.AddDays(1))), "day cannot be in the future.");
  }

  [Fact]
  public async Task Save_MoreThanFiveYearsAgo_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Strength.SaveAsync(OwnerActor, Squat(day: Today.AddYears(-5).AddDays(-1))), "day cannot be more than 5 years ago.");
  }

  [Fact]
  public async Task List_SetsOfSeveralLifts_AreGroupedCommonLiftsFirstWithTheChange()
  {
    await Strength.SaveAsync(OwnerActor, Squat(100, 5, Today.AddDays(-20)));
    await Strength.SaveAsync(OwnerActor, Squat(110, 5, Today.AddDays(-2)));
    await Strength.SaveAsync(OwnerActor, Squat(60, 8, Today.AddDays(-3)) with { Lift = "hip_thrust" });
    await Strength.SaveAsync(OwnerActor, Squat(140, 3, Today.AddDays(-5)) with { Lift = Lifts.Deadlift });

    var lifts = await Strength.ListAsync(Owner.Id, Owner.Id, null, null);

    lifts!.Select(l => l.Lift).Should().Equal(Lifts.Squat, Lifts.Deadlift, "hip_thrust");
    var squat = lifts![0];
    squat.Sets.Select(s => s.Day).Should().Equal(Today.AddDays(-20), Today.AddDays(-2));
    squat.BestEstimatedMaxKg.Should().BeApproximately(128.33, 0.01);
    squat.ChangePct.Should().BeApproximately(10, 0.001);
    lifts[2].ChangePct.Should().BeNull("one set has nothing to compare with");
  }

  [Fact]
  public async Task List_SetOlderThanTheWindow_IsLeftOut()
  {
    await Strength.SaveAsync(OwnerActor, Squat(day: Today.AddDays(-120)));
    await Strength.SaveAsync(OwnerActor, Squat(day: Today.AddDays(-119)));

    var lifts = await Strength.ListAsync(Owner.Id, Owner.Id, null, null);

    lifts!.Single().Sets.Should().ContainSingle().Which.Day.Should().Be(Today.AddDays(-119));
  }

  [Fact]
  public async Task Delete_SomeoneElsesSet_IsFalseAndKeepsIt()
  {
    var theirs = await Strength.SaveAsync(MemberActor, Squat());

    (await Strength.DeleteAsync(OwnerActor, theirs.Id)).Should().BeFalse();
    (await Context.StrengthSets.CountAsync()).Should().Be(1);
  }

  [Fact]
  public async Task Delete_OwnSet_RemovesItAndAuditsTheLift()
  {
    var set = await Strength.SaveAsync(OwnerActor, Squat());

    (await Strength.DeleteAsync(OwnerActor, set.Id)).Should().BeTrue();
    (await Context.StrengthSets.AnyAsync()).Should().BeFalse();
    Audit.Events.Last().Action.Should().Be(AuditActions.StrengthDeleted);
  }
}
