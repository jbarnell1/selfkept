using System.Globalization;
using System.Text.RegularExpressions;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// Turns statements in what a person says into day log drafts, by code (docs/assistant-design.md,
/// "Capture"). A sentence is captured only when it reads as something that happened: questions,
/// plans, wishes, conditionals and sentences about someone else are left alone, and a negated
/// item ("no beer") is not logged. Each sentence gives at most one entry per kind, with coarse
/// tags from <see cref="JournalTags"/> and a portion only when one is said. The person's own words
/// are kept as the entry text, minus any calorie or gram count: the day log never holds amounts.
/// "yesterday" and "last night" move the entry to the day before, except for sleep, which is
/// credited to the day the night ended, like the sleep readings. Durable facts ("I'm vegetarian",
/// "I work nights") are returned separately for ai_memory, from a short and conservative list.
/// </summary>
public sealed partial class JournalCapture : IJournalCapture
{
  public const int MaxTextLength = 500;

  private sealed record Lexeme(Regex Pattern, string Kind, string? Tag, bool SelfCue = false, bool NegationSafe = false);

  private sealed record Hit(int Index, int Length, Lexeme Lexeme);

  private static readonly string[] KindOrder = [JournalKinds.Food, JournalKinds.Drink, JournalKinds.Training, JournalKinds.Symptom, JournalKinds.Sleep];

  private static Lexeme L(string kind, string? tag, string alternatives, bool selfCue = false, bool negationSafe = false) =>
    new(new Regex(@"(?<![a-z])(?:" + alternatives + @")(?![a-z])", RegexOptions.Compiled | RegexOptions.CultureInvariant), kind, tag, selfCue, negationSafe);

  private static readonly Lexeme[] Lexicon =
  [
    // Food. Generic meal words make an entry without a tag.
    L(JournalKinds.Food, JournalTags.SkippedMeal, @"skipped (?:breakfast|lunch|dinner|supper|a meal|meals|tea)|missed (?:breakfast|lunch|dinner)|didn'?t (?:eat|have) (?:breakfast|lunch|dinner|anything)|did not (?:eat|have) (?:breakfast|lunch|dinner|anything)|no (?:breakfast|lunch|dinner)|barely ate|hardly ate|didn'?t eat much", selfCue: true, negationSafe: true),
    L(JournalKinds.Food, JournalTags.LateMeal, @"late dinner|late meal|ate late|eating late|midnight snack|late[- ]night snack", selfCue: true),
    L(JournalKinds.Food, JournalTags.HighProtein, @"chicken|beef|steak|pork|lamb|turkey|fish|salmon|tuna|cod|mackerel|prawns?|shrimp|eggs?|omelett?e|tofu|tempeh|lentils|beans|chickpeas|(?:greek )?yog(?:h)?urt|cottage cheese|protein (?:shake|bar|smoothie)|whey|skyr"),
    L(JournalKinds.Food, JournalTags.Vegetables, @"salads?|veg|veggies|vegetables|greens|broccoli|spinach|kale|carrots?|peppers?|courgettes?|zucchini|peas|cauliflower|asparagus|green beans|sprouts|tomatoes|cucumber|stir[- ]fry"),
    L(JournalKinds.Food, JournalTags.Fruit, @"fruit|apples?|bananas?|berries|blueberries|strawberries|raspberries|oranges?|grapes|mango|pears?|kiwi|melon|pineapple"),
    L(JournalKinds.Food, JournalTags.WholeGrains, @"oats|oatmeal|porridge|brown rice|quinoa|whole ?grains?|whole[- ]?wheat|wholemeal|granola|rye bread|bran"),
    L(JournalKinds.Food, JournalTags.Sweets, @"cakes?|chocolate|sweets|candy|biscuits?|cookies?|ice cream|desserts?|pudding|doughnuts?|donuts?|pastry|pastries|croissants?|brownies?|muffins?"),
    L(JournalKinds.Food, JournalTags.FriedFood, @"fries|chips|crisps|fried|deep[- ]fried|fish and chips|tempura|nuggets|onion rings"),
    L(JournalKinds.Food, JournalTags.Takeaway, @"takeaway|take[- ]?out|takeaways|delivery|deliveroo|uber eats|just eat|mcdonald'?s|kfc|fast food|drive[- ]thru|burger king"),
    L(JournalKinds.Food, JournalTags.HomeCooked, @"home[- ]?cooked|home[- ]?made|cooked at home|cooked (?:dinner|lunch|breakfast)|made (?:dinner|lunch|breakfast)"),
    L(JournalKinds.Food, JournalTags.Swap, @"instead of|swapped|switched (?:to|from)|swap(?:ped)? (?:the|my)"),
    L(JournalKinds.Food, null, @"breakfast|brunch|lunch|dinner|supper|snacks?|meal|pizza|pasta|sandwich|wrap|toast|bagel|rice|curry|soup|noodles|burrito|burgers?|sushi|cereal|bread|cheese|food|roast|stew|tacos?|kebab"),

    // Drinks.
    L(JournalKinds.Drink, JournalTags.Alcohol, @"beers?|pints?|lager|ales?|ipa|stout|wine|prosecco|champagne|cava|cocktails?|gin|vodka|rum|whisk(?:e)?y|bourbon|tequila|cider|spritz|margaritas?|(?:a|some|few|couple of|two|three|four|five) drinks|went (?:out )?drinking|drinks with|hungover|hangover|booze"),
    L(JournalKinds.Drink, JournalTags.LateCaffeine, @"(?:afternoon|evening|late|night|after dinner) (?:coffee|espresso|tea|latte|cappuccino)|(?:coffee|espresso|tea|latte) (?:after (?:lunch|dinner|3|4|5|6)|in the (?:afternoon|evening)|at night|late)", selfCue: true),
    L(JournalKinds.Drink, JournalTags.Caffeine, @"coffees?|espressos?|lattes?|cappuccinos?|americanos?|flat whites?|mochas?|macchiatos?|cold brew|matcha|energy drinks?|red bull|monster|pre[- ]?workout|diet coke|coke zero|pepsi max|(?<!herbal |peppermint |chamomile |camomile |mint |rooibos |fruit |iced |for |at )teas?"),
    L(JournalKinds.Drink, JournalTags.SugaryDrink, @"soda|fizzy drinks?|(?<!diet )coke(?! zero)|(?<!diet )cola|(?<!diet )pepsi(?! max)|lemonade|juice|milkshake|frappuccino|sweet tea|slushie"),
    L(JournalKinds.Drink, JournalTags.Water, @"water"),

    // Training.
    L(JournalKinds.Training, JournalTags.RestDay, @"rest day|day off (?:training|the gym|from (?:the gym|training))|took (?:the|a) day off|recovery day|didn'?t (?:train|lift|work out|go to the gym|exercise)|did not (?:train|lift|work out|exercise)|no (?:training|workout|gym|exercise)", selfCue: true, negationSafe: true),
    L(JournalKinds.Training, JournalTags.Lifted, @"lifted|lifting|weights|weight training|strength (?:training|session|workout)|gym session|leg day|legs day|upper body|lower body|push day|pull day|squats?|squatted|deadlifts?|deadlifted|bench(?:ed)?(?: press)?|overhead press|pull[- ]?ups|push[- ]?ups|resistance training|did legs|hit the gym|went to the gym|gym"),
    L(JournalKinds.Training, JournalTags.Cardio, @"ran(?! out| late| into| over)|run(?! out| late| into)|running(?! out| late| into| low)|jog(?:ged|ging)?|cycled|cycling|bike ride|rode|spin(?: class)?|swim|swam|swimming|rowing|rowed|hiit|cardio|elliptical|treadmill|football|tennis|squash|climbing|bouldering|5k|10k|parkrun"),
    L(JournalKinds.Training, JournalTags.Walk, @"walk(?:ed)?|walking|hike|hiked|hiking|stroll"),
    L(JournalKinds.Training, JournalTags.HardSession, @"hard (?:session|workout|run|ride|day at the gym|training)|tough (?:session|workout|run)|brutal|intense|killer|smashed it|personal best|new pr|pb|maxed out|went hard|exhausting (?:session|workout)"),

    // Symptoms.
    L(JournalKinds.Symptom, JournalTags.Headache, @"headaches?|migraines?|head (?:hurts|is pounding|is killing me|aches)|pounding head"),
    L(JournalKinds.Symptom, JournalTags.SoreThroat, @"sore throat|scratchy throat|throat(?:'s| is| feels)? (?:a bit |really |very )?(?:sore|scratchy|raw|tickly|hurting)"),
    L(JournalKinds.Symptom, JournalTags.Congestion, @"congest(?:ed|ion)|stuffy(?: nose)?|blocked nose|runny nose|sniffles|sniffly|bunged up|sinuses"),
    L(JournalKinds.Symptom, JournalTags.Cough, @"cough(?:ing|s)?"),
    L(JournalKinds.Symptom, JournalTags.Fever, @"fever(?:ish)?|running a temp(?:erature)?|high temperature|chills|shivery"),
    L(JournalKinds.Symptom, JournalTags.Fatigue, @"tired|exhausted|fatigue(?:d)?|knackered|wiped(?: out)?|drained|worn out|no energy|low energy|sleepy|lethargic|shattered"),
    L(JournalKinds.Symptom, JournalTags.Nausea, @"nausea|nauseous|nauseated|queasy|felt sick|feel sick|feeling sick"),
    L(JournalKinds.Symptom, JournalTags.StomachUpset, @"stomach (?:ache|upset|cramps|bug|pain|issues)|upset stomach|tummy ache|bloated|bloating|diarrh(?:o)?ea|indigestion|heartburn|reflux"),
    L(JournalKinds.Symptom, JournalTags.Soreness, @"doms|achy|aching|stiff|sore"),
    L(JournalKinds.Symptom, JournalTags.Pain, @"pain|painful|hurts|hurt my|injured|injury|pulled (?:a|my) \w+|tweaked (?:my )?\w+|twinge"),
    L(JournalKinds.Symptom, JournalTags.Stress, @"stressed|stress|stressful|anxious|anxiety|overwhelmed|on edge|wound up|tense"),
    L(JournalKinds.Symptom, JournalTags.LowMood, @"low mood|feeling (?:low|down|flat|blue|sad|rubbish|crap)|felt (?:low|down|flat|blue|sad)|sad|miserable|down in the dumps"),

    // Sleep.
    L(JournalKinds.Sleep, JournalTags.PoorSleep, @"slept (?:badly|poorly|terribly|awfully|horribly|bad|terrible|awful|rubbish|like rubbish)|bad (?:night|sleep)|terrible (?:night|sleep)|awful (?:night|sleep)|rough night|poor sleep|didn'?t sleep(?: well| much)?|couldn'?t sleep|could not sleep|insomnia|tossed and turned|woke up (?:a lot|loads|several times|lots|constantly|repeatedly|multiple times|again and again)|kept waking|restless night|barely slept|hardly slept", negationSafe: true),
    L(JournalKinds.Sleep, JournalTags.GoodSleep, @"slept (?:well|great|brilliantly|like a log|like a baby|soundly|really well|so well|amazingly)|good (?:night|sleep|night'?s sleep)|great (?:night|sleep|night'?s sleep)|solid sleep|well rested"),
    L(JournalKinds.Sleep, JournalTags.Nap, @"nap(?:ped|ping|s)?|power nap|dozed off"),
    L(JournalKinds.Sleep, JournalTags.LateNight, @"late night|stayed up(?: late)?|went to bed late|bed (?:late|after midnight)|up (?:late|till|until) (?:\d|midnight)|night out"),
  ];

  public CaptureResult Extract(string message, DateTime nowUtc, TimeZoneInfo zone, string? topic = null)
  {
    if (string.IsNullOrWhiteSpace(message)) return CaptureResult.Empty;
    var drafts = new List<CaptureDraft>();
    var facts = new List<MemoryFact>();
    var now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);

    foreach (var sentence in Sentences(Normalise(message)))
    {
      var lower = sentence.ToLowerInvariant();
      if (IsQuestion(lower) || IsHypothetical(lower)) continue;

      foreach (var fact in Facts(lower))
      {
        if (facts.All(f => f.Key != fact.Key)) facts.Add(fact);
      }
      if (IsAboutSomeoneElse(lower)) continue;

      var hits = Hits(lower, topic);
      if (hits.Count == 0) continue;

      var byKind = hits.GroupBy(h => h.Lexeme.Kind).ToDictionary(g => g.Key, g => g.ToList());
      var tagsByKind = new Dictionary<string, List<string>>();
      foreach (var kind in KindOrder)
      {
        if (!byKind.TryGetValue(kind, out var kindHits)) continue;
        if (!Gated(kind, lower, kindHits, byKind, topic)) continue;
        var tags = kindHits.Where(h => h.Lexeme.Tag != null).Select(h => h.Lexeme.Tag!).Distinct().ToList();
        // hard_session needs a training word beside it: "a brutal day at work" is not a session.
        if (tags.Contains(JournalTags.HardSession) && tags.Count == 1 && topic != CheckinTopics.Training && !kindHits.Any(h => h.Lexeme.Tag == null)) continue;
        if (tags.Contains(JournalTags.LateCaffeine) && !tags.Contains(JournalTags.Caffeine)) tags.Add(JournalTags.Caffeine);
        tagsByKind[kind] = tags;
      }
      if (tagsByKind.Count == 0) continue;

      var text = Clean(sentence);
      var primary = KindOrder.First(tagsByKind.ContainsKey);
      foreach (var kind in KindOrder)
      {
        if (!tagsByKind.TryGetValue(kind, out var tags)) continue;
        var isPrimary = kind == primary;
        if (!isPrimary && tags.Count == 0) continue;
        if (isPrimary && tags.Count == 0 && text == null) continue;

        var at = When(lower, kind, now, zone);
        if (kind == JournalKinds.Drink && tags.Contains(JournalTags.Caffeine) && !tags.Contains(JournalTags.LateCaffeine) && LocalHour(at, zone) >= 15)
        {
          tags.Add(JournalTags.LateCaffeine);
        }
        var portion = kind is JournalKinds.Food or JournalKinds.Drink ? Portion(lower) : null;
        drafts.Add(new CaptureDraft(kind, isPrimary ? text : null, tags.Take(8).ToList(), portion, at));
      }
    }
    return new CaptureResult(drafts, facts);
  }

  // --- sentences ---------------------------------------------------------------------------------

  private static string Normalise(string text) =>
    MultiSpace().Replace(text.Replace('’', '\'').Replace('‘', '\'').Replace('—', ',').Replace('–', '-'), " ").Trim();

  private static IEnumerable<string> Sentences(string text) =>
    SentenceSplit().Split(text).Select(s => s.Trim()).Where(s => s.Length > 0);

  /// <summary>Ends with a question mark, or opens like a question ("how was", "should I", "did I").</summary>
  public static bool IsQuestion(string lower) =>
    lower.EndsWith('?') || WhQuestion().IsMatch(lower) || AuxQuestion().IsMatch(lower) || DidQuestion().IsMatch(lower);

  /// <summary>Plans, wishes, conditionals and advice-seeking: nothing has happened yet.</summary>
  public static bool IsHypothetical(string lower) => Hypothetical().IsMatch(lower);

  private static bool IsAboutSomeoneElse(string lower) => SomeoneElse().IsMatch(lower);

  // --- matching ----------------------------------------------------------------------------------

  private static List<Hit> Hits(string lower, string? topic)
  {
    var all = new List<Hit>();
    foreach (var lexeme in Lexicon)
    {
      foreach (Match m in lexeme.Pattern.Matches(lower)) all.Add(new Hit(m.Index, m.Length, lexeme));
    }
    // Longer matches win where two overlap: "sore throat" is not also "sore".
    var kept = new List<Hit>();
    foreach (var hit in all.OrderByDescending(h => h.Length).ThenBy(h => h.Index))
    {
      if (kept.Any(k => hit.Index < k.Index + k.Length && k.Index < hit.Index + hit.Length)) continue;
      kept.Add(hit);
    }
    return kept
      .Where(h => h.Lexeme.NegationSafe || !Negated(lower, h.Index))
      .OrderBy(h => h.Index)
      .ToList();
  }

  /// <summary>A negation earlier in the same clause, not reset by a later "had" ("no beer", "didn't have any coffee").</summary>
  private static bool Negated(string lower, int index)
  {
    var before = lower[..index];
    var clauseStart = Math.Max(before.LastIndexOfAny([',', ';', ':']), before.LastIndexOf(" but ", StringComparison.Ordinal));
    var clause = clauseStart >= 0 ? before[(clauseStart + 1)..] : before;
    var negation = NegationWord().Matches(clause).LastOrDefault();
    if (negation == null) return false;
    var after = clause[(negation.Index + negation.Length)..];
    return !ConsumptionVerb().IsMatch(after);
  }

  private static bool Gated(string kind, string lower, List<Hit> hits, Dictionary<string, List<Hit>> byKind, string? topic)
  {
    if (hits.Any(h => h.Lexeme.SelfCue)) return true;
    switch (kind)
    {
      case JournalKinds.Food:
        return topic == CheckinTopics.Eating || EatCue().IsMatch(lower);
      case JournalKinds.Drink:
        if (topic == CheckinTopics.Drinks || DrinkCue().IsMatch(lower) || WordCount(lower) <= 4) return true;
        return hits.Any(h => QuantityBefore().IsMatch(lower[..h.Index]));
      case JournalKinds.Training:
        return topic == CheckinTopics.Training || TrainingCue().IsMatch(lower);
      case JournalKinds.Symptom:
        return true;
      case JournalKinds.Sleep:
        return true;
      default:
        return false;
    }
  }

  private static int WordCount(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

  private static string? Portion(string lower)
  {
    if (LargePortion().IsMatch(lower)) return Portions.Large;
    if (SmallPortion().IsMatch(lower)) return Portions.Small;
    if (UsualPortion().IsMatch(lower)) return Portions.Usual;
    return null;
  }

  /// <summary>The person's words for the entry, without calorie or gram counts, at most 500 characters.</summary>
  public static string? Clean(string sentence)
  {
    var text = CalorieCount().Replace(sentence, string.Empty);
    text = MacroCount().Replace(text, string.Empty);
    text = MultiSpace().Replace(text, " ").Trim().Trim(',', ';', ' ');
    text = SpaceBeforePunctuation().Replace(text, "$1");
    if (text.Length == 0) return null;
    return text.Length > MaxTextLength ? text[..MaxTextLength] : text;
  }

  // --- time --------------------------------------------------------------------------------------

  private static DateTime When(string lower, string kind, DateTime now, TimeZoneInfo zone)
  {
    var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, zone);
    var today = DateOnly.FromDateTime(localNow);
    var yesterday = Yesterday().IsMatch(lower);
    // A night is credited to the day it ended, like the sleep readings: "slept badly last night"
    // belongs to today. A nap "yesterday" still moves.
    var lastNight = LastNight().IsMatch(lower) && kind != JournalKinds.Sleep;
    var shift = yesterday || lastNight;
    var day = shift ? today.AddDays(-1) : today;
    if (kind == JournalKinds.Sleep && !shift) return now;

    TimeOnly? time = kind == JournalKinds.Sleep ? null : ClockTime(lower);
    time ??= lastNight ? new TimeOnly(21, 0)
      : BreakfastTime().IsMatch(lower) ? new TimeOnly(8, 0)
      : LunchTime().IsMatch(lower) ? new TimeOnly(12, 30)
      : AfternoonTime().IsMatch(lower) ? new TimeOnly(15, 0)
      : EveningTime().IsMatch(lower) ? new TimeOnly(19, 0)
      : null;

    if (time == null && day == today) return now;
    var local = day.ToDateTime(time ?? new TimeOnly(12, 0), DateTimeKind.Unspecified);
    DateTime utc;
    try
    {
      utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
    catch (ArgumentException)
    {
      // A local time skipped by a clock change: an hour later exists.
      utc = TimeZoneInfo.ConvertTimeToUtc(local.AddHours(1), zone);
    }
    return utc > now ? now : utc;
  }

  private static int LocalHour(DateTime utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTimeFromUtc(utc, zone).Hour;

  private static TimeOnly? ClockTime(string lower)
  {
    var m = ClockPattern().Match(lower);
    if (!m.Success) return null;
    var hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
    var minute = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
    var meridiem = m.Groups["ap"].Value;
    if (meridiem == "pm" && hour < 12) hour += 12;
    else if (meridiem == "am" && hour == 12) hour = 0;
    else if (meridiem.Length == 0 && !m.Groups["m"].Success && hour is >= 1 and <= 7) hour += 12;
    if (hour is < 0 or > 23 || minute is < 0 or > 59) return null;
    return new TimeOnly(hour, minute);
  }

  // --- durable facts -----------------------------------------------------------------------------

  private static IEnumerable<MemoryFact> Facts(string lower)
  {
    var diet = DietFact().Match(lower);
    if (diet.Success)
    {
      var word = diet.Groups["diet"].Value == "pescetarian" ? "pescatarian" : diet.Groups["diet"].Value;
      yield return new MemoryFact(AiMemoryTypes.Preference, "diet", "Eats a " + word + " diet");
    }
    if (NoAlcoholFact().IsMatch(lower)) yield return new MemoryFact(AiMemoryTypes.Preference, "alcohol", "Does not drink alcohol");
    if (NightsFact().IsMatch(lower)) yield return new MemoryFact(AiMemoryTypes.LifeEvent, "work_pattern", "Works night shifts");
    else if (ShiftsFact().IsMatch(lower)) yield return new MemoryFact(AiMemoryTypes.LifeEvent, "work_pattern", "Works shifts");
    var intolerance = IntoleranceFact().Match(lower);
    if (intolerance.Success)
    {
      var what = intolerance.Groups["what"].Value;
      var key = what.StartsWith("lactose", StringComparison.Ordinal) ? "lactose_intolerant"
        : what.StartsWith("gluten", StringComparison.Ordinal) ? "gluten_free"
        : "coeliac";
      var value = key switch
      {
        "lactose_intolerant" => "Lactose intolerant",
        "gluten_free" => "Eats gluten free",
        _ => "Coeliac",
      };
      yield return new MemoryFact(AiMemoryTypes.Condition, key, value);
    }
    var allergy = AllergyFact().Match(lower);
    if (allergy.Success)
    {
      var what = allergy.Groups["what"].Value.Trim();
      var slug = NonSlug().Replace(what, "_").Trim('_');
      if (slug.Length is > 0 and <= 40) yield return new MemoryFact(AiMemoryTypes.Condition, "allergy_" + slug, "Allergic to " + what);
    }
  }

  // --- patterns ----------------------------------------------------------------------------------

  [GeneratedRegex(@"(?<=[.!?;])\s+|\n+|\s+-\s+")]
  private static partial Regex SentenceSplit();

  [GeneratedRegex(@"[ \t]{2,}")]
  private static partial Regex MultiSpace();

  [GeneratedRegex(@"\s+([,.;:!?])")]
  private static partial Regex SpaceBeforePunctuation();

  [GeneratedRegex(@"^(?:so |and |but |ok |okay |hey |hi )?(?:what|why|how|when|where|which|who)(?:'s|'re|\s+(?:\w+\s+)?(?:is|are|was|were|do|does|did|should|can|could|would|will|might|may|much|many|long|often|about|come))\b", RegexOptions.CultureInvariant)]
  private static partial Regex WhQuestion();

  [GeneratedRegex(@"^(?:so |and |but |ok |okay |hey |hi )?(?:should|shall|can|could|would|will|may|might|is|are|am|was|were|do|does|isn'?t|aren'?t|won'?t|wouldn'?t|shouldn'?t)\s+(?:i|you|we|it|they|he|she|this|that|there|my|your|the|caffeine|alcohol|coffee)\b", RegexOptions.CultureInvariant)]
  private static partial Regex AuxQuestion();

  [GeneratedRegex(@"^(?:so |and |but |ok |okay )?(?:did|have|has|had)\s+(?:i|you|we|they|he|she|it|my)\b", RegexOptions.CultureInvariant)]
  private static partial Regex DidQuestion();

  [GeneratedRegex(@"\b(?:if|whether|would|should|shouldn'?t|could(?! not)|might|thinking (?:of|about)|planning|plan to|going to|gonna|will|i'll|we'll|tomorrow|want to|wanna|would like|i'd like|hoping to|hope to|considering|supposed to|meant to|need to|have to|ought to|tempted|wondering|wonder)\b", RegexOptions.CultureInvariant)]
  private static partial Regex Hypothetical();

  [GeneratedRegex(@"\b(?:wife|husband|partner|son|daughter|kids?|child|children|baby|friend|mum|mom|dad|mother|father|brother|sister|boss|colleague|dog|cat|he|she|they)(?:'s)?\s+(?:has|had|have|is|was|were|got|feels|felt|ate|drank|ran|lifted|slept)\b", RegexOptions.CultureInvariant)]
  private static partial Regex SomeoneElse();

  [GeneratedRegex(@"\b(?:no|not|never|didn'?t|did not|don'?t|do not|haven'?t|have not|hasn'?t|wasn'?t|without|zero|none|nothing|avoided|cut out)\b", RegexOptions.CultureInvariant)]
  private static partial Regex NegationWord();

  [GeneratedRegex(@"\b(?:had|ate|drank|grabbed|got|ordered)\b", RegexOptions.CultureInvariant)]
  private static partial Regex ConsumptionVerb();

  [GeneratedRegex(@"\b(?:had|have had|ate|eaten|eating|snacked|snacking|grabbed|finished|enjoyed|cooked|made|ordered|got|tried|munched|scoffed)\b|\bfor (?:breakfast|brunch|lunch|dinner|supper|tea|a snack)\b|\b(?:breakfast|brunch|lunch|dinner|supper) (?:was|were|is)\b", RegexOptions.CultureInvariant)]
  private static partial Regex EatCue();

  [GeneratedRegex(@"\b(?:had|have had|drank|drunk|drinking|grabbed|sipped|finished|ordered|got|downed|necked|enjoyed)\b", RegexOptions.CultureInvariant)]
  private static partial Regex DrinkCue();

  [GeneratedRegex(@"(?:\b(?:a|an|one|two|three|four|five|six|couple|few|several|some|another|\d+|glass|glasses|pint|pints|cup|cups|mug|mugs|can|cans|bottle|bottles|shot|shots|large|small|double)\s+(?:of\s+)?(?:\w+\s+)?)$", RegexOptions.CultureInvariant)]
  private static partial Regex QuantityBefore();

  [GeneratedRegex(@"\b(?:did|done|went|hit|had|finished|completed|trained|worked out|smashed|got in|managed|lifted|ran|cycled|swam|walked|hiked|jogged|rowed|squatted|deadlifted|benched|rode|played|today|this morning|earlier|tonight)\b", RegexOptions.CultureInvariant)]
  private static partial Regex TrainingCue();

  [GeneratedRegex(@"\b(?:big|large|huge|massive|giant|loads of|a lot of|lots of|seconds|second helping|extra large|double portion|stuffed)\b", RegexOptions.CultureInvariant)]
  private static partial Regex LargePortion();

  [GeneratedRegex(@"\b(?:small|smaller|light|little|tiny|a bit of|half a|half portion|just a)\b", RegexOptions.CultureInvariant)]
  private static partial Regex SmallPortion();

  [GeneratedRegex(@"\b(?:usual|normal|regular|standard) (?:portion|amount|size|helping|plate|bowl|one)\b|\bthe usual\b", RegexOptions.CultureInvariant)]
  private static partial Regex UsualPortion();

  [GeneratedRegex(@"(?:about |around |roughly |approximately |~)?\d+(?:[.,]\d+)?\s*(?:k?cals?|kilocalories|calories)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex CalorieCount();

  [GeneratedRegex(@"(?:about |around |roughly |~)?\d+(?:[.,]\d+)?\s*(?:g|grams?)\s+(?:of\s+)?(?:protein|carbs?|carbohydrates?|fat|fibre|fiber|sugar)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex MacroCount();

  [GeneratedRegex(@"\byesterday\b", RegexOptions.CultureInvariant)]
  private static partial Regex Yesterday();

  [GeneratedRegex(@"\blast night\b", RegexOptions.CultureInvariant)]
  private static partial Regex LastNight();

  [GeneratedRegex(@"\b(?:this morning|for breakfast|at breakfast)\b", RegexOptions.CultureInvariant)]
  private static partial Regex BreakfastTime();

  [GeneratedRegex(@"\b(?:for lunch|at lunch|lunchtime|at midday|at noon)\b", RegexOptions.CultureInvariant)]
  private static partial Regex LunchTime();

  [GeneratedRegex(@"\bthis afternoon\b|\bin the afternoon\b", RegexOptions.CultureInvariant)]
  private static partial Regex AfternoonTime();

  [GeneratedRegex(@"\b(?:this evening|tonight|for dinner|at dinner|for supper|in the evening)\b", RegexOptions.CultureInvariant)]
  private static partial Regex EveningTime();

  [GeneratedRegex(@"\b(?:at|around|about)\s+(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm)?\b|\b(?<h>\d{1,2}):(?<m>\d{2})\s*(?<ap>am|pm)?\b", RegexOptions.CultureInvariant)]
  private static partial Regex ClockPattern();

  [GeneratedRegex(@"\bi(?:'m| am)\s+(?:a\s+|now\s+|fully\s+)?(?<diet>vegetarian|vegan|pescatarian|pescetarian)\b", RegexOptions.CultureInvariant)]
  private static partial Regex DietFact();

  [GeneratedRegex(@"\bi\s+(?:don'?t|do not|never)\s+drink(?:\s+alcohol|\s+booze)?\s*(?:at all)?(?:[.!,;]|$)", RegexOptions.CultureInvariant)]
  private static partial Regex NoAlcoholFact();

  [GeneratedRegex(@"\bi\s+(?:work|do)\s+(?:the\s+)?(?:night\s+shifts?|nights)\b|\bi(?:'m| am)\s+on\s+(?:the\s+)?night\s+shifts?\b|\bi(?:'m| am)\s+a\s+night\s+shift\s+worker\b", RegexOptions.CultureInvariant)]
  private static partial Regex NightsFact();

  [GeneratedRegex(@"\bi\s+work\s+(?:rotating\s+)?shifts\b|\bi(?:'m| am)\s+a\s+shift\s+worker\b", RegexOptions.CultureInvariant)]
  private static partial Regex ShiftsFact();

  [GeneratedRegex(@"\bi(?:'m| am)\s+(?<what>lactose[- ]intolerant|gluten[- ]free|coeliac|celiac)\b", RegexOptions.CultureInvariant)]
  private static partial Regex IntoleranceFact();

  [GeneratedRegex(@"\bi(?:'m| am)\s+allergic\s+to\s+(?<what>[a-z][a-z ]{1,30}?)(?=[.!,;]|$|\s+and\b|\s+so\b)", RegexOptions.CultureInvariant)]
  private static partial Regex AllergyFact();

  [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
  private static partial Regex NonSlug();
}
