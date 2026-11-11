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

  [Theory]
  [InlineData("none today")]
  [InlineData("None today.")]
  [InlineData("no, nothing today")]
  [InlineData("nope, not that I noticed")]
  [InlineData("nothing to report")]
  [InlineData("no issues")]
  [InlineData("not bad at all")]
  public void Interpret_NoSymptomsInOtherWords_SavesTheMarkerAndNotTheWords(string answer)
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Symptoms, answer, Now, TimeZoneInfo.Utc);

    result.Outcome.Should().Be(CheckinOutcome.None);
    result.Tone.Should().Be(CheckinTones.None);
    var draft = result.Drafts.Single();
    draft.Tags.Should().Equal(AssistantTags.NoSymptoms);
    draft.Text.Should().BeNull();
  }

  [Theory]
  [InlineData("not great")]
  [InlineData("not feeling well")]
  [InlineData("I don't feel right")]
  [InlineData("a bit under the weather")]
  public void Interpret_NotFine_IsNeverReadAsFine(string answer)
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Symptoms, answer, Now, TimeZoneInfo.Utc);

    result.Outcome.Should().Be(CheckinOutcome.Harvested);
    result.Tone.Should().Be(CheckinTones.Unwell);
    result.Drafts.Should().NotContain(d => d.Tags.Contains(AssistantTags.NoSymptoms));
  }

  [Fact]
  public void Interpret_ANamedSymptom_PicksTheUnwellTone()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Symptoms, "a bit of a headache this afternoon", Now, TimeZoneInfo.Utc);

    result.Tone.Should().Be(CheckinTones.Unwell);
    result.Drafts.Single().Tags.Should().Contain(JournalTags.Headache);
  }

  [Fact]
  public void Interpret_SymptomWordsTheLexiconDoesNotKnow_GetNoSympathyLine()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Symptoms, "same as yesterday", Now, TimeZoneInfo.Utc);

    result.Tone.Should().Be(CheckinTones.Noted);
    result.Drafts.Single().Text.Should().Be("same as yesterday");
    AssistantTemplates.EncouragementFor(CheckinTopics.Symptoms, result.Tone).Should().NotContain("Sorry");
  }

  [Theory]
  [InlineData("Pizza and ice cream")]
  [InlineData("Takeaway burger and fries")]
  [InlineData("Salmon and vegetables, then a brownie")]
  public void Interpret_SweetsOrTakeaway_NeverGetALineAboutJudgement(string answer)
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Eating, answer, Now, TimeZoneInfo.Utc);

    result.Outcome.Should().Be(CheckinOutcome.Harvested);
    var line = AssistantTemplates.EncouragementFor(CheckinTopics.Eating, result.Tone);
    line.Should().NotContainAny("judgement", "just one day", "honest");
  }

  [Fact]
  public void Encouragement_NoLine_GradesOrExcusesTheAnswer()
  {
    foreach (var line in AssistantTemplates.Encouragement.Values)
    {
      line.Should().NotContainAny("judgement", "honestly", "just one day", "guilt");
    }
  }

  [Theory]
  [InlineData("none today")]
  [InlineData("no, neither")]
  public void Interpret_NoDrinksInOtherWords_SavesBothMarkers(string answer)
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Drinks, answer, Now, TimeZoneInfo.Utc);

    result.Outcome.Should().Be(CheckinOutcome.None);
    result.Drafts.Single().Tags.Should().BeEquivalentTo([AssistantTags.NoAlcohol, AssistantTags.NoCaffeine]);
  }

  [Fact]
  public void Interpret_UntaggedFood_KeepsTheWords()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Eating, "Mostly leftovers", Now, TimeZoneInfo.Utc);

    result.Drafts.Single().Text.Should().Be("Mostly leftovers");
  }

  [Fact]
  public void Interpret_ARunGivenForEating_IsNotFiledAsFood()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Eating, "Went for a 40 minute run after work, legs felt good", Now, TimeZoneInfo.Utc);

    result.Outcome.Should().Be(CheckinOutcome.Retry);
    result.Drafts.Should().BeEmpty();
  }

  [Fact]
  public void Interpret_FoodAndARunGivenForEating_KeepsBoth()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Eating, "Chicken salad at lunch, then went for a run", Now, TimeZoneInfo.Utc);

    result.Outcome.Should().Be(CheckinOutcome.Harvested);
    result.Drafts.Select(d => d.Kind).Should().Contain([JournalKinds.Food, JournalKinds.Training]);
  }

  [Fact]
  public void Interpret_NoTrainingWithAMeal_StillRegistersRestDay()
  {
    var result = CheckinHarvest.Interpret(Capture, CheckinTopics.Training, "No training today, just a big pasta dinner", Now, TimeZoneInfo.Utc);

    result.Outcome.Should().Be(CheckinOutcome.None);
    result.Drafts.Should().Contain(d => d.Kind == JournalKinds.Training && d.Tags.Contains(JournalTags.RestDay));
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
