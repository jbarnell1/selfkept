using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Services.Assistant;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

public class CheckinMachineTests
{
  private static readonly DateTime Now = new(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);
  private static readonly JournalCapture Capture = new();

  [Fact]
  public void Start_AsksOnlyWhatIsMissing_InOrder()
  {
    var state = CheckinMachine.Start([CheckinTopics.Mood, CheckinTopics.Training]);

    state.Current.Should().Be(CheckinTopics.Training);
    state.Pending.Should().Equal(CheckinTopics.Mood);
  }

  [Fact]
  public void Start_NothingMissing_IsCompleteAtOnce()
  {
    var state = CheckinMachine.Start([]);

    state.Complete.Should().BeTrue();
    state.NothingMissing.Should().BeTrue();
    CheckinMachine.Status(state).Should().Be(CheckinStatuses.Done);
  }

  [Fact]
  public void Answer_EveryTopic_EndsComplete()
  {
    var state = CheckinMachine.Start([CheckinTopics.Eating, CheckinTopics.Drinks]);

    CheckinMachine.Answer(state, CheckinOutcome.Harvested);
    CheckinMachine.Answer(state, CheckinOutcome.None);

    state.Complete.Should().BeTrue();
    state.Done.Should().Equal(CheckinTopics.Eating, CheckinTopics.Drinks);
  }

  [Fact]
  public void Leave_KeepsWhatWasDone_AndIsPartial()
  {
    var state = CheckinMachine.Start([CheckinTopics.Eating, CheckinTopics.Drinks, CheckinTopics.Mood]);
    CheckinMachine.Answer(state, CheckinOutcome.Harvested);

    CheckinMachine.Leave(state);

    state.Done.Should().Equal(CheckinTopics.Eating);
    CheckinMachine.Status(state).Should().Be(CheckinStatuses.Partial);
  }

  [Fact]
  public void Resume_AfterLeaving_AsksOnlyWhatIsStillMissing()
  {
    var state = CheckinMachine.Start([CheckinTopics.Eating, CheckinTopics.Drinks, CheckinTopics.Mood]);
    CheckinMachine.Answer(state, CheckinOutcome.Harvested);
    CheckinMachine.Leave(state);

    // Mood was logged elsewhere in the meantime.
    CheckinMachine.Resume(state, [CheckinTopics.Drinks]);

    state.Left.Should().BeFalse();
    state.Current.Should().Be(CheckinTopics.Drinks);
    state.Pending.Should().BeEmpty();
  }

  [Fact]
  public void Answer_RetryOnce_ThenSkips()
  {
    var state = CheckinMachine.Start([CheckinTopics.Mood]);

    CheckinMachine.Answer(state, CheckinOutcome.Retry);
    state.Current.Should().Be(CheckinTopics.Mood);
    CheckinMachine.Answer(state, CheckinOutcome.Retry);

    state.Skipped.Should().Equal(CheckinTopics.Mood);
    state.Complete.Should().BeTrue();
  }

  [Fact]
  public void State_RoundTripsThroughJson()
  {
    var state = CheckinMachine.Start([CheckinTopics.Eating, CheckinTopics.Mood]);

    var back = CheckinState.FromJson(state.ToJson());

    back.Current.Should().Be(CheckinTopics.Eating);
    back.Pending.Should().Equal(CheckinTopics.Mood);
  }

  [Fact]
  public void Interpret_NoDrinks_RegistersBothMarkers()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Drinks, "None", Now, TimeZoneInfo.Utc);

    result.Outcome.Should().Be(CheckinOutcome.None);
    result.Drafts.Single().Tags.Should().BeEquivalentTo([AssistantTags.NoAlcohol, AssistantTags.NoCaffeine]);
  }

  [Fact]
  public void Interpret_CoffeeButNoAlcohol_SavesCaffeineAndTheAlcoholMarker()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Drinks, "Two coffees, no alcohol", Now, TimeZoneInfo.Utc);

    result.Drafts.SelectMany(d => d.Tags).Should().Contain([JournalTags.Caffeine, AssistantTags.NoAlcohol]).And.NotContain(AssistantTags.NoCaffeine);
  }

  [Fact]
  public void Interpret_NoTraining_RegistersRestDay()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Training, "No, not today", Now, TimeZoneInfo.Utc);

    result.Outcome.Should().Be(CheckinOutcome.None);
    result.Drafts.Single().Tags.Should().Equal(JournalTags.RestDay);
  }

  [Fact]
  public void Interpret_HardSession_PicksTheHardTone()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Training, "Yes, a really tough session, squats and deadlifts", Now, TimeZoneInfo.Utc);

    result.Tone.Should().Be(CheckinTones.Hard);
    result.Drafts.Single(d => d.Kind == JournalKinds.Training).Tags.Should().Contain(JournalTags.Lifted);
  }

  [Fact]
  public void Interpret_FeelingFine_RegistersNoSymptoms()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Symptoms, "All good, feeling fine", Now, TimeZoneInfo.Utc);

    result.Drafts.Single().Tags.Should().Equal(AssistantTags.NoSymptoms);
  }

  [Fact]
  public void Interpret_UntaggedFood_KeepsTheWords()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Eating, "Mostly leftovers", Now, TimeZoneInfo.Utc);

    result.Drafts.Single().Text.Should().Be("Mostly leftovers");
  }

  [Theory]
  [InlineData("great", 6)]
  [InlineData("pretty good thanks", 5)]
  [InlineData("okay", 4)]
  [InlineData("not bad", 4)]
  [InlineData("meh", 3)]
  [InlineData("a bit low", 2)]
  [InlineData("very low", 1)]
  [InlineData("4", 4)]
  public void Mood_Words_MapToTheCheckinScale(string answer, int mood)
  {
    CheckinHarvest.Mood(answer).Should().Be(mood);
  }

  [Fact]
  public void Interpret_MoodWithoutAMoodWord_AsksAgain()
  {
    CheckinHarvest.Interpret(Capture, CheckinTopics.Mood, "hmm", Now, TimeZoneInfo.Utc).Outcome.Should().Be(CheckinOutcome.Retry);
  }

  [Fact]
  public void Interpret_Skip_SavesNothing()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Eating, "skip", Now, TimeZoneInfo.Utc);

    result.Outcome.Should().Be(CheckinOutcome.Skipped);
    result.Drafts.Should().BeEmpty();
  }
}
