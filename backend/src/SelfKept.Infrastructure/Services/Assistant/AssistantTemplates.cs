using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// Every sentence the assistant says without the model: the replies used when the model is off,
/// unreachable or refused by the guard, the evening check-in's questions and its bank of
/// encouragement lines, and the nudge texts. None of them states a number except the facts taken
/// from the person's own record, and none promises an outcome.
/// </summary>
public static class AssistantTemplates
{
  public const string SystemPrompt =
    "You are the assistant inside a private, self-hosted health record. You talk with one person about their own record, in plain, warm, calm words. " +
    "Use only the facts in the brief below and what the person tells you. Rules you must follow: " +
    "do not state any number, date, range or unit that is not in the brief or the person's message; " +
    "never give a dose or amount of any medicine or supplement unless the brief lists it; " +
    "suggest an action only when it is a playbook entry in the brief, say its evidence grade, and say so plainly when the evidence is weak; " +
    "never diagnose, never say the person has a condition, and never say what caused a reading, only what tends to go together in their own record; " +
    "no calorie or macro counts; no scores, streaks or badges; for anything beyond self-care, suggest a clinician. " +
    "Keep replies short: two to five sentences, no headings, no lists, no em dashes. " +
    "When the app has saved something to the day log it shows that already, so only mention it briefly.";

  public const string LinePrompt =
    "Reword the line below in a warm, plain voice, as one or two short sentences. Keep its meaning. " +
    "Do not add numbers, promises, advice, causes or medical claims, and no question. No em dashes. Output only the reworded line.";

  public const string BriefPrompt =
    "Rewrite the morning note below as one calm paragraph of at most five sentences, in plain second person. " +
    "Keep every fact and number exactly as given and add none. No advice beyond the note, no causes, no diagnosis, no em dashes. Output only the paragraph.";

  /// <summary>When the model is off or cannot be reached: what was saved, then the record's own facts.</summary>
  public static string Unavailable(BriefFacts facts, bool saved) =>
    Join(
      saved ? "Saved to today's log." : null,
      "The model is not reachable right now, so here is a plain summary from your record.",
      FactsText(facts));

  /// <summary>When the guard refused the model's answer.</summary>
  public static string Refused(BriefFacts facts, bool saved) =>
    Join(
      saved ? "Saved to today's log." : null,
      "I held that answer back because it did not pass my checks. Here is what your record says.",
      FactsText(facts));

  private static string FactsText(BriefFacts facts)
  {
    var parts = new List<string?> { facts.SleepText };
    parts.AddRange(facts.ReadingTexts.Take(2));
    parts.Add(facts.TargetsText);
    parts.Add(facts.InsightText);
    var text = Join(parts.ToArray());
    return text.Length == 0 ? "There is not much in your record for today yet." : text;
  }

  public static string Join(params string?[] parts) =>
    string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

  // --- evening check-in -------------------------------------------------------------------------

  public const string Opening = "Let's close the day.";
  public const string NothingMissing = "Your day already has food, training, drinks, symptoms and mood on record, so there is nothing to ask tonight. Thank you.";
  public const string Completed = "That's the day. Everything you told me is saved.";
  public const string AlreadyDone = "Today's check-in is done. Anything you add in chat still goes on today's log.";
  public const string Left = "No problem. I have kept everything you told me, and today is marked partial.";
  public const string MoodRetry = "Sorry, I did not catch that. Was your mood great, good, okay, meh, low or very low?";
  public const string SkippedLine = "No problem, we can leave that one.";

  public static string Question(string topic) => topic switch
  {
    CheckinTopics.Eating => "How was your eating today?",
    CheckinTopics.Training => "Did you lift or train today? How did it go?",
    CheckinTopics.Drinks => "Any alcohol or caffeine today?",
    CheckinTopics.Symptoms => "How are you feeling physically? Any symptoms today, like a sore throat, a headache or tiredness?",
    CheckinTopics.Mood => "And how was your mood today: great, good, okay, meh, low or very low?",
    _ => "Anything else about today?",
  };

  /// <summary>The bank of encouragement lines, keyed by topic and by what the answer said.</summary>
  public static readonly IReadOnlyDictionary<(string Topic, string Tone), string> Encouragement = new Dictionary<(string, string), string>
  {
    [(CheckinTopics.Eating, CheckinTones.Balanced)] = "That sounds like a good day of eating.",
    [(CheckinTopics.Eating, CheckinTones.Treat)] = "Noted, and no judgement. One day of food is just one day.",
    [(CheckinTopics.Eating, CheckinTones.None)] = "Thanks for saying. A light eating day is worth having on the record next to tonight's sleep.",
    [(CheckinTopics.Eating, CheckinTones.Noted)] = "Thanks, that is on today's log.",
    [(CheckinTopics.Training, CheckinTones.Hard)] = "That was great work today. It will be interesting to see if it is reflected in tomorrow's recovery readings.",
    [(CheckinTopics.Training, CheckinTones.Done)] = "Nice work getting that in. Tomorrow's readings may show how your body took it.",
    [(CheckinTopics.Training, CheckinTones.None)] = "Rest days count too. Recovery is part of training.",
    [(CheckinTopics.Training, CheckinTones.Noted)] = "Thanks, that is on today's log.",
    [(CheckinTopics.Drinks, CheckinTones.Alcohol)] = "Thanks for logging it honestly. It will be interesting to see whether tonight's sleep and tomorrow's resting heart rate look any different.",
    [(CheckinTopics.Drinks, CheckinTones.LateCaffeine)] = "Noted. Caffeine late in the day can show up in sleep, so tomorrow's sleep reading is worth a look.",
    [(CheckinTopics.Drinks, CheckinTones.Noted)] = "Thanks, that is noted.",
    [(CheckinTopics.Drinks, CheckinTones.None)] = "A clear day on drinks, noted.",
    [(CheckinTopics.Symptoms, CheckinTones.Unwell)] = "Sorry you are not feeling great. I have noted it, and tomorrow's readings may help show how things are going. Take it easy if you can.",
    [(CheckinTopics.Symptoms, CheckinTones.None)] = "Good to hear you are feeling well.",
    [(CheckinTopics.Mood, CheckinTones.High)] = "Glad to hear it.",
    [(CheckinTopics.Mood, CheckinTones.Middle)] = "Thanks for telling me.",
    [(CheckinTopics.Mood, CheckinTones.Low)] = "Thanks for telling me. Low days happen, and if they keep coming it may be worth talking to someone.",
  };

  public static string EncouragementFor(string topic, string tone) =>
    Encouragement.TryGetValue((topic, tone), out var line) ? line
    : Encouragement.TryGetValue((topic, CheckinTones.Noted), out var noted) ? noted
    : "Thanks, that is noted.";

  // --- nudges ------------------------------------------------------------------------------------

  /// <summary>Fixed and free of numbers and health values: a lock screen may show it to anyone.</summary>
  public const string NudgeTitle = "Evening check-in";
  public const string NudgeMessage = "Ready for a quick end of day chat? A minute or two closes today's log.";
  public const string NudgeTestTitle = "Test reminder";
  public const string NudgeTestMessage = "This is a test. Evening reminders will arrive here.";
}

/// <summary>What an evening answer said, for picking its encouragement line.</summary>
public static class CheckinTones
{
  public const string Balanced = "balanced";
  public const string Treat = "treat";
  public const string Noted = "noted";
  public const string None = "none";
  public const string Hard = "hard";
  public const string Done = "done";
  public const string Alcohol = "alcohol";
  public const string LateCaffeine = "late_caffeine";
  public const string Unwell = "unwell";
  public const string High = "high";
  public const string Middle = "middle";
  public const string Low = "low";
}
