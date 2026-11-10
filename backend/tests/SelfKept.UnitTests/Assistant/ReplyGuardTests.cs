using FluentAssertions;
using SelfKept.Infrastructure.Services.Assistant;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

public class ReplyGuardTests
{
  private const string Brief =
    "Readings against their usual range:\n" +
    "- Resting heart rate: 62 bpm last night; usual range 54 bpm to 58 bpm, so above your usual range.\n" +
    "- Sleep: 6 h 5 min last night.\n" +
    "Medicines and supplements they take:\n" +
    "- Supplement: Vitamin D3, daily; contains cholecalciferol 25 mcg.";

  private readonly ReplyGuard _guard = new();

  private static Core.Interfaces.GuardContext Context(string? message = null) => GuardNumbers.Context(Brief, message);

  [Theory]
  [InlineData("I have chest pain and my left arm hurts")]
  [InlineData("I can't breathe properly")]
  [InlineData("I fainted at work this afternoon")]
  [InlineData("My face is drooping and my speech is slurred speech")]
  [InlineData("I'm coughing up blood")]
  public void RedFlagReply_EmergencySymptom_ReturnsEmergencyTemplate(string message)
  {
    _guard.RedFlagReply(message).Should().Be(ReplyGuard.EmergencyReply);
  }

  [Theory]
  [InlineData("I keep thinking about killing myself")]
  [InlineData("Some days I want to die")]
  [InlineData("I've been having suicidal thoughts")]
  public void RedFlagReply_CrisisLanguage_ReturnsCrisisTemplate(string message)
  {
    _guard.RedFlagReply(message).Should().Be(ReplyGuard.CrisisReply);
  }

  [Theory]
  [InlineData("No chest pain, just a sore throat")]
  [InlineData("Had a great run and a big lunch")]
  [InlineData("My resting heart rate is up, why?")]
  public void RedFlagReply_OrdinaryMessage_ReturnsNull(string message)
  {
    _guard.RedFlagReply(message).Should().BeNull();
  }

  [Fact]
  public void CheckReply_NumbersFromTheBrief_Passes()
  {
    var verdict = _guard.CheckReply("Your resting heart rate was 62 bpm, above your usual 54 to 58.", Context());

    verdict.Ok.Should().BeTrue(verdict.Reason);
  }

  [Fact]
  public void CheckReply_NumberNotInTheBrief_IsRefused()
  {
    var verdict = _guard.CheckReply("Aim for 10000 steps tomorrow.", Context());

    verdict.Ok.Should().BeFalse();
    verdict.Reason.Should().Contain("10000");
  }

  [Fact]
  public void CheckReply_NumberWordNotInTheBrief_IsRefused()
  {
    _guard.CheckReply("A cold like this usually lasts about seven days.", Context()).Ok.Should().BeFalse();
  }

  [Fact]
  public void CheckReply_NumberFromThePersonsOwnMessage_Passes()
  {
    _guard.CheckReply("Three beers is worth noting next to tonight's sleep.", Context("I had three beers")).Ok.Should().BeTrue();
  }

  [Fact]
  public void CheckReply_DoseUnitNotInTheRecord_IsRefused()
  {
    var verdict = _guard.CheckReply("Your resting heart rate was 62 bpm, so take 62 mg of something.", Context());

    verdict.Ok.Should().BeFalse();
    verdict.Reason.Should().Contain("dose unit");
  }

  [Fact]
  public void CheckReply_DoseUnitListedInTheBrief_Passes()
  {
    _guard.CheckReply("Your vitamin D3 lists 25 mcg of cholecalciferol.", Context()).Ok.Should().BeTrue();
  }

  [Theory]
  [InlineData("Based on this I would diagnose a mild infection.")]
  [InlineData("You probably have a cold.")]
  [InlineData("You're anaemic.")]
  [InlineData("Infection detected in your readings.")]
  public void CheckReply_DiagnosisOrDetection_IsRefused(string reply)
  {
    _guard.CheckReply(reply, Context()).Ok.Should().BeFalse();
  }

  [Theory]
  [InlineData("Your heart rate is up because of the alcohol.")]
  [InlineData("Alcohol is damaging your sleep.")]
  [InlineData("That is caused by stress.")]
  public void CheckReply_CauseClaim_IsRefused(string reply)
  {
    _guard.CheckReply(reply, Context()).Ok.Should().BeFalse();
  }

  [Theory]
  [InlineData("Your metabolic age looks young.")]
  [InlineData("You could eat fewer calories tomorrow.")]
  [InlineData("Keep your macros balanced.")]
  [InlineData("Your stress score is high.")]
  [InlineData("You are close to the natural limit for muscle.")]
  [InlineData("Your oxygen is fine, nothing to worry about.")]
  [InlineData("Your LDL should now be lower.")]
  [InlineData("Keep the streak going!")]
  [InlineData("Nasal zinc can help.")]
  public void CheckReply_ThingsWeWillNotShow_AreRefused(string reply)
  {
    _guard.CheckReply(reply, Context()).Ok.Should().BeFalse();
  }

  [Fact]
  public void CheckReply_RemedyNotInPlaybookOrRecord_IsRefused()
  {
    var verdict = _guard.CheckReply("You could try echinacea.", Context());

    verdict.Ok.Should().BeFalse();
    verdict.Reason.Should().Contain("echinacea");
  }

  [Fact]
  public void CheckReply_RemedyInAPlaybookEntry_Passes()
  {
    var context = GuardNumbers.Context(Brief + "\n" + Playbook.ColdZincText);

    _guard.CheckReply("The review on zinc found it may shorten a cold, with low certainty.", context).Ok.Should().BeTrue();
  }

  [Fact]
  public void CheckReply_EmDash_IsRewrittenNotRefused()
  {
    var verdict = _guard.CheckReply("Sleep was short \u2014 worth an early night.", Context());

    verdict.Ok.Should().BeTrue();
    verdict.Text.Should().Be("Sleep was short, worth an early night.");
  }

  [Fact]
  public void CheckReply_TooLong_IsRefused()
  {
    _guard.CheckReply(new string('a', ReplyGuard.MaxReplyLength + 1), Context()).Ok.Should().BeFalse();
  }

  [Fact]
  public void CheckLine_Promise_IsRefused()
  {
    _guard.CheckLine("Great work, your recovery will improve tomorrow.", Core.Interfaces.GuardContext.Empty).Ok.Should().BeFalse();
  }

  [Fact]
  public void CheckLine_AnyNumber_IsRefused()
  {
    _guard.CheckLine("Great work, that was 5 sets.", Core.Interfaces.GuardContext.Empty).Ok.Should().BeFalse();
  }

  [Fact]
  public void CheckLine_EveryBankLine_Passes()
  {
    foreach (var line in AssistantTemplates.Encouragement.Values)
    {
      _guard.CheckLine(line, GuardNumbers.Context(line)).Ok.Should().BeTrue(line);
    }
  }
}
