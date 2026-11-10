using FluentAssertions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Assistant;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

public class StateBriefComposerTests
{
  private static readonly BriefFacts NoFacts = new(null, [], null, null, []);

  [Fact]
  public void Compose_LongDayLog_StaysUnderTheCapAndKeepsTheEarlySections()
  {
    var log = Enumerable.Range(0, 400).Select(i => $"Mon 9 Nov, food: \"a long description of a meal number {i}\" [Vegetables]").ToList();
    var sections = new List<BriefSection>
    {
      new("Goals", ["Main goal: Lose fat. Weight 82.4 kg now, aiming for 78 kg."]),
      new("Readings against their usual range", ["Resting heart rate: 62 bpm last night; usual range 54 bpm to 58 bpm."]),
      new("Day log, last 7 days, newest first", log),
    };

    var brief = StateBriefComposer.Compose(sections, [], NoFacts);

    brief.Text.Length.Should().BeLessThanOrEqualTo(StateBriefComposer.Cap);
    brief.Text.Should().StartWith("Goals:").And.Contain("Resting heart rate: 62 bpm");
    brief.Text.Should().Contain("meal number 0").And.NotContain("meal number 399");
  }

  [Fact]
  public void Compose_NumberSet_HoldsEveryNumberInTheText()
  {
    var brief = StateBriefComposer.Compose(
      [new BriefSection("Readings", ["Resting heart rate: 62 bpm; usual range 54 to 58.", "Sleep: 7 h 15 min."])], [], NoFacts);

    brief.Numbers.Should().BeEquivalentTo(["62", "54", "58", "7", "15"]);
  }

  [Fact]
  public void Compose_EmptySections_AreLeftOut()
  {
    var brief = StateBriefComposer.Compose([new BriefSection("Targets", []), new BriefSection("Goals", ["No goal is set."])], [], NoFacts);

    brief.Text.Should().Be("Goals:\n- No goal is set.");
  }

  [Fact]
  public void Compose_SmallCap_NeverPassesIt()
  {
    var brief = StateBriefComposer.Compose([new BriefSection("Goals", ["one line that is short", "another line that is a bit longer"])], [], NoFacts, cap: 40);

    brief.Text.Length.Should().BeLessThanOrEqualTo(40);
  }
}
