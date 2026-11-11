using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services;
using SelfKept.Infrastructure.Services.Body;
using Xunit;

namespace SelfKept.UnitTests.Body;

/// <summary>GoalService on the InMemory provider with the real repository: one active goal per slot, validation, ending.</summary>
public class GoalServiceTests : BodyTestBase
{
  private static SaveGoalRequest LoseFat(string slot = GoalSlots.Main, string? title = null) =>
    new(slot, GoalKinds.LoseFat, title, MetricKeys.WeightKg, 90, 80, -0.5, null, null);

  private Task<List<Goal>> RowsAsync(Guid personId) =>
    Context.Goals.AsNoTracking().Where(g => g.PersonId == personId).OrderBy(g => g.CreatedAt).ThenBy(g => g.Id).ToListAsync();

  // --- create ---

  [Fact]
  public async Task Create_ValidGoal_IsActiveAndStartsToday()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat() with { TargetDate = Today.AddDays(140) });

    goal.Should().BeEquivalentTo(new GoalDto(
      goal.Id, GoalSlots.Main, GoalKinds.LoseFat, "Lose fat", MetricKeys.WeightKg, 90, 80, -0.5, Today, Today.AddDays(140), GoalStatuses.Active, null));
    var row = (await RowsAsync(Owner.Id)).Should().ContainSingle().Subject;
    row.Id.Should().Be(goal.Id);
    row.TitleCipher.Should().BeNull("the kind says it all");
    row.CreatedAt.Should().Be(Now.UtcDateTime);
  }

  [Fact]
  public async Task Create_TitleOfTheirOwn_IsTrimmedAndStoredEncrypted()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat(title: "  Back in the blue suit  "));

    goal.Title.Should().Be("Back in the blue suit");
    var row = (await RowsAsync(Owner.Id)).Single();
    row.TitleCipher.Should().StartWith(EncryptionService.CurrentPrefix).And.NotContain("blue suit");
    Encryption.Decrypt(row.TitleCipher!).Should().Be("Back in the blue suit");
  }

  [Fact]
  public async Task Create_AnyGoal_AuditsSlotKindAndStatusButNeverTheTitle()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat(title: "Back in the blue suit"));

    var written = Audit.Events.Should().ContainSingle().Subject;
    written.Action.Should().Be(AuditActions.GoalSaved);
    written.ActorUserId.Should().Be(OwnerActor.UserId);
    written.HouseholdId.Should().Be(Household);
    written.TargetType.Should().Be("goal");
    written.TargetId.Should().Be(goal.Id.ToString());
    RecordingAudit.DetailsJson(written).Should().Be("""{"slot":"main","kind":"lose_fat","status":"active"}""");
    Audit.Everything().Should().NotContain("blue suit");
  }

  [Fact]
  public async Task Create_SlotAlreadyHasAnActiveGoal_EndsTheOldOneAsDropped()
  {
    var first = await Goals.CreateAsync(OwnerActor, LoseFat());
    Clock.Advance(TimeSpan.FromDays(1));

    var second = await Goals.CreateAsync(OwnerActor, new SaveGoalRequest(GoalSlots.Main, GoalKinds.Maintain, null, null, null, null, null, null, null));

    var active = await Goals.ListAsync(Owner.Id, Owner.Id, includeEnded: false);
    active.Should().ContainSingle().Which.Id.Should().Be(second.Id, "a slot holds one active goal");
    var all = await Goals.ListAsync(Owner.Id, Owner.Id, includeEnded: true);
    var old = all!.Single(g => g.Id == first.Id);
    old.Status.Should().Be(GoalStatuses.Dropped);
    old.EndedAt.Should().Be(Now.UtcDateTime.AddDays(1));
  }

  [Fact]
  public async Task Create_OtherSlot_KeepsBothActive()
  {
    var main = await Goals.CreateAsync(OwnerActor, LoseFat());

    var secondary = await Goals.CreateAsync(OwnerActor, new SaveGoalRequest(GoalSlots.Secondary, GoalKinds.KeepStrength, null, null, null, null, null, null, null));

    var active = await Goals.ListAsync(Owner.Id, Owner.Id, includeEnded: false);
    active!.Select(g => g.Id).Should().Equal(main.Id, secondary.Id);
    active.Should().OnlyContain(g => g.Status == GoalStatuses.Active);
  }

  [Fact]
  public async Task Create_SomeoneElseHasAGoalInTheSlot_LeavesTheirsAlone()
  {
    var theirs = await Goals.CreateAsync(MemberActor, LoseFat());

    await Goals.CreateAsync(OwnerActor, LoseFat());

    var row = (await RowsAsync(Member.Id)).Should().ContainSingle().Subject;
    row.Id.Should().Be(theirs.Id);
    row.Status.Should().Be(GoalStatuses.Active);
    row.EndedAt.Should().BeNull();
  }

  [Fact]
  public async Task Create_NoStartDay_UsesTodayWhereThePersonLives()
  {
    Owner.TimeZone = "America/New_York";
    await Context.SaveChangesAsync();
    Clock.Advance(TimeSpan.FromHours(-6));

    var goal = await Goals.CreateAsync(OwnerActor, LoseFat());

    goal.StartedOn.Should().Be(Today.AddDays(-1), "03:00 UTC is still the evening before in New York");
  }

  [Fact]
  public async Task Create_StartDayGiven_KeepsIt()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat() with { StartedOn = Today.AddDays(-20) });

    goal.StartedOn.Should().Be(Today.AddDays(-20));
  }

  [Fact]
  public async Task Create_AccountWithoutAPerson_IsRefused()
  {
    var nobody = new Actor(Guid.NewGuid(), Guid.NewGuid(), Household, RequestInfo.None);

    await ShouldBeRefusedAsync(() => Goals.CreateAsync(nobody, LoseFat()), "No person profile for this account.");
  }

  // --- validation ---

  [Fact]
  public async Task Create_UnknownSlot_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Goals.CreateAsync(OwnerActor, LoseFat(slot: "third")), "slot must be one of: main, secondary.");
    (await RowsAsync(Owner.Id)).Should().BeEmpty();
    Audit.Events.Should().BeEmpty();
  }

  [Fact]
  public async Task Create_UnknownKind_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Goals.CreateAsync(OwnerActor, LoseFat() with { Kind = "get_shredded" }),
      "kind must be one of: lose_fat, gain_muscle, recomposition, maintain, keep_strength, fitness, sleep_better, improve_labs, custom.");
  }

  [Fact]
  public async Task Create_UnknownMetric_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Goals.CreateAsync(OwnerActor, LoseFat() with { MetricKey = "wingspan_cm" }), "Unknown metric 'wingspan_cm'.");
  }

  [Fact]
  public async Task Create_TitleOfEightyCharacters_IsAccepted()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat(title: new string('a', GoalService.MaxTitleLength)));

    goal.Title.Should().HaveLength(80);
  }

  [Fact]
  public async Task Create_TitleOverEightyCharacters_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Goals.CreateAsync(OwnerActor, LoseFat(title: new string('a', GoalService.MaxTitleLength + 1))),
      "A title is at most 80 characters.");
  }

  [Fact]
  public async Task Create_CustomGoalWithoutATitle_IsRefused()
  {
    var request = new SaveGoalRequest(GoalSlots.Main, GoalKinds.Custom, "   ", null, null, null, null, null, null);

    await ShouldBeRefusedAsync(() => Goals.CreateAsync(OwnerActor, request), "A goal of your own needs a title.");
  }

  [Fact]
  public async Task Create_CustomGoalWithATitle_IsAccepted()
  {
    var request = new SaveGoalRequest(GoalSlots.Main, GoalKinds.Custom, "Walk the coast path", null, null, null, null, null, null);

    var goal = await Goals.CreateAsync(OwnerActor, request);

    goal.Title.Should().Be("Walk the coast path");
    goal.MetricKey.Should().BeNull();
  }

  [Fact]
  public async Task Create_NumberThatIsNotANumber_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Goals.CreateAsync(OwnerActor, LoseFat() with { StartValue = double.NaN }), "startValue must be a number.");
    await ShouldBeRefusedAsync(() => Goals.CreateAsync(OwnerActor, LoseFat() with { TargetValue = double.PositiveInfinity }), "targetValue must be a number.");
    await ShouldBeRefusedAsync(() => Goals.CreateAsync(OwnerActor, LoseFat() with { PacePerWeek = double.NegativeInfinity }), "pacePerWeek must be a number.");
  }

  [Fact]
  public async Task Create_PacePointingAwayFromTheTarget_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Goals.CreateAsync(OwnerActor, LoseFat() with { PacePerWeek = 0.5 }), "pacePerWeek points away from the target.");
  }

  [Fact]
  public async Task Create_PaceOfZeroOrNoStartValue_IsNotJudged()
  {
    var flat = await Goals.CreateAsync(OwnerActor, LoseFat() with { PacePerWeek = 0 });
    var open = await Goals.CreateAsync(OwnerActor, LoseFat(GoalSlots.Secondary) with { StartValue = null, PacePerWeek = 0.5 });

    flat.PacePerWeek.Should().Be(0);
    open.PacePerWeek.Should().Be(0.5, "with no start value there is no direction to check against");
  }

  [Fact]
  public async Task Create_TargetDateOnTheStartDay_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Goals.CreateAsync(OwnerActor, LoseFat() with { TargetDate = Today }), "targetDate must be after the start.");
    await ShouldBeRefusedAsync(
      () => Goals.CreateAsync(OwnerActor, LoseFat() with { StartedOn = Today.AddDays(10), TargetDate = Today.AddDays(5) }),
      "targetDate must be after the start.");
  }

  // --- update ---

  [Fact]
  public async Task Update_OwnGoal_ChangesItAndKeepsTheStartDay()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat() with { StartedOn = Today.AddDays(-20) });
    Clock.Advance(TimeSpan.FromHours(2));

    var updated = await Goals.UpdateAsync(OwnerActor, goal.Id, LoseFat(title: "Slower and steadier") with { TargetValue = 82, PacePerWeek = -0.25 });

    updated.Should().NotBeNull();
    updated!.Id.Should().Be(goal.Id);
    updated.Title.Should().Be("Slower and steadier");
    updated.TargetValue.Should().Be(82);
    updated.PacePerWeek.Should().Be(-0.25);
    updated.StartedOn.Should().Be(Today.AddDays(-20), "no start day in the request keeps the one it had");
    var row = (await RowsAsync(Owner.Id)).Should().ContainSingle().Subject;
    row.UpdatedAt.Should().Be(Now.UtcDateTime.AddHours(2));
    row.CreatedAt.Should().Be(Now.UtcDateTime);
    Audit.Events.Select(e => e.Action).Should().Equal(AuditActions.GoalSaved, AuditActions.GoalSaved);
  }

  [Fact]
  public async Task Update_DifferentSlot_IsRefused()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat());

    await ShouldBeRefusedAsync(
      () => Goals.UpdateAsync(OwnerActor, goal.Id, LoseFat(GoalSlots.Secondary)),
      "A goal keeps its slot. End it and start a new one.");
    (await RowsAsync(Owner.Id)).Single().Slot.Should().Be(GoalSlots.Main);
  }

  [Fact]
  public async Task Update_InvalidRequest_IsRefusedAndChangesNothing()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat());

    await ShouldBeRefusedAsync(
      () => Goals.UpdateAsync(OwnerActor, goal.Id, LoseFat() with { TargetValue = 70, PacePerWeek = 1 }),
      "pacePerWeek points away from the target.");
    (await RowsAsync(Owner.Id)).Single().TargetValue.Should().Be(80);
  }

  [Fact]
  public async Task Update_SomeoneElsesGoal_IsNullAndChangesNothing()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat());

    var updated = await Goals.UpdateAsync(MemberActor, goal.Id, LoseFat() with { TargetValue = 60 });

    updated.Should().BeNull();
    (await RowsAsync(Owner.Id)).Single().TargetValue.Should().Be(80);
  }

  [Fact]
  public async Task Update_UnknownGoal_IsNull()
  {
    (await Goals.UpdateAsync(OwnerActor, Guid.NewGuid(), LoseFat())).Should().BeNull();
  }

  // --- end ---

  [Fact]
  public async Task End_Done_EndsItAndFreesTheSlot()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat());
    Clock.Advance(TimeSpan.FromDays(30));

    var ended = await Goals.EndAsync(OwnerActor, goal.Id, new EndGoalRequest(GoalStatuses.Done));

    ended!.Status.Should().Be(GoalStatuses.Done);
    ended.EndedAt.Should().Be(Now.UtcDateTime.AddDays(30));
    (await Goals.ListAsync(Owner.Id, Owner.Id, includeEnded: false)).Should().BeEmpty();
    (await Goals.ListAsync(Owner.Id, Owner.Id, includeEnded: true)).Should().ContainSingle();
    var written = Audit.Events[^1];
    written.Action.Should().Be(AuditActions.GoalEnded);
    RecordingAudit.DetailsJson(written).Should().Be("""{"slot":"main","kind":"lose_fat","status":"done"}""");
  }

  [Fact]
  public async Task End_Dropped_EndsItAsDropped()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat());

    var ended = await Goals.EndAsync(OwnerActor, goal.Id, new EndGoalRequest(GoalStatuses.Dropped));

    ended!.Status.Should().Be(GoalStatuses.Dropped);
  }

  [Fact]
  public async Task End_StatusThatIsNotAnEnding_IsRefused()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat());

    await ShouldBeRefusedAsync(() => Goals.EndAsync(OwnerActor, goal.Id, new EndGoalRequest(GoalStatuses.Active)), "status must be done or dropped.");
    await ShouldBeRefusedAsync(() => Goals.EndAsync(OwnerActor, goal.Id, new EndGoalRequest("paused")), "status must be done or dropped.");
    (await RowsAsync(Owner.Id)).Single().Status.Should().Be(GoalStatuses.Active);
  }

  [Fact]
  public async Task End_SomeoneElsesGoal_IsNullAndLeavesItActive()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat());

    var ended = await Goals.EndAsync(MemberActor, goal.Id, new EndGoalRequest(GoalStatuses.Dropped));

    ended.Should().BeNull();
    (await RowsAsync(Owner.Id)).Single().Status.Should().Be(GoalStatuses.Active);
  }

  [Fact]
  public async Task End_AlreadyEnded_KeepsTheFirstEndingAndWritesNoAudit()
  {
    var goal = await Goals.CreateAsync(OwnerActor, LoseFat());
    await Goals.EndAsync(OwnerActor, goal.Id, new EndGoalRequest(GoalStatuses.Done));
    Clock.Advance(TimeSpan.FromDays(3));

    var again = await Goals.EndAsync(OwnerActor, goal.Id, new EndGoalRequest(GoalStatuses.Dropped));

    again!.Status.Should().Be(GoalStatuses.Done);
    again.EndedAt.Should().Be(Now.UtcDateTime);
    Audit.Events.Select(e => e.Action).Should().Equal(AuditActions.GoalSaved, AuditActions.GoalEnded);
  }

  [Fact]
  public async Task Create_AfterTheSlotWasEnded_LeavesTheEndedGoalAsItWas()
  {
    var first = await Goals.CreateAsync(OwnerActor, LoseFat());
    await Goals.EndAsync(OwnerActor, first.Id, new EndGoalRequest(GoalStatuses.Done));

    await Goals.CreateAsync(OwnerActor, LoseFat());

    var all = await Goals.ListAsync(Owner.Id, Owner.Id, includeEnded: true);
    all!.Single(g => g.Id == first.Id).Status.Should().Be(GoalStatuses.Done, "only an active goal is dropped by a new one");
  }
}
