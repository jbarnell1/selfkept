using System.Globalization;
using System.Text;
using SelfKept.Core.DTOs;

namespace SelfKept.Infrastructure.Services.Interpret;

/// <summary>The facts the Today sentence is built from. Cached as JSON next to the sentence.</summary>
public sealed record TodayFacts(
  double? SleepMinutes,
  string? SleepBand,
  string? HrvBand,
  string? RestingHrBand,
  /// <summary>Severity of the leading card, which the sentence points at instead of repeating (the card sits right below it).</summary>
  string? TopCardSeverity,
  int? LastCheckinMood,
  bool Learning);

/// <summary>
/// The templated Today sentence: one or two lines in the calm voice (docs/insights-design.md).
/// Pure, so the wording is tested without a database or a model.
/// </summary>
public static class TodayComposer
{
  private static readonly string[] MoodWords = ["", "very low", "low", "meh", "okay", "good", "great"];

  public static string Compose(TodayFacts facts)
  {
    var lines = new List<string>();

    var first = new StringBuilder();
    if (facts.SleepMinutes is { } minutes)
    {
      first.Append("You slept ").Append(FormatMinutes(minutes)).Append(" last night");
      first.Append(facts.SleepBand switch
      {
        BandPositions.Below => ", a little short of your usual.",
        BandPositions.Above => ", longer than your usual.",
        BandPositions.Inside => ", about your usual.",
        _ => ".",
      });
    }
    else if (!facts.Learning)
    {
      first.Append("No sleep recorded for last night.");
    }

    var readiness = Readiness(facts.HrvBand, facts.RestingHrBand);
    if (readiness != null)
    {
      if (first.Length > 0) first.Append(' ');
      first.Append(readiness);
    }
    if (first.Length > 0) lines.Add(first.ToString());

    if (facts.Learning)
    {
      lines.Add("Still learning your normal. A couple more weeks of data and the comparisons start.");
    }
    else if (facts.TopCardSeverity is { } severity)
    {
      lines.Add(severity switch
      {
        "clinician" => "One result is worth raising with a clinician; the details are below.",
        "watch" => "One thing is worth a look; it is below.",
        _ => "There is a note for you below.",
      });
    }
    else if (facts.LastCheckinMood is { } mood && mood >= 1 && mood <= 6)
    {
      lines.Add($"Your last check-in was {MoodWords[mood]}.");
    }
    else if (lines.Count == 0)
    {
      lines.Add("Nothing new to note today.");
    }

    return string.Join(" ", lines.Take(2));
  }

  private static string? Readiness(string? hrv, string? rhr)
  {
    var hrvText = hrv switch
    {
      BandPositions.Below => "HRV is below your usual range",
      BandPositions.Above => "HRV is above your usual range",
      BandPositions.Inside => "HRV is inside your usual range",
      _ => null,
    };
    var rhrText = rhr switch
    {
      BandPositions.Below => "resting heart rate is a touch below it",
      BandPositions.Above => "resting heart rate is a touch above it",
      BandPositions.Inside => "resting heart rate is where it usually sits",
      _ => null,
    };
    if (hrvText == null && rhrText == null) return null;
    if (hrvText != null && rhrText != null) return hrvText + " and " + rhrText + ".";
    if (hrvText != null) return hrvText + ".";
    return "Resting heart rate is " + rhr switch
    {
      BandPositions.Below => "a touch below your usual.",
      BandPositions.Above => "a touch above your usual.",
      _ => "where it usually sits.",
    };
  }

  /// <summary>435 minutes reads as "7 h 15 min"; 480 as "8 h"; 45 as "45 min".</summary>
  public static string FormatMinutes(double minutes)
  {
    var total = (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
    var hours = total / 60;
    var rest = total % 60;
    if (hours == 0) return $"{rest.ToString(CultureInfo.InvariantCulture)} min";
    if (rest == 0) return $"{hours.ToString(CultureInfo.InvariantCulture)} h";
    return $"{hours.ToString(CultureInfo.InvariantCulture)} h {rest.ToString(CultureInfo.InvariantCulture)} min";
  }
}
