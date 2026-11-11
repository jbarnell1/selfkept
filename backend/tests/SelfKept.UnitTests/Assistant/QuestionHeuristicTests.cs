using FluentAssertions;
using SelfKept.Infrastructure.Services.Assistant.Lookup;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

/// <summary>Which chat messages read as questions, for the cloud lookup offer.</summary>
public class QuestionHeuristicTests
{
  [Theory]
  [InlineData("Does magnesium help with sleep?")]
  [InlineData("does magnesium help with sleep")]
  [InlineData("How much protein do I need after lifting")]
  [InlineData("What is a normal resting heart rate")]
  [InlineData("Is it ok to run with a cold")]
  [InlineData("Should I take zinc at the first sign of a cold")]
  [InlineData("When should I take creatine")]
  [InlineData("Can I drink coffee before a blood test")]
  [InlineData("Slept badly again. Why does that keep happening")]
  [InlineData("I wonder whether ashwagandha does anything")]
  [InlineData("Tell me about vitamin D and colds")]
  public void IsQuestion_Questions_AreQuestions(string message)
  {
    QuestionHeuristic.IsQuestion(message).Should().BeTrue();
  }

  [Theory]
  [InlineData("Had oats and berries for breakfast.")]
  [InlineData("Did 5k this morning, felt good.")]
  [InlineData("Could barely sleep last night.")]
  [InlineData("Was tired all day.")]
  [InlineData("When I woke up my head hurt.")]
  [InlineData("Can barely walk after leg day.")]
  [InlineData("What a day.")]
  [InlineData("Thanks!")]
  [InlineData("")]
  [InlineData("   ")]
  public void IsQuestion_Statements_AreNotQuestions(string message)
  {
    QuestionHeuristic.IsQuestion(message).Should().BeFalse();
  }
}
