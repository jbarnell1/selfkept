using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>What the playbook rules read: the person's own record around today.</summary>
public sealed record PlaybookInputs(
  DateOnly Today,
  IReadOnlyCollection<string> Substances,
  int ActiveMedicines,
  IReadOnlyList<JournalEntry> Journal,
  double? SleepMinutesLastNight,
  IReadOnlyCollection<string> InsightKinds);

/// <summary>
/// The reviewed suggestions the assistant may offer (docs/assistant-design.md, "Playbook"). Code
/// decides which apply; the model only words them. Every figure here comes from a source marked
/// VERIFIED in the research notes (Watson 2015, Gardiner 2023, Stanley 2013, Nault 2024, Hemila
/// 2013). The zinc entry follows the review's own scope: all zinc formulations, the adverse
/// events it lists, nothing about nasal zinc and no interaction claim.
/// </summary>
public static class Playbook
{
  public const string Sleep = "sleep_seven_hours";
  public const string Caffeine = "caffeine_cutoff";
  public const string Alcohol = "alcohol_nights";
  public const string TrainingRecovery = "training_recovery";
  public const string ColdRest = "early_cold_rest";
  public const string ColdZinc = "early_cold_zinc";
  public const string ColdVitaminC = "early_cold_vitamin_c";

  public const string SleepText =
    "Sleep researchers' consensus for adults is 7 or more hours a night on a regular basis. A steady bedtime that leaves room for that is the simplest place to start.";

  public const string CaffeineText =
    "In a pooled analysis of caffeine studies, caffeine cut total sleep by about 45 minutes and sleep efficiency by about 7 %. " +
    "The authors suggest a coffee at least 8.8 hours before bed, and a standard pre-workout serve at least 13.2 hours before.";

  public const string AlcoholText =
    "Nights after drinking are worth setting beside your own dry nights. The morning brief puts resting heart rate and sleep side by side, so your own pattern shows over time.";

  public const string TrainingText =
    "Heart rate variability usually takes up to 24 hours to recover after an easy session, 24 to 48 hours after a threshold session, and at least 48 hours after a high-intensity one. " +
    "An easier day after a hard one gives the readings room to come back.";

  public const string ColdRestText =
    "Rest and an easy day come first. Readings alone can only say that something looks unusual for you; a symptom you log is what makes a cold the likelier story. Anything beyond self-care is for a clinician.";

  public const string ColdZincText =
    "A 2024 Cochrane review found that zinc taken once a cold has started may shorten it by about 2.37 days on average, with low certainty. " +
    "Side effects were more common with zinc: an unpleasant taste, loss of smell, vomiting, stomach cramps and diarrhoea. " +
    "Taken to prevent colds, it may make little or no difference.";

  public const string ColdVitaminCText =
    "A Cochrane review found that vitamin C taken regularly, before any cold, shortened colds by about 8 % in adults. " +
    "Started after symptoms begin, it showed no consistent effect on how long or how bad a cold was.";

  private const string MedicinesCaution = "You have medicines on your list. Ask a pharmacist before adding a supplement.";

  public static IReadOnlyList<PlaybookMatch> Applicable(PlaybookInputs input)
  {
    var matches = new List<PlaybookMatch>();

    // Sleep: a short night, a short run of nights, or a poor or late night noted.
    string? sleepWhy = null;
    if (input.SleepMinutesLastNight is { } minutes && minutes < 420)
    {
      sleepWhy = $"Last night's sleep was {TodayComposer.FormatMinutes(minutes)}.";
    }
    else if (input.InsightKinds.Contains(InsightKinds.SleepDebt))
    {
      sleepWhy = "Short nights showed up in your readings this week.";
    }
    else if (DaysWith(input, t => t is JournalTags.PoorSleep or JournalTags.LateNight, 3) > 0)
    {
      sleepWhy = "You noted a poor or late night in the last few days.";
    }
    if (sleepWhy != null)
    {
      matches.Add(new PlaybookMatch(Sleep, "sleep", "Seven hours or more", SleepText, "consensus",
        "Watson 2015, Sleep 38(6):843-844 (AASM and SRS consensus)", "https://pmc.ncbi.nlm.nih.gov/articles/PMC4434546/", sleepWhy, []));
    }

    // Caffeine: in the day log this week, or on the list.
    var caffeineDays = DaysWith(input, t => t is JournalTags.Caffeine or JournalTags.LateCaffeine);
    if (caffeineDays > 0 || input.Substances.Contains(SubstanceTags.Caffeine))
    {
      var why = caffeineDays > 0
        ? $"Caffeine is in your day log on {caffeineDays} of the last 7 days."
        : "Something on your list contains caffeine.";
      matches.Add(new PlaybookMatch(Caffeine, "caffeine", "A caffeine cut-off", CaffeineText, "meta-analysis",
        "Gardiner 2023, Sleep Med Rev 69:101764", "https://pubmed.ncbi.nlm.nih.gov/36870101/", why, []));
    }

    // Alcohol: in the day log this week. No outside figure is quoted.
    var alcoholDays = DaysWith(input, t => t == JournalTags.Alcohol);
    if (alcoholDays > 0)
    {
      matches.Add(new PlaybookMatch(Alcohol, "alcohol", "Your nights after drinks", AlcoholText, "personal pattern",
        "Your own record; no outside figure is quoted", string.Empty, $"Alcohol is in your day log on {alcoholDays} of the last 7 days.", []));
    }

    // Training load: a hard session in the last two days, or a week well above usual.
    var hardRecently = DaysWith(input, t => t == JournalTags.HardSession, 2) > 0;
    if (hardRecently || input.InsightKinds.Contains(InsightKinds.TrainingSpike))
    {
      var why = hardRecently ? "You logged a hard session in the last two days." : "Training load is well above your usual this week.";
      matches.Add(new PlaybookMatch(TrainingRecovery, "training", "Room to recover", TrainingText, "review",
        "Stanley 2013, Sports Med 43(12):1259-77", "https://pubmed.ncbi.nlm.nih.gov/23912805/", why, []));
    }

    // Early cold: a cold-like symptom logged in the last three days. Readings alone never qualify
    // for the supplements; they only bring up rest.
    var coldDays = DaysWith(input, t => JournalTags.ColdLike.Contains(t), 3);
    if (coldDays > 0 || input.InsightKinds.Contains(InsightKinds.IllnessPattern))
    {
      var why = coldDays > 0 ? "You logged a cold-like symptom in the last three days." : "Your overnight readings look unusual for you.";
      matches.Add(new PlaybookMatch(ColdRest, "early_cold", "Rest first", ColdRestText, "self-care",
        "General self-care; no figure is quoted", string.Empty, why, []));
    }
    if (coldDays > 0)
    {
      var cautions = input.ActiveMedicines > 0 ? new[] { MedicinesCaution } : [];
      if (!input.Substances.Contains(SubstanceTags.Zinc))
      {
        matches.Add(new PlaybookMatch(ColdZinc, "early_cold", "Zinc once a cold starts", ColdZincText, "low certainty",
          "Nault 2024, Cochrane Database Syst Rev CD014914", "https://pubmed.ncbi.nlm.nih.gov/38719213/",
          "You logged a cold-like symptom and zinc is not on your list.", cautions));
      }
      matches.Add(new PlaybookMatch(ColdVitaminC, "early_cold", "Vitamin C, and its timing", ColdVitaminCText, "Cochrane review",
        "Hemila 2013, Cochrane Database Syst Rev CD000980", "https://pubmed.ncbi.nlm.nih.gov/23440782/",
        "You logged a cold-like symptom.", cautions));
    }
    return matches;
  }

  /// <summary>How many of the last <paramref name="window"/> days (today included) have an entry with a matching tag.</summary>
  private static int DaysWith(PlaybookInputs input, Func<string, bool> tag, int window = 7)
  {
    var from = input.Today.AddDays(-(window - 1));
    return input.Journal
      .Where(j => j.Day >= from && j.Day <= input.Today && j.Tags.Any(tag))
      .Select(j => j.Day)
      .Distinct()
      .Count();
  }
}

/// <summary>Loads the playbook inputs from the person's own record.</summary>
public sealed class PlaybookService : IPlaybookService
{
  private readonly IJournalRepository _journal;
  private readonly IMedicationRepository _medications;
  private readonly IDailySummaryRepository _summaries;
  private readonly IInsightRepository _insights;

  public PlaybookService(IJournalRepository journal, IMedicationRepository medications, IDailySummaryRepository summaries, IInsightRepository insights)
  {
    _journal = journal;
    _medications = medications;
    _summaries = summaries;
    _insights = insights;
  }

  public async Task<IReadOnlyList<PlaybookMatch>> ApplicableAsync(Person person, DateOnly today, CancellationToken ct = default)
  {
    var journal = await _journal.ListOwnAsync(person.Id, today.AddDays(-6), today, ct);
    var active = (await _medications.ListOwnAsync(person.Id, ct)).Where(m => m.ActiveOn(today)).ToList();
    var sleep = await _summaries.GetOwnRangeAsync(person.Id, [MetricKeys.SleepStage], today.AddDays(-1), today, ct);
    var lastNight = (sleep.FirstOrDefault(s => s.Day == today) ?? sleep.FirstOrDefault(s => s.Day == today.AddDays(-1)))?.Value;
    var insights = await _insights.ListOwnAsync(person.Id, today.AddDays(-1), today, ct);
    return Playbook.Applicable(new PlaybookInputs(
      today,
      active.SelectMany(m => m.Substances).Distinct(StringComparer.Ordinal).ToList(),
      active.Count(m => m.Kind == MedicationKinds.Medication),
      journal,
      lastNight,
      insights.Select(i => i.Kind).Distinct(StringComparer.Ordinal).ToList()));
  }
}
