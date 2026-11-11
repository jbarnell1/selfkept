using FluentAssertions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using Xunit;

namespace SelfKept.UnitTests.Body;

/// <summary>
/// The access rule on every body read, through the real repositories, the real share_grant
/// repository and the real policy: the owner sees their rows, a household member with the grant
/// for that category sees them, a member with every grant but that one gets null, and so does a
/// stranger. Goals and targets read under body, the day log under check-ins, medicines under labs,
/// the strength log under activity. Then the two services that read across categories: progress
/// and the target tally give back only what the viewer was granted.
/// </summary>
public class BodyAccessTests : BodyTestBase
{
  private static readonly DateOnly From = Today.AddDays(-30);

  private static readonly bool?[] NothingKnown = [null, null, null, null, null, null, null];

  public override async Task InitializeAsync()
  {
    await base.InitializeAsync();
    var at = Now.UtcDateTime;

    await GoalRepo.AddAsync(new Goal
    {
      Id = Guid.CreateVersion7(), PersonId = Owner.Id, Slot = GoalSlots.Main, Kind = GoalKinds.LoseFat, MetricKey = MetricKeys.WeightKg,
      StartValue = 90, TargetValue = 80, PacePerWeek = -0.5, StartedOn = Today.AddDays(-30), CreatedAt = at, UpdatedAt = at,
    });
    await GoalRepo.AddAsync(new Goal
    {
      Id = Guid.CreateVersion7(), PersonId = Owner.Id, Slot = GoalSlots.Secondary, Kind = GoalKinds.KeepStrength, Status = GoalStatuses.Dropped,
      StartedOn = Today.AddDays(-60), CreatedAt = at, UpdatedAt = at, EndedAt = at,
    });
    await GoalRepo.AddAsync(new Goal
    {
      Id = Guid.CreateVersion7(), PersonId = Member.Id, Slot = GoalSlots.Main, Kind = GoalKinds.Maintain, StartedOn = Today, CreatedAt = at, UpdatedAt = at,
    });

    await TargetRepo.AddAsync(Target(Owner.Id, 0, "Protein", TargetKinds.Metric, TargetComparators.AtLeast, 120, TargetPeriods.Day, metricKey: MetricKeys.ProteinG));
    await TargetRepo.AddAsync(Target(Owner.Id, 1, "Steps", TargetKinds.Metric, TargetComparators.AtLeast, 8000, TargetPeriods.Day, metricKey: MetricKeys.Steps));
    await TargetRepo.AddAsync(Target(Owner.Id, 2, "No alcohol", TargetKinds.Journal, TargetComparators.AtMost, 0, TargetPeriods.Day, tag: JournalTags.Alcohol));
    await TargetRepo.AddAsync(Target(Owner.Id, 3, "Lifting days", TargetKinds.Strength, TargetComparators.AtLeast, 2, TargetPeriods.Week));
    await TargetRepo.AddAsync(Target(Member.Id, 0, "Sleep", TargetKinds.Metric, TargetComparators.AtLeast, 420, TargetPeriods.Day, metricKey: MetricKeys.SleepStage));

    await JournalRepo.AddAsync(Entry(Owner.Id, Today.AddDays(-2), JournalKinds.Food, JournalTags.HighProtein));
    await JournalRepo.AddAsync(Entry(Owner.Id, Today.AddDays(-1), JournalKinds.Drink, JournalTags.Alcohol));
    await JournalRepo.AddAsync(Entry(Member.Id, Today, JournalKinds.Note));

    await MedicationRepo.AddAsync(new Medication
    {
      Id = Guid.CreateVersion7(), PersonId = Owner.Id, Kind = MedicationKinds.Supplement, DetailCipher = Encryption.Encrypt("{}"),
      Substances = [SubstanceTags.Creatine], CreatedAt = at, UpdatedAt = at,
    });
    await MedicationRepo.AddAsync(new Medication
    {
      Id = Guid.CreateVersion7(), PersonId = Member.Id, Kind = MedicationKinds.Medication, DetailCipher = Encryption.Encrypt("{}"), CreatedAt = at, UpdatedAt = at,
    });

    await StrengthRepo.AddAsync(new StrengthSet { Id = Guid.CreateVersion7(), PersonId = Owner.Id, Day = Today.AddDays(-3), Lift = Lifts.Deadlift, WeightKg = 140, Reps = 5, CreatedAt = at });
    await StrengthRepo.AddAsync(new StrengthSet { Id = Guid.CreateVersion7(), PersonId = Owner.Id, Day = Today.AddDays(-1), Lift = Lifts.Squat, WeightKg = 100, Reps = 5, CreatedAt = at });
    await StrengthRepo.AddAsync(new StrengthSet { Id = Guid.CreateVersion7(), PersonId = Member.Id, Day = Today, Lift = Lifts.Row, WeightKg = 60, Reps = 8, CreatedAt = at });

    await SeedSummariesAsync(Owner.Id, MetricKeys.WeightKg,
      (Today.AddDays(-6), 84.6), (Today.AddDays(-5), 84.4), (Today.AddDays(-4), 84.5), (Today.AddDays(-3), 84.2),
      (Today.AddDays(-2), 84.3), (Today.AddDays(-1), 84.0), (Today, 84.1));
    await SeedSummariesAsync(Owner.Id, MetricKeys.ProteinG, (Today.AddDays(-2), 100), (Today.AddDays(-1), 130), (Today, 40));
    await SeedSummariesAsync(Owner.Id, MetricKeys.Steps, (Today.AddDays(-1), 9000));
  }

  private static DailyTarget Target(
    Guid personId, short sortOrder, string label, string kind, string comparator, double value, string period, string? metricKey = null, string? tag = null) => new()
    {
      Id = Guid.CreateVersion7(), PersonId = personId, Label = label, Kind = kind, MetricKey = metricKey, Tag = tag, Comparator = comparator,
      Value = value, Period = period, SortOrder = sortOrder, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
    };

  private static JournalEntry Entry(Guid personId, DateOnly day, string kind, params string[] tags) => new()
  {
    Id = Guid.CreateVersion7(), PersonId = personId, At = day.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc), Day = day, Kind = kind, Tags = tags,
    Source = JournalSources.Chip, CreatedAt = Now.UtcDateTime,
  };

  private static TargetTallyDto Tally(IReadOnlyList<TargetTallyDto> tallies, string label) => tallies.Single(t => t.Target.Label == label);

  // --- goals: body ---

  [Fact]
  public async Task ListGoals_Owner_ReturnsTheirOwnRowsOnly()
  {
    var active = await GoalRepo.ListAsync(Owner.Id, Owner.Id, includeEnded: false);
    var all = await GoalRepo.ListAsync(Owner.Id, Owner.Id, includeEnded: true);

    active.Should().ContainSingle().Which.Kind.Should().Be(GoalKinds.LoseFat);
    all.Should().HaveCount(2).And.OnlyContain(g => g.PersonId == Owner.Id);
    all![0].Status.Should().Be(GoalStatuses.Active, "active goals come first");
  }

  [Fact]
  public async Task ListGoals_MemberWithTheBodyGrant_ReturnsRows()
  {
    await ShareAsync(ShareCategory.Body);

    var goals = await GoalRepo.ListAsync(Member.Id, Owner.Id, includeEnded: true);

    goals.Should().HaveCount(2).And.OnlyContain(g => g.PersonId == Owner.Id);
  }

  [Fact]
  public async Task ListGoals_MemberWithEveryGrantButBody_IsNull()
  {
    await ShareEverythingButAsync(ShareCategory.Body);

    (await GoalRepo.ListAsync(Member.Id, Owner.Id, includeEnded: true)).Should().BeNull();
  }

  [Fact]
  public async Task ListGoals_MemberWithNoGrant_IsNull()
  {
    (await GoalRepo.ListAsync(Member.Id, Owner.Id, includeEnded: true)).Should().BeNull();
  }

  [Fact]
  public async Task ListGoals_Stranger_IsNull()
  {
    await ShareAsync(ShareCategory.Body);

    (await GoalRepo.ListAsync(Stranger, Owner.Id, includeEnded: true)).Should().BeNull();
  }

  // --- targets: body ---

  [Fact]
  public async Task ListTargets_Owner_ReturnsTheirOwnRowsInOrder()
  {
    var targets = await TargetRepo.ListAsync(Owner.Id, Owner.Id);

    targets!.Select(t => t.Label).Should().Equal("Protein", "Steps", "No alcohol", "Lifting days");
  }

  [Fact]
  public async Task ListTargets_MemberWithTheBodyGrant_ReturnsRows()
  {
    await ShareAsync(ShareCategory.Body);

    (await TargetRepo.ListAsync(Member.Id, Owner.Id)).Should().HaveCount(4).And.OnlyContain(t => t.PersonId == Owner.Id);
  }

  [Fact]
  public async Task ListTargets_MemberWithEveryGrantButBody_IsNull()
  {
    await ShareEverythingButAsync(ShareCategory.Body);

    (await TargetRepo.ListAsync(Member.Id, Owner.Id)).Should().BeNull();
  }

  [Fact]
  public async Task ListTargets_MemberWithNoGrant_IsNull()
  {
    (await TargetRepo.ListAsync(Member.Id, Owner.Id)).Should().BeNull();
  }

  [Fact]
  public async Task ListTargets_Stranger_IsNull()
  {
    await ShareAsync(ShareCategory.Body);

    (await TargetRepo.ListAsync(Stranger, Owner.Id)).Should().BeNull();
  }

  // --- day log: check-ins ---

  [Fact]
  public async Task ListJournal_Owner_ReturnsTheirOwnRowsNewestFirst()
  {
    var entries = await JournalRepo.ListAsync(Owner.Id, Owner.Id, From, Today, 100);

    entries!.Select(e => e.Kind).Should().Equal(JournalKinds.Drink, JournalKinds.Food);
    entries.Should().OnlyContain(e => e.PersonId == Owner.Id);
  }

  [Fact]
  public async Task ListJournal_MemberWithTheCheckinsGrant_ReturnsRows()
  {
    await ShareAsync(ShareCategory.Checkins);

    (await JournalRepo.ListAsync(Member.Id, Owner.Id, From, Today, 100)).Should().HaveCount(2).And.OnlyContain(e => e.PersonId == Owner.Id);
  }

  [Fact]
  public async Task ListJournal_MemberWithEveryGrantButCheckins_IsNull()
  {
    await ShareEverythingButAsync(ShareCategory.Checkins);

    (await JournalRepo.ListAsync(Member.Id, Owner.Id, From, Today, 100)).Should().BeNull();
  }

  [Fact]
  public async Task ListJournal_MemberWithNoGrant_IsNull()
  {
    (await JournalRepo.ListAsync(Member.Id, Owner.Id, From, Today, 100)).Should().BeNull();
  }

  [Fact]
  public async Task ListJournal_Stranger_IsNull()
  {
    await ShareAsync(ShareCategory.Checkins);

    (await JournalRepo.ListAsync(Stranger, Owner.Id, From, Today, 100)).Should().BeNull();
  }

  // --- medicines: labs ---

  [Fact]
  public async Task ListMedications_Owner_ReturnsTheirOwnRowsOnly()
  {
    var medications = await MedicationRepo.ListAsync(Owner.Id, Owner.Id);

    medications.Should().ContainSingle().Which.Substances.Should().Equal(SubstanceTags.Creatine);
  }

  [Fact]
  public async Task ListMedications_MemberWithTheLabsGrant_ReturnsRows()
  {
    await ShareAsync(ShareCategory.Labs);

    (await MedicationRepo.ListAsync(Member.Id, Owner.Id)).Should().ContainSingle().Which.PersonId.Should().Be(Owner.Id);
  }

  [Fact]
  public async Task ListMedications_MemberWithEveryGrantButLabs_IsNull()
  {
    await ShareEverythingButAsync(ShareCategory.Labs);

    (await MedicationRepo.ListAsync(Member.Id, Owner.Id)).Should().BeNull();
  }

  [Fact]
  public async Task ListMedications_MemberWithNoGrant_IsNull()
  {
    (await MedicationRepo.ListAsync(Member.Id, Owner.Id)).Should().BeNull();
  }

  [Fact]
  public async Task ListMedications_Stranger_IsNull()
  {
    await ShareAsync(ShareCategory.Labs);

    (await MedicationRepo.ListAsync(Stranger, Owner.Id)).Should().BeNull();
  }

  // --- strength log: activity ---

  [Fact]
  public async Task ListStrength_Owner_ReturnsTheirOwnRowsOldestFirst()
  {
    var sets = await StrengthRepo.ListAsync(Owner.Id, Owner.Id, From, Today);

    sets!.Select(s => s.Lift).Should().Equal(Lifts.Deadlift, Lifts.Squat);
  }

  [Fact]
  public async Task ListStrength_MemberWithTheActivityGrant_ReturnsRows()
  {
    await ShareAsync(ShareCategory.Activity);

    (await StrengthRepo.ListAsync(Member.Id, Owner.Id, From, Today)).Should().HaveCount(2).And.OnlyContain(s => s.PersonId == Owner.Id);
  }

  [Fact]
  public async Task ListStrength_MemberWithEveryGrantButActivity_IsNull()
  {
    await ShareEverythingButAsync(ShareCategory.Activity);

    (await StrengthRepo.ListAsync(Member.Id, Owner.Id, From, Today)).Should().BeNull();
  }

  [Fact]
  public async Task ListStrength_MemberWithNoGrant_IsNull()
  {
    (await StrengthRepo.ListAsync(Member.Id, Owner.Id, From, Today)).Should().BeNull();
  }

  [Fact]
  public async Task ListStrength_Stranger_IsNull()
  {
    await ShareAsync(ShareCategory.Activity);

    (await StrengthRepo.ListAsync(Stranger, Owner.Id, From, Today)).Should().BeNull();
  }

  // --- a grant goes one way ---

  [Fact]
  public async Task EveryList_OwnerLooksAtTheMemberTheyShareWith_IsNull()
  {
    await ShareAsync(Enum.GetValues<ShareCategory>());

    (await GoalRepo.ListAsync(Owner.Id, Member.Id, includeEnded: true)).Should().BeNull("sharing with someone is not being shared with");
    (await TargetRepo.ListAsync(Owner.Id, Member.Id)).Should().BeNull();
    (await JournalRepo.ListAsync(Owner.Id, Member.Id, From, Today, 100)).Should().BeNull();
    (await MedicationRepo.ListAsync(Owner.Id, Member.Id)).Should().BeNull();
    (await StrengthRepo.ListAsync(Owner.Id, Member.Id, From, Today)).Should().BeNull();
  }

  // --- the services pass the denial on ---

  [Fact]
  public async Task ServiceLists_MemberWithNoGrant_AreAllNull()
  {
    (await Goals.ListAsync(Member.Id, Owner.Id, includeEnded: true)).Should().BeNull();
    (await Targets.ListAsync(Member.Id, Owner.Id)).Should().BeNull();
    (await Journal.ListAsync(Member.Id, Owner.Id, From, Today)).Should().BeNull();
    (await Medications.ListAsync(Member.Id, Owner.Id)).Should().BeNull();
    (await Strength.ListAsync(Member.Id, Owner.Id, From, Today)).Should().BeNull();
  }

  [Fact]
  public async Task ServiceLists_MemberWithEveryGrant_ReturnTheOwnersRows()
  {
    await ShareAsync(Enum.GetValues<ShareCategory>());

    (await Goals.ListAsync(Member.Id, Owner.Id, includeEnded: true)).Should().HaveCount(2);
    (await Targets.ListAsync(Member.Id, Owner.Id)).Should().HaveCount(4);
    (await Journal.ListAsync(Member.Id, Owner.Id, From, Today)).Should().HaveCount(2);
    (await Medications.ListAsync(Member.Id, Owner.Id)).Should().ContainSingle();
    (await Strength.ListAsync(Member.Id, Owner.Id, From, Today)).Should().HaveCount(2, "one group per lift");
  }

  // --- progress ---

  [Fact]
  public async Task GetProgress_Owner_ReturnsEverythingWithSuggestions()
  {
    var progress = await Progress.GetAsync(Owner.Id, Owner.Id);

    progress.Should().NotBeNull();
    progress!.Day.Should().Be(Today);
    progress.Goals.Should().ContainSingle().Which.Goal.Kind.Should().Be(GoalKinds.LoseFat);
    progress.Goals[0].Current.Should().NotBeNull();
    progress.Body.WeightKg.Should().Be(84.1);
    progress.Body.HeightCm.Should().Be(180);
    progress.Targets.Should().HaveCount(4);
    progress.Series.Select(s => s.Key).Should().Equal(MetricKeys.WeightKg, MetricKeys.ProteinG);
    progress.Strength.Select(l => l.Lift).Should().Equal(Lifts.Squat, Lifts.Deadlift);
    progress.Suggestions.Should().HaveCount(5);
  }

  [Fact]
  public async Task GetProgress_MemberWithEveryGrantButBody_IsNull()
  {
    await ShareEverythingButAsync(ShareCategory.Body);

    (await Progress.GetAsync(Member.Id, Owner.Id)).Should().BeNull();
  }

  [Fact]
  public async Task GetProgress_MemberWithNoGrant_IsNull()
  {
    (await Progress.GetAsync(Member.Id, Owner.Id)).Should().BeNull();
  }

  [Fact]
  public async Task GetProgress_Stranger_IsNull()
  {
    await ShareAsync(ShareCategory.Body);

    (await Progress.GetAsync(Stranger, Owner.Id)).Should().BeNull();
  }

  [Fact]
  public async Task GetProgress_BodyGrantWithoutActivity_HasTheBodyButNoStrengthLog()
  {
    await ShareAsync(ShareCategory.Body);

    var progress = await Progress.GetAsync(Member.Id, Owner.Id);

    progress.Should().NotBeNull();
    progress!.Body.WeightKg.Should().Be(84.1);
    progress.Goals.Should().ContainSingle().Which.Current.Should().NotBeNull();
    progress.Strength.Should().BeEmpty("the strength log reads under activity");
  }

  [Fact]
  public async Task GetProgress_BodyAndActivityGrants_HasTheStrengthLog()
  {
    await ShareAsync(ShareCategory.Body, ShareCategory.Activity);

    var progress = await Progress.GetAsync(Member.Id, Owner.Id);

    progress!.Strength.Select(l => l.Lift).Should().Equal(Lifts.Squat, Lifts.Deadlift);
  }

  [Fact]
  public async Task GetProgress_GrantedMember_GetsNoSuggestions()
  {
    await ShareAsync(Enum.GetValues<ShareCategory>());

    var progress = await Progress.GetAsync(Member.Id, Owner.Id);

    progress!.Suggestions.Should().BeEmpty("suggestions are for the person themself");
  }

  [Fact]
  public async Task GetProgress_BodyGrantOnly_TargetsBuiltOnOtherCategoriesAreUnknown()
  {
    await ShareAsync(ShareCategory.Body);

    var progress = await Progress.GetAsync(Member.Id, Owner.Id);

    Tally(progress!.Targets, "Protein").KnownDays.Should().Be(2);
    foreach (var label in new[] { "Steps", "No alcohol", "Lifting days" })
    {
      var tally = Tally(progress.Targets, label);
      tally.Current.Should().BeNull(label);
      tally.Met.Should().BeNull(label);
      tally.Week.Should().Equal(NothingKnown);
    }
  }

  [Fact]
  public async Task GetProgress_SleepGoalForABodyOnlyViewer_SaysItIsNotShared()
  {
    await GoalRepo.AddAsync(new Goal
    {
      Id = Guid.CreateVersion7(), PersonId = Owner.Id, Slot = GoalSlots.Secondary, Kind = GoalKinds.SleepBetter, TargetValue = 450,
      StartValue = 400, StartedOn = Today.AddDays(-10), CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
    });
    await SeedSummariesAsync(Owner.Id, MetricKeys.SleepStage, (Today.AddDays(-2), 410), (Today.AddDays(-1), 430), (Today, 420));
    await ShareAsync(ShareCategory.Body);

    var progress = await Progress.GetAsync(Member.Id, Owner.Id);

    var sleep = progress!.Goals.Single(g => g.Goal.Kind == GoalKinds.SleepBetter);
    sleep.Current.Should().BeNull();
    sleep.Fraction.Should().BeNull();
    sleep.ActualPacePerWeek.Should().BeNull();
    sleep.ProjectedDate.Should().BeNull();
    sleep.PaceStatus.Should().Be(PaceStatuses.Unknown);
    sleep.Summary.Should().Be("Not shared with you.");
  }

  [Fact]
  public async Task GetProgress_SleepGoalOnceSleepIsShared_HasItsNumber()
  {
    await GoalRepo.AddAsync(new Goal
    {
      Id = Guid.CreateVersion7(), PersonId = Owner.Id, Slot = GoalSlots.Secondary, Kind = GoalKinds.SleepBetter, TargetValue = 450,
      StartValue = 400, StartedOn = Today.AddDays(-10), CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
    });
    await SeedSummariesAsync(Owner.Id, MetricKeys.SleepStage, (Today.AddDays(-2), 410), (Today.AddDays(-1), 430), (Today, 420));
    await ShareAsync(ShareCategory.Body, ShareCategory.Sleep);

    var progress = await Progress.GetAsync(Member.Id, Owner.Id);

    var sleep = progress!.Goals.Single(g => g.Goal.Kind == GoalKinds.SleepBetter);
    sleep.Current.Should().Be(420, "the mean of the last seven days");
    sleep.Unit.Should().Be("min");
    sleep.Summary.Should().StartWith("7 h now, aiming for 7 h 30 min.");
  }

  // --- the target tally ---

  [Fact]
  public async Task Tally_Owner_TalliesEveryTarget()
  {
    var tally = await Targets.TallyAsync(Owner.Id, Owner.Id);

    tally.Should().NotBeNull();
    tally!.Day.Should().Be(Today);
    tally.Goals.Should().ContainSingle().Which.Kind.Should().Be(GoalKinds.LoseFat);
    tally.Targets.Select(t => t.Target.Label).Should().Equal("Protein", "Steps", "No alcohol", "Lifting days");

    var protein = Tally(tally.Targets, "Protein");
    protein.Current.Should().Be(40);
    protein.Met.Should().BeNull("today's running total can still reach the target");
    protein.Week.Should().Equal(null, null, null, null, false, true, null);

    Tally(tally.Targets, "Steps").Week.Should().Equal(null, null, null, null, null, true, null);

    var alcohol = Tally(tally.Targets, "No alcohol");
    alcohol.Week.Should().Equal(null, null, null, null, true, false, null);
    alcohol.Met.Should().BeNull("nothing is in the day log today");

    var lifting = Tally(tally.Targets, "Lifting days");
    lifting.Current.Should().Be(2);
    lifting.Met.Should().BeTrue();
    lifting.Week.Should().Equal(false, false, false, true, false, true, false);
  }

  [Fact]
  public async Task Tally_MemberWithEveryGrantButBody_IsNull()
  {
    await ShareEverythingButAsync(ShareCategory.Body);

    (await Targets.TallyAsync(Member.Id, Owner.Id)).Should().BeNull();
  }

  [Fact]
  public async Task Tally_MemberWithNoGrant_IsNull()
  {
    (await Targets.TallyAsync(Member.Id, Owner.Id)).Should().BeNull();
  }

  [Fact]
  public async Task Tally_Stranger_IsNull()
  {
    await ShareAsync(ShareCategory.Body);

    (await Targets.TallyAsync(Stranger, Owner.Id)).Should().BeNull();
  }

  [Fact]
  public async Task Tally_BodyGrantOnly_TalliesBodyMetricsAndLeavesTheRestUnknown()
  {
    await ShareAsync(ShareCategory.Body);

    var tally = await Targets.TallyAsync(Member.Id, Owner.Id);

    tally!.Targets.Should().HaveCount(4, "the targets themselves read under body");
    Tally(tally.Targets, "Protein").Week.Should().Equal(null, null, null, null, false, true, null);
    foreach (var label in new[] { "Steps", "No alcohol", "Lifting days" })
    {
      var unknown = Tally(tally.Targets, label);
      unknown.Current.Should().BeNull(label);
      unknown.Met.Should().BeNull(label);
      unknown.Week.Should().Equal(NothingKnown);
      unknown.MetDays.Should().Be(0);
      unknown.KnownDays.Should().Be(0);
    }
  }

  [Fact]
  public async Task Tally_BodyAndCheckinsGrants_TalliesTheDayLogTarget()
  {
    await ShareAsync(ShareCategory.Body, ShareCategory.Checkins);

    var tally = await Targets.TallyAsync(Member.Id, Owner.Id);

    Tally(tally!.Targets, "No alcohol").Week.Should().Equal(null, null, null, null, true, false, null);
    Tally(tally.Targets, "Lifting days").Week.Should().Equal(NothingKnown);
    Tally(tally.Targets, "Steps").Week.Should().Equal(NothingKnown);
  }

  [Fact]
  public async Task Tally_BodyAndActivityGrants_TalliesTheStrengthAndStepsTargets()
  {
    await ShareAsync(ShareCategory.Body, ShareCategory.Activity);

    var tally = await Targets.TallyAsync(Member.Id, Owner.Id);

    var lifting = Tally(tally!.Targets, "Lifting days");
    lifting.Current.Should().Be(2);
    lifting.Met.Should().BeTrue();
    Tally(tally.Targets, "Steps").Week.Should().Equal(null, null, null, null, null, true, null);
    Tally(tally.Targets, "No alcohol").Week.Should().Equal(NothingKnown);
  }

  [Fact]
  public async Task Tally_MemberLooksAtTheirOwn_SeesNothingOfTheOwners()
  {
    await SeedSummariesAsync(Member.Id, MetricKeys.SleepStage, (Today, 430));

    var tally = await Targets.TallyAsync(Member.Id, Member.Id);

    tally!.Goals.Should().ContainSingle().Which.Kind.Should().Be(GoalKinds.Maintain);
    tally.Targets.Should().ContainSingle().Which.Target.Label.Should().Be("Sleep");
    tally.Targets[0].Met.Should().BeTrue("last night's sleep is final when it arrives");
  }
}
