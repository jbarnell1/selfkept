using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.ContextEngine;
using Xunit;

namespace SelfKept.UnitTests.ContextEngine;

/// <summary>Derived context: what the record shows on a day, each window at its edge.</summary>
public class ContextDerivationTests
{
  private static readonly DateOnly Day = new(2026, 11, 9);

  private static ContextRecord Record(
    IReadOnlyList<ContextEvent>? events = null,
    IReadOnlyList<Goal>? goals = null,
    IReadOnlyList<Medication>? medications = null,
    IReadOnlyList<LoggedDay>? logged = null,
    IReadOnlyList<StrengthSet>? strength = null,
    IReadOnlyList<WorkoutDay>? workouts = null) =>
    new(events ?? [], goals, medications, logged, strength, workouts);

  private static Goal Goal(DateOnly started, string status = GoalStatuses.Active) =>
    new() { Id = Guid.NewGuid(), Kind = GoalKinds.LoseFat, StartedOn = started, Status = status };

  [Fact]
  public void Active_NothingInTheRecord_IsEmpty() =>
    ContextDerivation.Active(Day, Record()).Should().BeEmpty();

  [Fact]
  public void Active_GoalStartedThirteenDaysAgo_IsANewGoal()
  {
    var active = ContextDerivation.Active(Day, Record(goals: [Goal(Day.AddDays(-13))]));

    var hit = active.Should().ContainSingle().Subject;
    hit.Kind.Should().Be(ContextKinds.NewGoal);
    hit.Source.Should().Be(ContextSources.Derived);
    hit.Reason.Should().Be("Your goal to lose fat started on Oct 27, 2026");
  }

  [Fact]
  public void Active_GoalStartedFourteenDaysAgo_IsNoLongerNew() =>
    ContextDerivation.Active(Day, Record(goals: [Goal(Day.AddDays(-14))])).Should().BeEmpty();

  [Fact]
  public void Active_MedicineStartedAndSupplementStopped_AreBothChanges()
  {
    var medications = new[]
    {
      new Medication { Kind = MedicationKinds.Medication, StartedOn = Day.AddDays(-3) },
      new Medication { Kind = MedicationKinds.Supplement, StartedOn = Day.AddDays(-200), StoppedOn = Day.AddDays(-1) },
    };

    var active = ContextDerivation.Active(Day, Record(medications: medications));

    active.Should().HaveCount(2).And.OnlyContain(a => a.Kind == ContextKinds.MedicationChange);
    active.Select(a => a.Detail).Should().BeEquivalentTo(["started", "stopped"]);
    active.Select(a => a.Reason).Should().Contain("A supplement on your list stopped on Nov 8, 2026");
  }

  [Fact]
  public void Active_MedicineStartedLongAgo_IsNotAChange() =>
    ContextDerivation.Active(Day, Record(medications: [new Medication { StartedOn = Day.AddDays(-30) }])).Should().BeEmpty();

  [Fact]
  public void Active_SoreThroatTwoDaysAgo_IsIllnessSinceThatDay()
  {
    var logged = new[] { new LoggedDay(Day.AddDays(-2), [JournalTags.SoreThroat]), new LoggedDay(Day, [JournalTags.Cough]) };

    var hit = ContextDerivation.Active(Day, Record(logged: logged)).Should().ContainSingle().Subject;

    hit.Kind.Should().Be(ContextKinds.Illness);
    hit.Since.Should().Be(Day.AddDays(-2));
    hit.Detail.Should().Be(JournalTags.SoreThroat);
    hit.Reason.Should().Be("You logged a sore throat on Nov 7, 2026");
  }

  [Fact]
  public void Active_SymptomThreeDaysAgo_IsOutsideTheWindow() =>
    ContextDerivation.Active(Day, Record(logged: [new LoggedDay(Day.AddDays(-3), [JournalTags.Fever])])).Should().BeEmpty();

  [Fact]
  public void Active_StressOrSoreness_IsNotIllness() =>
    ContextDerivation.Active(Day, Record(logged: [new LoggedDay(Day, [JournalTags.Stress, JournalTags.Soreness])])).Should().BeEmpty();

  [Fact]
  public void Active_StrengthSetYesterday_IsHardTraining()
  {
    var hit = ContextDerivation.Active(Day, Record(strength: [new StrengthSet { Day = Day.AddDays(-1), Lift = "squat", WeightKg = 100, Reps = 5 }]))
      .Should().ContainSingle().Subject;

    hit.Kind.Should().Be(ContextKinds.HardTraining);
    hit.Detail.Should().Be("strength");
  }

  [Fact]
  public void Active_LongWorkoutYesterday_IsHardTraining_AShortOneIsNot()
  {
    ContextDerivation.Active(Day, Record(workouts: [new WorkoutDay(Day.AddDays(-1), 75)])).Should().ContainSingle()
      .Which.Reason.Should().Be("A 75 minute workout on Nov 8, 2026");
    ContextDerivation.Active(Day, Record(workouts: [new WorkoutDay(Day.AddDays(-1), 40)])).Should().BeEmpty();
  }

  [Fact]
  public void Active_HardSessionTagYesterday_IsHardTraining() =>
    ContextDerivation.Active(Day, Record(logged: [new LoggedDay(Day.AddDays(-1), [JournalTags.HardSession])]))
      .Should().ContainSingle().Which.Detail.Should().Be(JournalTags.HardSession);

  [Fact]
  public void Active_OpenUserEvent_CountsForFourteenDaysFromItsStart()
  {
    var travel = new ContextEvent { Id = Guid.NewGuid(), Kind = ContextKinds.Travel, StartedOn = Day.AddDays(-13), Source = ContextSources.User };

    ContextDerivation.Active(Day, Record(events: [travel])).Should().ContainSingle().Which.Reason.Should().Be("You logged travel on Oct 27, 2026");
    ContextDerivation.Active(Day.AddDays(1), Record(events: [travel])).Should().BeEmpty();
  }

  [Fact]
  public void Active_EndedUserEvent_StopsAfterItsEnd()
  {
    var ill = new ContextEvent { Kind = ContextKinds.Illness, StartedOn = Day.AddDays(-5), EndedOn = Day.AddDays(-1) };

    ContextDerivation.Active(Day, Record(events: [ill])).Should().BeEmpty();
    ContextDerivation.Active(Day.AddDays(-1), Record(events: [ill])).Should().ContainSingle();
  }

  [Fact]
  public void Active_UnreadableParts_DeriveNothing()
  {
    // Null parts are what a viewer without the grant gets; nothing is derived from them.
    var record = new ContextRecord([], null, null, null, null, null);
    ContextDerivation.Active(Day, record).Should().BeEmpty();
  }

  [Fact]
  public void HardTrainingDays_ListsEachKind_InTheLookback()
  {
    var record = Record(
      strength: [new StrengthSet { Day = Day.AddDays(-1), Lift = "squat", WeightKg = 100, Reps = 5 }],
      workouts: [new WorkoutDay(Day.AddDays(-3), 90), new WorkoutDay(Day.AddDays(-20), 90)],
      events: [new ContextEvent { Kind = ContextKinds.HardTraining, StartedOn = Day.AddDays(-5), EndedOn = Day.AddDays(-5) }]);

    ContextDerivation.HardTrainingDays(Day, record).Should().BeEquivalentTo([Day.AddDays(-1), Day.AddDays(-3), Day.AddDays(-5)]);
  }
}
