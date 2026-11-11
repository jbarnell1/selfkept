using System.Text.Json;
using System.Text.RegularExpressions;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// The evening check-in's state, stored as JSON on the conversation. Topic keys and flags only:
/// nothing the person typed is ever kept here.
/// </summary>
public sealed class CheckinState
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  /// <summary>Topics still to ask, in asking order. The one being asked is <see cref="Current"/>.</summary>
  public List<string> Pending { get; set; } = [];
  public string? Current { get; set; }
  public List<string> Done { get; set; } = [];
  public List<string> Skipped { get; set; } = [];
  public bool Left { get; set; }
  public bool Complete { get; set; }

  /// <summary>How many times the current topic has been asked again (mood words that were not understood).</summary>
  public int Retries { get; set; }

  /// <summary>Whether the record was already complete when the check-in opened.</summary>
  public bool NothingMissing { get; set; }

  public string ToJson() => JsonSerializer.Serialize(this, Json);

  public static CheckinState FromJson(string? json)
  {
    if (string.IsNullOrWhiteSpace(json) || json == "{}") return new CheckinState();
    try
    {
      return JsonSerializer.Deserialize<CheckinState>(json, Json) ?? new CheckinState();
    }
    catch (JsonException)
    {
      return new CheckinState();
    }
  }
}

public enum CheckinOutcome
{
  /// <summary>Something was saved for the topic.</summary>
  Harvested,

  /// <summary>The answer was "none today": a marker was saved so the day counts as known.</summary>
  None,

  /// <summary>The person passed on the topic. Nothing saved.</summary>
  Skipped,

  /// <summary>The answer could not be read (a mood with no mood word): ask once more.</summary>
  Retry,
}

/// <summary>
/// The check-in's flow, decided by code and never by the model (docs/assistant-design.md,
/// "Evening check-in"). It asks only about topics today's record is missing, one at a time, in
/// the order of <see cref="CheckinTopics.All"/>. Leaving keeps everything harvested and marks the
/// day partial; coming back resumes with whatever is still missing. Pure.
/// </summary>
public static class CheckinMachine
{
  public const int MaxRetries = 1;

  public static CheckinState Start(IReadOnlyCollection<string> missing)
  {
    var state = new CheckinState
    {
      Pending = CheckinTopics.All.Where(missing.Contains).ToList(),
      NothingMissing = missing.Count == 0,
    };
    Advance(state);
    return state;
  }

  /// <summary>Back after leaving or finishing: whatever is still missing and was not answered or skipped is asked.</summary>
  public static void Resume(CheckinState state, IReadOnlyCollection<string> missing)
  {
    state.Left = false;
    var answered = new HashSet<string>(state.Done.Concat(state.Skipped));
    state.Pending = CheckinTopics.All
      .Where(t => missing.Contains(t) && !answered.Contains(t) && t != state.Current)
      .ToList();
    if (state.Current == null) Advance(state);
    else state.Complete = false;
  }

  public static void Answer(CheckinState state, CheckinOutcome outcome)
  {
    if (state.Current is not { } topic) return;
    switch (outcome)
    {
      case CheckinOutcome.Retry when state.Retries < MaxRetries:
        state.Retries++;
        return;
      case CheckinOutcome.Retry:
      case CheckinOutcome.Skipped:
        state.Skipped.Add(topic);
        break;
      default:
        state.Done.Add(topic);
        break;
    }
    state.Retries = 0;
    state.Current = null;
    Advance(state);
  }

  /// <summary>
  /// Topics an answer already put on the log, though it was given for another question: they are
  /// done and not asked. The topic being asked is left to <see cref="Answer"/>.
  /// </summary>
  public static void Cover(CheckinState state, IEnumerable<string> topics)
  {
    foreach (var topic in topics.Distinct())
    {
      if (topic == state.Current || !state.Pending.Remove(topic)) continue;
      state.Done.Add(topic);
    }
  }

  public static void Leave(CheckinState state)
  {
    state.Left = true;
  }

  public static string Status(CheckinState? state) =>
    state == null ? CheckinStatuses.NotStarted
    : state.Complete && !state.Left ? CheckinStatuses.Done
    : CheckinStatuses.Partial;

  private static void Advance(CheckinState state)
  {
    if (state.Pending.Count > 0)
    {
      state.Current = state.Pending[0];
      state.Pending.RemoveAt(0);
      state.Complete = false;
    }
    else
    {
      state.Current = null;
      state.Complete = true;
    }
  }
}

/// <summary>What one evening answer gives: drafts to save, a mood, the outcome and the tone for the encouragement line.</summary>
public sealed record CheckinHarvestResult(IReadOnlyList<CaptureDraft> Drafts, int? Mood, CheckinOutcome Outcome, string Tone);

/// <summary>
/// Reads one answer for one topic, by code. The capture lexicon does the tagging (with the topic
/// as a hint, so "salmon, rice and greens" counts as food). An answer that says "none" for the
/// topic saves a marker entry, so a target can count the day as known: rest_day for training,
/// skipped_meal for eating, no_alcohol and no_caffeine for drinks, no_symptoms for symptoms. Pure.
/// </summary>
public static partial class CheckinHarvest
{
  public static CheckinHarvestResult Interpret(IJournalCapture capture, string topic, string answer, DateTime nowUtc, TimeZoneInfo zone)
  {
    var text = answer.Trim();
    var lower = text.ToLowerInvariant();
    if (SkipAnswer().IsMatch(lower)) return new CheckinHarvestResult([], null, CheckinOutcome.Skipped, CheckinTones.Noted);

    if (topic == CheckinTopics.Mood)
    {
      var mood = Mood(lower);
      return mood is { } m
        ? new CheckinHarvestResult([], m, CheckinOutcome.Harvested, m >= 5 ? CheckinTones.High : m >= 3 ? CheckinTones.Middle : CheckinTones.Low)
        : new CheckinHarvestResult([], null, CheckinOutcome.Retry, CheckinTones.Noted);
    }

    var kind = KindOf(topic);
    var drafts = capture.Extract(text, nowUtc, zone, topic, lookingBack: true).Drafts.ToList();
    var ofTopic = drafts.Where(d => d.Kind == kind).ToList();
    var bareNo = BareNo().IsMatch(lower);

    // The lexicon found something, but none of it answers this question (a run given for the
    // eating question). It is not filed under the topic in the person's words: the flow keeps
    // what was found and asks once more.
    if (ofTopic.Count == 0 && !bareNo && !SaysNone(topic, lower) && drafts.Any(d => TopicOf(d.Kind) != null))
    {
      return new CheckinHarvestResult([], null, CheckinOutcome.Retry, CheckinTones.Noted);
    }

    switch (topic)
    {
      case CheckinTopics.Drinks:
      {
        var tags = ofTopic.SelectMany(d => d.Tags).ToHashSet();
        var markers = new List<string>();
        if (!tags.Contains(JournalTags.Alcohol) && (bareNo || NoAlcohol().IsMatch(lower))) markers.Add(AssistantTags.NoAlcohol);
        if (!tags.Contains(JournalTags.Caffeine) && (bareNo || NoCaffeine().IsMatch(lower))) markers.Add(AssistantTags.NoCaffeine);
        if (markers.Count > 0) drafts.Add(new CaptureDraft(JournalKinds.Drink, null, markers, null, nowUtc));
        if (ofTopic.Count == 0 && markers.Count == 0) drafts.Add(Fallback(kind, text, nowUtc));
        var tone = tags.Contains(JournalTags.Alcohol) ? CheckinTones.Alcohol
          : tags.Contains(JournalTags.LateCaffeine) ? CheckinTones.LateCaffeine
          : ofTopic.Count == 0 && markers.Count > 0 ? CheckinTones.None
          : CheckinTones.Noted;
        return new CheckinHarvestResult(drafts, null, ofTopic.Count == 0 && markers.Count > 0 ? CheckinOutcome.None : CheckinOutcome.Harvested, tone);
      }
      case CheckinTopics.Training:
      {
        if (ofTopic.Count == 0 && (bareNo || NoTraining().IsMatch(lower)))
        {
          drafts.Add(new CaptureDraft(JournalKinds.Training, null, [JournalTags.RestDay], null, nowUtc));
          return new CheckinHarvestResult(drafts, null, CheckinOutcome.None, CheckinTones.None);
        }
        if (ofTopic.Count == 0) drafts.Add(Fallback(kind, text, nowUtc));
        var tags = ofTopic.SelectMany(d => d.Tags).ToHashSet();
        var tone = tags.Contains(JournalTags.RestDay) ? CheckinTones.None
          : tags.Contains(JournalTags.HardSession) ? CheckinTones.Hard
          : tags.Overlaps([JournalTags.Lifted, JournalTags.Cardio, JournalTags.Walk]) ? CheckinTones.Done
          : CheckinTones.Noted;
        return new CheckinHarvestResult(drafts, null, tone == CheckinTones.None ? CheckinOutcome.None : CheckinOutcome.Harvested, tone);
      }
      case CheckinTopics.Symptoms:
      {
        if (ofTopic.Count == 0 && (bareNo || FeelingFine().IsMatch(lower)))
        {
          drafts.Add(new CaptureDraft(JournalKinds.Symptom, null, [AssistantTags.NoSymptoms], null, nowUtc));
          return new CheckinHarvestResult(drafts, null, CheckinOutcome.None, CheckinTones.None);
        }
        if (ofTopic.Count == 0) drafts.Add(Fallback(kind, text, nowUtc));
        return new CheckinHarvestResult(drafts, null, CheckinOutcome.Harvested, CheckinTones.Unwell);
      }
      default:
      {
        // Eating.
        var tags = ofTopic.SelectMany(d => d.Tags).ToHashSet();
        if (ofTopic.Count == 0 && (bareNo || NotEating().IsMatch(lower)))
        {
          drafts.Add(new CaptureDraft(JournalKinds.Food, text, [JournalTags.SkippedMeal], null, nowUtc));
          return new CheckinHarvestResult(drafts, null, CheckinOutcome.None, CheckinTones.None);
        }
        if (ofTopic.Count == 0) drafts.Add(Fallback(kind, text, nowUtc));
        var tone = tags.Contains(JournalTags.SkippedMeal) ? CheckinTones.None
          : tags.Overlaps([JournalTags.Sweets, JournalTags.FriedFood, JournalTags.Takeaway]) ? CheckinTones.Treat
          : tags.Overlaps([JournalTags.Vegetables, JournalTags.Fruit, JournalTags.WholeGrains, JournalTags.HighProtein, JournalTags.HomeCooked]) ? CheckinTones.Balanced
          : CheckinTones.Noted;
        return new CheckinHarvestResult(drafts, null, CheckinOutcome.Harvested, tone);
      }
    }
  }

  /// <summary>1 (very low) to 6 (great), the scale of the existing mood check-in. Null when no mood word is there.</summary>
  public static int? Mood(string lower)
  {
    var digit = MoodDigit().Match(lower);
    if (digit.Success) return int.Parse(digit.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
    if (VeryLow().IsMatch(lower)) return 1;
    if (NotBad().IsMatch(lower)) return 4;
    if (Low().IsMatch(lower)) return 2;
    if (Great().IsMatch(lower)) return 6;
    if (Good().IsMatch(lower)) return 5;
    if (Meh().IsMatch(lower)) return 3;
    if (Okay().IsMatch(lower)) return 4;
    return null;
  }

  /// <summary>The topic a day log kind answers, or null for a kind no question asks about (a note).</summary>
  public static string? TopicOf(string kind) => kind switch
  {
    JournalKinds.Food => CheckinTopics.Eating,
    JournalKinds.Training => CheckinTopics.Training,
    JournalKinds.Drink => CheckinTopics.Drinks,
    JournalKinds.Symptom => CheckinTopics.Symptoms,
    _ => null,
  };

  /// <summary>True when the answer says there was none of the topic ("no training", "feeling fine").</summary>
  private static bool SaysNone(string topic, string lower) => topic switch
  {
    CheckinTopics.Drinks => NoAlcohol().IsMatch(lower) || NoCaffeine().IsMatch(lower),
    CheckinTopics.Training => NoTraining().IsMatch(lower),
    CheckinTopics.Symptoms => FeelingFine().IsMatch(lower),
    _ => NotEating().IsMatch(lower),
  };

  private static string KindOf(string topic) => topic switch
  {
    CheckinTopics.Eating => JournalKinds.Food,
    CheckinTopics.Training => JournalKinds.Training,
    CheckinTopics.Drinks => JournalKinds.Drink,
    CheckinTopics.Symptoms => JournalKinds.Symptom,
    _ => JournalKinds.Note,
  };

  /// <summary>The answer in the person's own words, when the lexicon found nothing to tag.</summary>
  private static CaptureDraft Fallback(string kind, string text, DateTime nowUtc) =>
    new(kind, JournalCapture.Clean(text) ?? text, [], null, nowUtc);

  [GeneratedRegex(@"^\s*(?:skip|pass|next|rather not(?: say)?|prefer not to say|no comment|not telling|n/?a)\s*[.!]*\s*$", RegexOptions.CultureInvariant)]
  private static partial Regex SkipAnswer();

  [GeneratedRegex(@"^\s*(?:(?:no|nope|nah|none|nothing|not really|not today|zero|neither|no to both|nothing at all|not at all)[\s.!,]*)+(?:thanks|thank you)?[\s.!]*$", RegexOptions.CultureInvariant)]
  private static partial Regex BareNo();

  [GeneratedRegex(@"\bno (?:alcohol|drinks?|booze|beer|wine)\b|\bdidn'?t drink\b|\bdid not drink\b|\bsober\b|\bdry (?:day|night)\b|\bnothing to drink\b|\bno to (?:both|alcohol)\b|\bneither\b", RegexOptions.CultureInvariant)]
  private static partial Regex NoAlcohol();

  [GeneratedRegex(@"\bno (?:caffeine|coffee|tea|coffees)\b|\bno to (?:both|caffeine)\b|\bdecaf\b|\bneither\b|\bdidn'?t have (?:any )?(?:coffee|caffeine)\b", RegexOptions.CultureInvariant)]
  private static partial Regex NoCaffeine();

  [GeneratedRegex(@"\b(?:didn'?t|did not|no|not)\s+(?:train|lift|work out|workout|exercise|go to the gym|gym|training)\b|\brest(?:ed)?\b|\bday off\b|\btook it easy\b", RegexOptions.CultureInvariant)]
  private static partial Regex NoTraining();

  [GeneratedRegex(@"\b(?:fine|good|great|well|ok|okay|all good|no symptoms|nothing to report|feeling normal|healthy|all clear)\b", RegexOptions.CultureInvariant)]
  private static partial Regex FeelingFine();

  [GeneratedRegex(@"\b(?:didn'?t eat|did not eat|barely ate|hardly ate|not much|fasted|fasting|forgot to eat)\b", RegexOptions.CultureInvariant)]
  private static partial Regex NotEating();

  [GeneratedRegex(@"(?<![\d.])(?<n>[1-6])(?:\s*(?:/|out of)\s*6)?(?![\d.])", RegexOptions.CultureInvariant)]
  private static partial Regex MoodDigit();

  [GeneratedRegex(@"\b(?:very low|terrible|awful|miserable|horrible|really bad|really low|dreadful|worst)\b", RegexOptions.CultureInvariant)]
  private static partial Regex VeryLow();

  [GeneratedRegex(@"\bnot (?:too |that |so )?bad\b", RegexOptions.CultureInvariant)]
  private static partial Regex NotBad();

  [GeneratedRegex(@"\b(?:low|down|bad|rough|sad|not great|not good|poor|crap|rubbish|flat)\b", RegexOptions.CultureInvariant)]
  private static partial Regex Low();

  [GeneratedRegex(@"\b(?:great|amazing|fantastic|excellent|brilliant|awesome|wonderful|superb)\b", RegexOptions.CultureInvariant)]
  private static partial Regex Great();

  [GeneratedRegex(@"\b(?:good|happy|well|positive|pretty good|nice|cheerful)\b", RegexOptions.CultureInvariant)]
  private static partial Regex Good();

  [GeneratedRegex(@"\b(?:meh|so-so|so so|average|middling|blah)\b", RegexOptions.CultureInvariant)]
  private static partial Regex Meh();

  [GeneratedRegex(@"\b(?:okay|ok|fine|alright|all right|not bad|decent)\b", RegexOptions.CultureInvariant)]
  private static partial Regex Okay();
}
