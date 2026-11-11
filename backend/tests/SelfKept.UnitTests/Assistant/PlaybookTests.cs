using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Services.Assistant;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

public class PlaybookTests
{
  private static readonly DateOnly Today = new(2026, 11, 10);

  private static JournalEntry Entry(int daysAgo, params string[] tags) =>
    new() { Id = Guid.NewGuid(), Day = Today.AddDays(-daysAgo), Kind = JournalTags.KindOf(tags[0]), Tags = tags };

  private static PlaybookInputs Inputs(IReadOnlyList<JournalEntry>? journal = null, string[]? substances = null, int medicines = 0, double? sleep = 450, string[]? insights = null) =>
    new(Today, substances ?? [], medicines, journal ?? [], sleep, insights ?? []);

  private static IEnumerable<string> Keys(PlaybookInputs inputs) => Playbook.Applicable(inputs).Select(m => m.Key);

  [Fact]
  public void Applicable_NothingInTheRecord_IsEmpty()
  {
    Playbook.Applicable(Inputs()).Should().BeEmpty();
  }

  [Fact]
  public void Applicable_ShortNight_OffersSleep()
  {
    var match = Playbook.Applicable(Inputs(sleep: 365)).Single();

    match.Key.Should().Be(Playbook.Sleep);
    match.Why.Should().Contain("6 h 5 min");
  }

  [Fact]
  public void Applicable_CaffeineLogged_OffersTheCutOffWithTheDayCount()
  {
    var match = Playbook.Applicable(Inputs([Entry(0, JournalTags.Caffeine), Entry(2, JournalTags.LateCaffeine)])).Single();

    match.Key.Should().Be(Playbook.Caffeine);
    match.Why.Should().Contain("2 of the last 7 days");
  }

  [Fact]
  public void Applicable_CaffeineOnTheList_OffersTheCutOff()
  {
    Keys(Inputs(substances: [SubstanceTags.Caffeine])).Should().Equal(Playbook.Caffeine);
  }

  [Fact]
  public void Applicable_HardSessionTwoDaysAgo_OffersRecovery()
  {
    Keys(Inputs([Entry(1, JournalTags.HardSession)])).Should().Equal(Playbook.TrainingRecovery);
  }

  [Fact]
  public void Applicable_HardSessionAWeekAgo_DoesNot()
  {
    Keys(Inputs([Entry(5, JournalTags.HardSession)])).Should().BeEmpty();
  }

  [Fact]
  public void Applicable_ColdSymptom_OffersRestZincAndVitaminC()
  {
    Keys(Inputs([Entry(0, JournalTags.SoreThroat)])).Should().Equal(Playbook.ColdRest, Playbook.ColdZinc, Playbook.ColdVitaminC);
  }

  [Fact]
  public void Applicable_ColdSymptomAndZincOnTheList_LeavesZincOut()
  {
    Keys(Inputs([Entry(0, JournalTags.Cough)], substances: [SubstanceTags.Zinc])).Should().NotContain(Playbook.ColdZinc);
  }

  [Fact]
  public void Applicable_ColdSymptomWithMedicines_AddsThePharmacistCaution()
  {
    var zinc = Playbook.Applicable(Inputs([Entry(0, JournalTags.Congestion)], medicines: 1)).Single(m => m.Key == Playbook.ColdZinc);

    zinc.Cautions.Should().ContainSingle().Which.Should().Contain("pharmacist");
  }

  [Fact]
  public void Applicable_ReadingsAloneWithoutASymptom_OffersRestOnly()
  {
    Keys(Inputs(insights: [InsightKinds.IllnessPattern])).Should().Equal(Playbook.ColdRest);
  }

  [Fact]
  public void Entries_QuoteOnlyVerifiedFigures()
  {
    // B1 Stanley, B2 Gardiner, B3a Nault, B4 Hemila, E1 Watson: every number in the wording is one of theirs.
    var allowed = new HashSet<string> { "7", "45", "8.8", "13.2", "24", "48", "2024", "2.37", "8" };
    foreach (var text in new[] { Playbook.SleepText, Playbook.CaffeineText, Playbook.TrainingText, Playbook.ColdZincText, Playbook.ColdVitaminCText, Playbook.AlcoholText, Playbook.ColdRestText })
    {
      GuardNumbers.Of(text).Should().BeSubsetOf(allowed, text);
    }
    Playbook.ColdZincText.Should().NotContain("lozenge").And.NotContain("nasal").And.NotContain("antibiotic");
  }
}
