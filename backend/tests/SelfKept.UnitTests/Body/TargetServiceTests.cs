using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using Xunit;

namespace SelfKept.UnitTests.Body;

/// <summary>TargetService on the InMemory provider with the real repositories: validation, defaults, the cap and ownership.</summary>
public class TargetServiceTests : BodyTestBase
{
  private static SaveTargetRequest Protein(double value = 130) =>
    new(null, "Protein", TargetKinds.Metric, MetricKeys.ProteinG, null, TargetComparators.AtLeast, value, TargetPeriods.Day, null, null);

  private static SaveTargetRequest NoAlcohol() =>
    new(null, "No alcohol", TargetKinds.Journal, null, JournalTags.Alcohol, TargetComparators.AtMost, 0, TargetPeriods.Week, null, null);

  private static SaveTargetRequest LiftingDays() =>
    new(null, "Lifting days", TargetKinds.Strength, null, null, TargetComparators.AtLeast, 2, TargetPeriods.Week, null, null);

  private async Task SeedCacheAsync(Guid personId)
  {
    Context.TodayCaches.Add(new TodayCache { PersonId = personId, ComputedAt = Now.UtcDateTime, Sentence = "Cached." });
    await Context.SaveChangesAsync();
    Context.ChangeTracker.Clear();
  }

  // --- create ---

  [Fact]
  public async Task Create_MetricTarget_IsSavedWithTheMetricsUnit()
  {
    var target = await Targets.CreateAsync(OwnerActor, Protein());

    target.Label.Should().Be("Protein");
    target.Unit.Should().Be("g");
    target.Active.Should().BeTrue();
    target.SortOrder.Should().Be(0);
    (await Context.DailyTargets.AsNoTracking().SingleAsync()).PersonId.Should().Be(Owner.Id);
  }

  [Fact]
  public async Task Create_SleepTarget_IsCountedInMinutes()
  {
    var target = await Targets.CreateAsync(OwnerActor, Protein() with { Label = "Sleep", MetricKey = MetricKeys.SleepStage, Value = 420 });

    target.Unit.Should().Be("min");
  }

  [Fact]
  public async Task Create_JournalAndStrengthTargets_NameWhatTheyCount()
  {
    var journal = await Targets.CreateAsync(OwnerActor, NoAlcohol());
    var strength = await Targets.CreateAsync(OwnerActor, LiftingDays());

    journal.Unit.Should().Be("entries");
    strength.Unit.Should().Be("days");
  }

  [Fact]
  public async Task Create_NoSortOrder_IsPlacedAfterTheExistingTargets()
  {
    await Targets.CreateAsync(OwnerActor, Protein());
    await Targets.CreateAsync(OwnerActor, NoAlcohol());

    var third = await Targets.CreateAsync(OwnerActor, LiftingDays());

    third.SortOrder.Should().Be(2);
  }

  [Fact]
  public async Task Create_LabelWithSpaces_IsTrimmed()
  {
    var target = await Targets.CreateAsync(OwnerActor, Protein() with { Label = "  Protein  " });

    target.Label.Should().Be("Protein");
  }

  [Fact]
  public async Task Create_AnyTarget_AuditsKindAndPeriodOnlyAndDropsTheTodayCache()
  {
    await SeedCacheAsync(Owner.Id);

    var target = await Targets.CreateAsync(OwnerActor, NoAlcohol());

    var written = Audit.Events.Should().ContainSingle().Subject;
    written.Action.Should().Be(AuditActions.TargetSaved);
    written.TargetId.Should().Be(target.Id.ToString());
    RecordingAudit.DetailsJson(written).Should().Be("""{"kind":"journal","period":"week"}""");
    (await Context.TodayCaches.AnyAsync()).Should().BeFalse();
  }

  [Fact]
  public async Task Create_ThirteenthTarget_IsRefused()
  {
    for (var i = 0; i < 12; i++) await Targets.CreateAsync(OwnerActor, Protein(100 + i));

    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, Protein()), "At most 12 targets. Remove one first.");
    (await Context.DailyTargets.CountAsync()).Should().Be(12);
  }

  [Fact]
  public async Task Create_TwelveTargetsBelongToSomeoneElse_IsAccepted()
  {
    for (var i = 0; i < 12; i++) await Targets.CreateAsync(MemberActor, Protein(100 + i));

    var target = await Targets.CreateAsync(OwnerActor, Protein());

    target.Should().NotBeNull();
  }

  [Fact]
  public async Task Create_EmptyLabel_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, Protein() with { Label = "   " }), "A label is 1 to 48 characters.");
  }

  [Fact]
  public async Task Create_LabelOverFortyEightCharacters_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, Protein() with { Label = new string('a', 49) }), "A label is 1 to 48 characters.");
  }

  [Fact]
  public async Task Create_UnknownKindComparatorOrPeriod_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, Protein() with { Kind = "habit" }), "kind must be one of: metric, journal, strength.");
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, Protein() with { Comparator = "exactly" }), "comparator must be one of: at_least, at_most.");
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, Protein() with { Period = "month" }), "period must be one of: day, week.");
  }

  [Fact]
  public async Task Create_NegativeOrNonFiniteValue_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, Protein(-1)), "value must be zero or more.");
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, Protein(double.NaN)), "value must be zero or more.");
  }

  [Fact]
  public async Task Create_MetricTargetWithATag_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, Protein() with { Tag = JournalTags.Alcohol }), "A metric target has no tag.");
  }

  [Fact]
  public async Task Create_MetricNotInTheCatalogue_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, Protein() with { MetricKey = "made_up" }), "Unknown metric 'made_up'.");
  }

  [Fact]
  public async Task Create_LabMarker_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Targets.CreateAsync(OwnerActor, Protein() with { MetricKey = "lab.ldl" }),
      "'lab.ldl' has no daily figure to set a target on.");
  }

  [Fact]
  public async Task Create_MetricWithNoDailyRule_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Targets.CreateAsync(OwnerActor, Protein() with { MetricKey = "height_cm" }),
      "'height_cm' has no daily figure to set a target on.");
  }

  [Fact]
  public async Task Create_JournalTargetWithAMetric_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, NoAlcohol() with { MetricKey = MetricKeys.ProteinG }), "A day log target has no metric.");
  }

  [Fact]
  public async Task Create_JournalTargetWithATagOfTheirOwn_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, NoAlcohol() with { Tag = "pizza" }), "Unknown tag 'pizza'.");
  }

  [Fact]
  public async Task Create_StrengthTargetWithAMetricOrTag_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, LiftingDays() with { Tag = JournalTags.Lifted }), "A strength target has no metric or tag.");
    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, LiftingDays() with { MetricKey = MetricKeys.Steps }), "A strength target has no metric or tag.");
  }

  [Fact]
  public async Task Create_OwnGoal_IsLinked()
  {
    var goal = await Goals.CreateAsync(OwnerActor, new SaveGoalRequest(GoalSlots.Main, GoalKinds.LoseFat, null, null, 90, 80, null, null, null));

    var target = await Targets.CreateAsync(OwnerActor, Protein() with { GoalId = goal.Id });

    target.GoalId.Should().Be(goal.Id);
  }

  [Fact]
  public async Task Create_SomeoneElsesGoal_IsRefused()
  {
    var theirs = await Goals.CreateAsync(MemberActor, new SaveGoalRequest(GoalSlots.Main, GoalKinds.Maintain, null, null, null, null, null, null, null));

    await ShouldBeRefusedAsync(() => Targets.CreateAsync(OwnerActor, Protein() with { GoalId = theirs.Id }), "No such goal.");
  }

  // --- update and delete ---

  [Fact]
  public async Task Update_NoSortOrder_KeepsTheStoredOneAndNoActiveMeansActive()
  {
    await Targets.CreateAsync(OwnerActor, Protein());
    var second = await Targets.CreateAsync(OwnerActor, NoAlcohol() with { Active = false });

    var updated = await Targets.UpdateAsync(OwnerActor, second.Id, NoAlcohol() with { Value = 1 });

    updated!.SortOrder.Should().Be(1);
    updated.Active.Should().BeTrue();
    updated.Value.Should().Be(1);
  }

  [Fact]
  public async Task Update_SomeoneElsesTarget_IsNullAndChangesNothing()
  {
    var theirs = await Targets.CreateAsync(MemberActor, Protein());

    var updated = await Targets.UpdateAsync(OwnerActor, theirs.Id, Protein(999));

    updated.Should().BeNull();
    (await Context.DailyTargets.AsNoTracking().SingleAsync()).Value.Should().Be(130);
  }

  [Fact]
  public async Task Update_InvalidRequest_IsRefusedAndChangesNothing()
  {
    var target = await Targets.CreateAsync(OwnerActor, Protein());
    Context.ChangeTracker.Clear();

    await ShouldBeRefusedAsync(() => Targets.UpdateAsync(OwnerActor, target.Id, Protein(-5)), "value must be zero or more.");
    (await Context.DailyTargets.AsNoTracking().SingleAsync()).Value.Should().Be(130);
  }

  [Fact]
  public async Task Delete_OwnTarget_RemovesItAndAudits()
  {
    var target = await Targets.CreateAsync(OwnerActor, LiftingDays());

    var removed = await Targets.DeleteAsync(OwnerActor, target.Id);

    removed.Should().BeTrue();
    (await Context.DailyTargets.AnyAsync()).Should().BeFalse();
    Audit.Events.Last().Action.Should().Be(AuditActions.TargetDeleted);
  }

  [Fact]
  public async Task Delete_SomeoneElsesTarget_IsFalseAndKeepsIt()
  {
    var theirs = await Targets.CreateAsync(MemberActor, LiftingDays());

    var removed = await Targets.DeleteAsync(OwnerActor, theirs.Id);

    removed.Should().BeFalse();
    (await Context.DailyTargets.CountAsync()).Should().Be(1);
  }

  [Fact]
  public async Task Tally_InactiveTarget_IsLeftOut()
  {
    await Targets.CreateAsync(OwnerActor, Protein());
    await Targets.CreateAsync(OwnerActor, NoAlcohol() with { Active = false });

    var tally = await Targets.TallyAsync(Owner.Id, Owner.Id);

    tally!.Day.Should().Be(Today);
    tally.Targets.Should().ContainSingle().Which.Target.Label.Should().Be("Protein");
  }
}
