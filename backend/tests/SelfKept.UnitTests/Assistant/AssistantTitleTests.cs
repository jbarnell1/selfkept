using FluentAssertions;
using SelfKept.Infrastructure.Services.Assistant;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

public class AssistantTitleTests
{
  [Fact]
  public void AutoTitle_FirstSentenceFits_IsThatSentence()
  {
    AssistantService.AutoTitle("Had oatmeal with berries for breakfast and two coffees. Slept badly and feel flat today.")
      .Should().Be("Had oatmeal with berries for breakfast and two coffees");
  }

  [Fact]
  public void AutoTitle_LongSentence_IsCutAtTheLastWholeWord()
  {
    var title = AssistantService.AutoTitle("Had oatmeal with berries for breakfast and two big coffees, slept badly, then a long walk");

    title.Should().Be("Had oatmeal with berries for breakfast and two big coffees");
    title.Length.Should().BeLessThanOrEqualTo(60);
  }

  [Fact]
  public void AutoTitle_CutBeforeAComma_DropsTheTrailingPunctuation()
  {
    AssistantService.AutoTitle("Breakfast was oatmeal with berries and honey, toast, jam, and coffee with milk and sugar")
      .Should().Be("Breakfast was oatmeal with berries and honey, toast, jam");
  }

  [Fact]
  public void AutoTitle_ShortOpener_IsNotTheWholeTitle()
  {
    AssistantService.AutoTitle("Hi. Why is my HRV low this week?").Should().Be("Hi. Why is my HRV low this week");
  }

  [Theory]
  [InlineData("Why is my HRV low?", "Why is my HRV low")]
  [InlineData("  salmon   and rice  ", "salmon and rice")]
  [InlineData("?", "?")]
  public void AutoTitle_ShortMessage_IsKeptWithoutTrailingPunctuation(string message, string expected)
  {
    AssistantService.AutoTitle(message).Should().Be(expected);
  }

  [Fact]
  public void CheckinTitle_Day_ReadsMonthFirst()
  {
    AssistantService.CheckinTitle(new DateOnly(2026, 11, 10)).Should().Be("Evening check-in, Nov 10");
  }
}
