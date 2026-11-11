using FluentAssertions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Services.Assistant.Lookup;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

/// <summary>
/// The cloud lookup scrub (docs/lookup-design.md, "The two modes"): hard identifiers always,
/// dates, ages and numbers in the anonymized mode, the "About me" block in the detailed one, and
/// the same output when it runs on its own output.
/// </summary>
public class LookupScrubberTests
{
  private static readonly LookupScrubContext Household = new(["Ada Lovelace", "Bob", "Zoë", "José María"], "mybox.tail1234.ts.net");

  private static string Anon(string text) => LookupScrubber.Anonymize(text, Household).Text;

  private static int Count(LookupScrubResult result, string kind) => result.Changes.SingleOrDefault(c => c.Kind == kind)?.Count ?? 0;

  [Fact]
  public void Anonymize_HouseholdNames_AreReplacedAsWholeWords()
  {
    var result = LookupScrubber.Anonymize("Bob and Ada both sleep badly. Ada Lovelace asked.", Household);

    result.Text.Should().Be("someone and someone both sleep badly. someone asked.");
    Count(result, LookupChangeKinds.Name).Should().Be(3);
  }

  [Fact]
  public void Anonymize_NameInsideAnotherWord_IsLeftAlone()
  {
    var result = LookupScrubber.Anonymize("Bobby went to Canada with Adam, and the kebab was fine.", Household);

    result.Text.Should().Be("Bobby went to Canada with Adam, and the kebab was fine.");
    result.Changes.Should().BeEmpty();
  }

  [Fact]
  public void Anonymize_NameInAnyCase_IsReplaced()
  {
    Anon("BOB snores and bob coughs").Should().Be("someone snores and someone coughs");
  }

  [Fact]
  public void Anonymize_Possessives_AreReplaced()
  {
    Anon("Is Bob's cough worse than Ada’s?").Should().Be("Is someone's cough worse than someone’s?");
  }

  [Fact]
  public void Anonymize_NonAsciiNames_AreReplacedInAnyCaseAndForm()
  {
    // "Zoë" typed with a combining diaeresis (NFD) is the same name.
    Anon("ZOË and Zoë and josé maría").Should().Be("someone and someone and someone");
    Anon("Zoëlla is not a household name").Should().Be("Zoëlla is not a household name");
  }

  [Fact]
  public void Anonymize_Emails_AreRemoved()
  {
    var result = LookupScrubber.Anonymize("Write to ada.l+health@example.co.uk or bob@mail.example.com.", Household);

    result.Text.Should().Be("Write to (email removed) or (email removed).");
    Count(result, LookupChangeKinds.Email).Should().Be(2);
  }

  [Theory]
  [InlineData("Call 555-123-4567 today")]
  [InlineData("Call (555) 123-4567 today")]
  [InlineData("Call +44 20 7946 0958 today")]
  [InlineData("Call 020 7946 0958 today")]
  [InlineData("Call 555.123.4567 today")]
  [InlineData("Call 5551234567 today")]
  [InlineData("Call 555 123 4567 today")]
  public void Anonymize_PhoneFormats_AreRemoved(string text)
  {
    var result = LookupScrubber.Anonymize(text, Household);

    result.Text.Should().Be("Call (phone number removed) today");
    Count(result, LookupChangeKinds.Phone).Should().Be(1);
  }

  [Fact]
  public void Anonymize_WebAddresses_AreRemoved()
  {
    var result = LookupScrubber.Anonymize("I read https://example.com/sleep?x=1, www.health.example.org and examine.com/supplements.", Household);

    result.Text.Should().Be("I read (link removed), (link removed) and (link removed).");
    Count(result, LookupChangeKinds.Link).Should().Be(3);
  }

  [Fact]
  public void Anonymize_ServerHostName_IsRemoved()
  {
    Anon("My server is mybox.tail1234.ts.net, also called mybox.").Should().Be("My server is (link removed), also called (link removed).");
  }

  [Fact]
  public void Anonymize_MissingSpaceAfterFullStop_IsNotALink()
  {
    Anon("I slept badly.Then I ran.").Should().Be("I slept badly.Then I ran.");
  }

  [Theory]
  [InlineData("It started on March 3 and got worse.", "It started recently and got worse.")]
  [InlineData("It started on 3 March and got worse.", "It started recently and got worse.")]
  [InlineData("It started Mar 3rd, 2026 and got worse.", "It started recently and got worse.")]
  [InlineData("It started on the 3rd of March 2026 and got worse.", "It started recently and got worse.")]
  [InlineData("It started on Monday, March 3 and got worse.", "It started recently and got worse.")]
  [InlineData("It started on 2026-03-03 and got worse.", "It started recently and got worse.")]
  [InlineData("It started on 03/03/2026 and got worse.", "It started recently and got worse.")]
  [InlineData("It started on 3.3.26 and got worse.", "It started recently and got worse.")]
  [InlineData("It started in March 2026 and got worse.", "It started in recently and got worse.")]
  [InlineData("It started in 2019 and got worse.", "It started in a past year and got worse.")]
  public void Anonymize_Dates_AreRemoved(string text, string expected)
  {
    var result = LookupScrubber.Anonymize(text, Household);

    result.Text.Should().Be(expected);
    Count(result, LookupChangeKinds.Date).Should().Be(1);
  }

  [Fact]
  public void Anonymize_MayAsAVerb_IsNotADate()
  {
    Anon("May I take it with food? It may help.").Should().Be("May I take it with food? It may help.");
  }

  [Theory]
  [InlineData("I'm 43 and sleep badly.", "I'm in my 40s and sleep badly.")]
  [InlineData("I am 43, female.", "I am in my 40s, female.")]
  [InlineData("I’m 43 years old.", "I’m in my 40s.")]
  [InlineData("She is 43 years old.", "She is in their 40s.")]
  [InlineData("A 67-year-old man.", "A 60-something man.")]
  [InlineData("Is it safe at age 43?", "Is it safe in their 40s?")]
  [InlineData("Patient aged 71.", "Patient in their 70s.")]
  [InlineData("My son is 7 yo.", "My son is under 10.")]
  public void Anonymize_StatedAges_BecomeDecades(string text, string expected)
  {
    var result = LookupScrubber.Anonymize(text, Household);

    result.Text.Should().Be(expected);
    Count(result, LookupChangeKinds.Age).Should().Be(1);
  }

  [Fact]
  public void Anonymize_NumberAfterImThatIsNotAnAge_IsNotAnAge()
  {
    var result = LookupScrubber.Anonymize("I'm 5 minutes in and I'm 187.4 lbs.", Household);

    result.Text.Should().Be("I'm 5 minutes in and I'm 190 lbs.");
    Count(result, LookupChangeKinds.Age).Should().Be(0);
    Count(result, LookupChangeKinds.Number).Should().Be(1);
  }

  [Theory]
  [InlineData("I weigh 187.4 lbs", "I weigh 190 lbs")]
  [InlineData("I weigh 187.4lbs", "I weigh 190lbs")]
  [InlineData("Ferritin 7.25 ug/L", "Ferritin 7.3 ug/L")]
  [InlineData("Took 1,234 mg", "Took 1,200 mg")]
  [InlineData("12345 steps", "12000 steps")]
  [InlineData("A dose of 0.0456 mg", "A dose of 0.046 mg")]
  [InlineData("HRV 9.96 ms", "HRV 10 ms")]
  [InlineData("Blood pressure 125/82", "Blood pressure 130/82")]
  public void Anonymize_Numbers_AreRoundedToTwoSignificantFigures(string text, string expected)
  {
    var result = LookupScrubber.Anonymize(text, Household);

    result.Text.Should().Be(expected);
    Count(result, LookupChangeKinds.Number).Should().Be(1);
  }

  [Fact]
  public void Anonymize_SmallNumbersAndNamesWithDigits_AreLeftAlone()
  {
    var result = LookupScrubber.Anonymize("Is 400 mg of B12, D3 or omega-3 too much at 7.5 hours of sleep and 62 bpm?", Household);

    result.Text.Should().Be("Is 400 mg of B12, D3 or omega-3 too much at 7.5 hours of sleep and 62 bpm?");
    result.Changes.Should().BeEmpty();
  }

  [Fact]
  public void Anonymize_ItsOwnOutput_ChangesNothing()
  {
    const string text = "Hi, I'm Ada (ada@example.com, +1 555 123 4567). On March 3 Bob, 43 years old, weighed 187.4 kg; " +
      "I'm 43 and my ferritin was 7.25. See https://example.com/x and mybox.tail1234.ts.net. A 67-year-old at age 71 since 2019.";
    var first = LookupScrubber.Anonymize(text, Household);

    var second = LookupScrubber.Anonymize(first.Text, Household);

    first.Changes.Should().NotBeEmpty();
    second.Text.Should().Be(first.Text);
    second.Changes.Should().BeEmpty();
  }

  [Fact]
  public void Detailed_KeepsTheQuestionAsTyped_MinusHardIdentifiers_AndAddsAboutMe()
  {
    var about = new LookupAboutMe(43, SexAtBirth.Female, ["Main goal: Sleep better.", "Supplement: Magnesium, 200 mg, evening."]);

    var result = LookupScrubber.Detailed("Bob says 187.4 mg on March 3 helped. Does it?", about, Household);

    result.Text.Should().Be(
      "someone says 187.4 mg on March 3 helped. Does it?\n\nAbout me:\n- In my 40s.\n- Sex at birth: female.\n- Main goal: Sleep better.\n- Supplement: Magnesium, 200 mg, evening.");
    result.Changes.Should().BeEquivalentTo(new[] { new LookupChangeDto(LookupChangeKinds.Name, 1), new LookupChangeDto(LookupChangeKinds.Record, 1) });
  }

  [Fact]
  public void Detailed_FactLineWithAName_IsScrubbedToo()
  {
    var about = new LookupAboutMe(null, SexAtBirth.Unspecified, ["Main goal: Run with Bob."]);

    LookupScrubber.Detailed("Does it help?", about, Household).Text.Should().Be("Does it help?\n\nAbout me:\n- Main goal: Run with someone.");
  }

  [Fact]
  public void Detailed_ItsOwnOutput_GetsNoSecondAboutMeBlock()
  {
    var about = new LookupAboutMe(43, SexAtBirth.Male, ["Supplement: Zinc."]);
    var first = LookupScrubber.Detailed("Does zinc help Bob?", about, Household);

    var second = LookupScrubber.Detailed(first.Text, about, Household);

    second.Text.Should().Be(first.Text);
    second.Changes.Should().BeEmpty();
  }

  [Fact]
  public void AboutMeBlock_NeverHoldsTheBirthDate_OnlyTheDecade()
  {
    var age = LookupScrubber.AgeOn(new DateOnly(1982, 11, 11), new DateOnly(2026, 11, 10));

    age.Should().Be(43);
    LookupScrubber.AboutMeBlock(new LookupAboutMe(age, SexAtBirth.Unspecified, []))
      .Should().Be("About me:\n- In my 40s.");
  }

  [Fact]
  public void AboutMeBlock_NothingToSay_IsNull()
  {
    LookupScrubber.AboutMeBlock(new LookupAboutMe(null, SexAtBirth.Unspecified, [])).Should().BeNull();
  }

  [Fact]
  public void FindHardIdentifiers_CleanText_IsEmpty()
  {
    LookupScrubber.FindHardIdentifiers("Does magnesium glycinate help with sleep in adults in their 40s?", Household).Should().BeEmpty();
  }

  [Fact]
  public void FindHardIdentifiers_EditedTextWithANameAndEmail_ReportsBoth()
  {
    var findings = LookupScrubber.FindHardIdentifiers("Ask Bob at bob@example.com about bob's sleep", Household);

    findings.Should().BeEquivalentTo(new[] { new LookupChangeDto(LookupChangeKinds.Email, 1), new LookupChangeDto(LookupChangeKinds.Name, 2) });
  }

  [Fact]
  public void FindHardIdentifiers_DatesAndNumbers_AreNotHardIdentifiers()
  {
    LookupScrubber.FindHardIdentifiers("On March 3 I was 43 and weighed 187.4 kg", Household).Should().BeEmpty();
  }
}
