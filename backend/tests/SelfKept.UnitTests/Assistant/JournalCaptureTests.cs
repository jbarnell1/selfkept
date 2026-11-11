using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Services.Assistant;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

public class JournalCaptureTests
{
  // 18:00 UTC on Tue 10 Nov 2026, which is 18:00 in London.
  private static readonly DateTime Now = new(2026, 11, 10, 18, 0, 0, DateTimeKind.Utc);
  private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
  private static readonly DateOnly Today = new(2026, 11, 10);

  private readonly JournalCapture _capture = new();

  private static DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, London));

  [Fact]
  public void Extract_StatementsAboutFoodAndSymptoms_SavesBothWithTags()
  {
    var result = _capture.Extract("Had oats and berries instead of the bagel. Throat's a bit scratchy and I'm tired.", Now, London);

    result.Drafts.Should().HaveCount(2);
    var food = result.Drafts.Single(d => d.Kind == JournalKinds.Food);
    food.Tags.Should().Contain([JournalTags.WholeGrains, JournalTags.Fruit, JournalTags.Swap]);
    food.Text.Should().Be("Had oats and berries instead of the bagel.");
    var symptom = result.Drafts.Single(d => d.Kind == JournalKinds.Symptom);
    symptom.Tags.Should().BeEquivalentTo([JournalTags.SoreThroat, JournalTags.Fatigue]);
  }

  [Theory]
  [InlineData("Should I have a coffee?")]
  [InlineData("How was my sleep last night")]
  [InlineData("Is beer bad for my heart rate?")]
  [InlineData("Did I lift enough this week")]
  public void Extract_Question_CapturesNothing(string message)
  {
    _capture.Extract(message, Now, London).Drafts.Should().BeEmpty();
  }

  [Theory]
  [InlineData("If I have a beer tonight will I sleep worse")]
  [InlineData("I'm going to have pizza later")]
  [InlineData("Thinking about a run tomorrow")]
  [InlineData("I need to drink more water")]
  public void Extract_PlanOrHypothetical_CapturesNothing(string message)
  {
    _capture.Extract(message, Now, London).Drafts.Should().BeEmpty();
  }

  [Fact]
  public void Extract_NegatedItem_IsNotLogged()
  {
    _capture.Extract("I didn't have any alcohol today.", Now, London).Drafts.Should().BeEmpty();
  }

  [Fact]
  public void Extract_SomeoneElse_IsNotLogged()
  {
    _capture.Extract("My wife has a headache.", Now, London).Drafts.Should().BeEmpty();
  }

  [Fact]
  public void Extract_Yesterday_MovesTheEntryToYesterday()
  {
    var draft = _capture.Extract("Yesterday I had three beers.", Now, London).Drafts.Single();

    draft.Kind.Should().Be(JournalKinds.Drink);
    draft.Tags.Should().Contain(JournalTags.Alcohol);
    DayOf(draft.At).Should().Be(Today.AddDays(-1));
  }

  [Fact]
  public void Extract_LastNightDrink_MovesToYesterday()
  {
    var draft = _capture.Extract("Last night I had a couple of glasses of wine.", Now, London).Drafts.Single();

    DayOf(draft.At).Should().Be(Today.AddDays(-1));
  }

  [Fact]
  public void Extract_LastNightSleep_StaysOnTheDayTheNightEnded()
  {
    var draft = _capture.Extract("Slept badly last night.", Now, London).Drafts.Single();

    draft.Kind.Should().Be(JournalKinds.Sleep);
    draft.Tags.Should().Contain(JournalTags.PoorSleep);
    DayOf(draft.At).Should().Be(Today);
  }

  [Theory]
  [InlineData("Had a big bowl of pasta for dinner.", Portions.Large)]
  [InlineData("Just a small salad for lunch.", Portions.Small)]
  [InlineData("Had the usual portion of curry for dinner.", Portions.Usual)]
  public void Extract_PortionWhenSaid_IsKept(string message, string portion)
  {
    _capture.Extract(message, Now, London).Drafts.Single(d => d.Kind == JournalKinds.Food).Portion.Should().Be(portion);
  }

  [Fact]
  public void Extract_NoPortionSaid_HasNoPortion()
  {
    _capture.Extract("Had chicken and rice for lunch.", Now, London).Drafts.Single().Portion.Should().BeNull();
  }

  [Fact]
  public void Extract_CalorieAndGramCounts_AreNeverKept()
  {
    var draft = _capture.Extract("Ate a 600 calorie burrito with 40g of protein for lunch.", Now, London).Drafts.Single();

    draft.Text.Should().NotContain("600").And.NotContain("calorie").And.NotContain("40g");
    draft.Tags.Should().NotContain(t => t.Contains("cal"));
  }

  [Fact]
  public void Extract_CoffeeAfterThree_TagsLateCaffeine()
  {
    var draft = _capture.Extract("Had a coffee at 4pm.", Now, London).Drafts.Single();

    draft.Tags.Should().Contain([JournalTags.Caffeine, JournalTags.LateCaffeine]);
  }

  [Theory]
  [InlineData("Two coffees before noon.")]
  [InlineData("A coffee in the morning.")]
  public void Extract_CoffeeWithAMorningTime_SentInTheEvening_IsNotLate(string message)
  {
    var evening = new DateTime(2026, 11, 10, 21, 30, 0, DateTimeKind.Utc);

    var draft = _capture.Extract(message, evening, London).Drafts.Single();

    draft.Tags.Should().Contain(JournalTags.Caffeine).And.NotContain(JournalTags.LateCaffeine);
  }

  [Fact]
  public void Extract_MealNamedOnlyAsATime_IsNotFood()
  {
    var result = _capture.Extract("Had two coffees this morning and lifted legs at lunch, felt strong", Now, London);

    result.Drafts.Select(d => d.Kind).Should().BeEquivalentTo([JournalKinds.Drink, JournalKinds.Training]);
    result.Drafts.Single(d => d.Kind == JournalKinds.Drink).Text.Should().NotBeNull("the words go with the first kind kept");
  }

  [Theory]
  [InlineData("Had pasta at lunch.")]
  [InlineData("Ate at lunch with the team.")]
  [InlineData("Had a big lunch.")]
  public void Extract_MealEatenAtAMealTime_IsStillFood(string message)
  {
    _capture.Extract(message, Now, London).Drafts.Should().ContainSingle(d => d.Kind == JournalKinds.Food);
  }

  [Fact]
  public void Extract_LookingBackInTheEvening_OnlyAStatedTimeMakesCoffeeLate()
  {
    var evening = new DateTime(2026, 11, 10, 21, 30, 0, DateTimeKind.Utc);

    _capture.Extract("Two coffees", evening, London, CheckinTopics.Drinks, lookingBack: true).Drafts.Single()
      .Tags.Should().Contain(JournalTags.Caffeine).And.NotContain(JournalTags.LateCaffeine);
    _capture.Extract("A coffee at 4pm", evening, London, CheckinTopics.Drinks, lookingBack: true).Drafts.Single()
      .Tags.Should().Contain(JournalTags.LateCaffeine, "a stated time still counts");
    _capture.Extract("Just had a coffee.", evening, London).Drafts.Single()
      .Tags.Should().Contain(JournalTags.LateCaffeine, "in chat the message time is the drink time");
  }

  [Fact]
  public void Extract_MorningCoffee_IsNotLate()
  {
    var draft = _capture.Extract("Had a coffee this morning.", Now, London).Drafts.Single();

    draft.Tags.Should().Contain(JournalTags.Caffeine).And.NotContain(JournalTags.LateCaffeine);
  }

  [Fact]
  public void Extract_HardTraining_TagsLiftedAndHardSession()
  {
    var draft = _capture.Extract("Did a brutal leg day at the gym.", Now, London).Drafts.Single();

    draft.Kind.Should().Be(JournalKinds.Training);
    draft.Tags.Should().Contain([JournalTags.Lifted, JournalTags.HardSession]);
  }

  [Fact]
  public void Extract_BrutalDayAtWork_IsNotTraining()
  {
    _capture.Extract("Had a brutal day at work.", Now, London).Drafts.Should().NotContain(d => d.Kind == JournalKinds.Training);
  }

  [Fact]
  public void Extract_FoodWordWithoutEating_CapturesNothing()
  {
    _capture.Extract("I love pizza.", Now, London).Drafts.Should().BeEmpty();
  }

  [Fact]
  public void Extract_FragmentWithEatingTopic_CountsAsFood()
  {
    var draft = _capture.Extract("Salmon, rice and greens", Now, London, CheckinTopics.Eating).Drafts.Single();

    draft.Kind.Should().Be(JournalKinds.Food);
    draft.Tags.Should().Contain([JournalTags.HighProtein, JournalTags.Vegetables]);
  }

  [Fact]
  public void Extract_DurableFact_IsRemembered()
  {
    var result = _capture.Extract("I'm vegetarian and I work nights.", Now, London);

    result.Facts.Select(f => f.Key).Should().BeEquivalentTo(["diet", "work_pattern"]);
  }

  [Fact]
  public void Extract_FactInAQuestion_IsNotRemembered()
  {
    _capture.Extract("Should I be vegetarian?", Now, London).Facts.Should().BeEmpty();
  }

  [Fact]
  public void Extract_RanOutOfCoffee_IsNotARun()
  {
    _capture.Extract("Ran out of coffee today.", Now, London).Drafts.Should().NotContain(d => d.Kind == JournalKinds.Training);
  }
}
