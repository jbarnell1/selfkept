using System.Text.RegularExpressions;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// The gate between the local model and the person (docs/assistant-design.md, "Guard"). Red-flag
/// symptoms are answered by code before any model call. A model reply passes only when every
/// number in it is in the brief, a playbook entry or the person's own message; it names no dose
/// unit that the record does not; it makes no diagnosis or cause claim; it names no remedy that
/// is neither on the person's list nor in an applicable playbook entry; and it says nothing from
/// the proposal's "What we will not show" list. Pure, so every rule has a unit test.
/// </summary>
public sealed partial class ReplyGuard : IReplyGuard
{
  public const int MaxReplyLength = 1200;
  public const int MaxLineLength = 240;

  public const string EmergencyReply =
    "This sounds like it could need urgent care, and it is not something I can judge from here. " +
    "If you have chest pain, trouble breathing, have fainted, or notice signs of a stroke such as a drooping face, arm weakness or slurred speech, " +
    "call your local emergency number now or have someone take you to an emergency department. " +
    "If it has passed and you are unsure, contact a doctor or an urgent care service today.";

  public const string CrisisReply =
    "I'm really sorry you're going through this, and I'm glad you said it. You deserve support from a person right now. " +
    "If you might act on these thoughts or you are in danger, call your local emergency number or go to an emergency department. " +
    "You can also talk to a crisis line: in the US and Canada call or text 988, and in the UK and Ireland Samaritans answer on 116 123. " +
    "If you can, tell someone near you how you are feeling.";

  private static readonly (Regex Pattern, bool Crisis)[] RedFlags =
  [
    (ChestPain(), false),
    (Breathing(), false),
    (Fainting(), false),
    (Stroke(), false),
    (Bleeding(), false),
    (Seizure(), false),
    (Anaphylaxis(), false),
    (Overdose(), false),
    (SelfHarm(), true),
  ];

  /// <summary>Remedy names the model may only use when the brief, a playbook entry or the person's message names them.</summary>
  private static readonly string[] Remedies =
  [
    "zinc", "vitamin c", "vitamin d", "vitamin b12", "b12", "melatonin", "magnesium", "ashwagandha", "echinacea", "elderberry",
    "ibuprofen", "paracetamol", "acetaminophen", "aspirin", "naproxen", "antihistamine", "decongestant", "cough syrup", "lozenge",
    "iron", "creatine", "fish oil", "omega-3", "omega 3", "probiotic", "turmeric", "curcumin", "cbd", "valerian", "theanine",
    "glycine", "garlic", "ginseng", "caffeine pill", "electrolyte", "sleeping pill", "antibiotic", "steroid",
  ];

  public string? RedFlagReply(string userMessage)
  {
    if (string.IsNullOrWhiteSpace(userMessage)) return null;
    var text = Normalise(userMessage).ToLowerInvariant();
    var emergency = false;
    foreach (var (pattern, crisis) in RedFlags)
    {
      foreach (Match m in pattern.Matches(text))
      {
        if (Negated(text, m.Index)) continue;
        if (crisis) return CrisisReply;
        emergency = true;
      }
    }
    return emergency ? EmergencyReply : null;
  }

  public GuardVerdict CheckSentence(string sentence, GuardContext context)
  {
    var text = Sanitise(sentence);
    if (text.Length == 0) return GuardVerdict.Pass(text);
    var reason = RuleBreak(text, context);
    return reason == null ? GuardVerdict.Pass(text) : GuardVerdict.Fail(reason);
  }

  public GuardVerdict CheckReply(string reply, GuardContext context)
  {
    var text = Sanitise(reply);
    if (text.Length == 0) return GuardVerdict.Fail("empty");
    if (text.Length > MaxReplyLength) return GuardVerdict.Fail("too long");
    var reason = RuleBreak(text, context);
    return reason == null ? GuardVerdict.Pass(text) : GuardVerdict.Fail(reason);
  }

  public GuardVerdict CheckLine(string line, GuardContext context)
  {
    var text = Sanitise(line);
    if (text.Length == 0) return GuardVerdict.Fail("empty");
    if (text.Length > MaxLineLength) return GuardVerdict.Fail("too long");
    if (Promise().IsMatch(text)) return GuardVerdict.Fail("promise");
    if (text.Contains('?')) return GuardVerdict.Fail("question");
    var reason = RuleBreak(text, context);
    return reason == null ? GuardVerdict.Pass(text) : GuardVerdict.Fail(reason);
  }

  /// <summary>The first rule the text breaks, or null.</summary>
  public static string? RuleBreak(string text, GuardContext context)
  {
    var lower = text.ToLowerInvariant();
    var allowedLower = context.AllowedText.ToLowerInvariant();

    foreach (var number in GuardNumbers.Of(text))
    {
      if (!context.Numbers.Contains(number)) return $"number {number} not in the record";
    }

    foreach (Match m in DoseUnit().Matches(lower))
    {
      var unit = UnitKey(m.Groups["unit"].Value);
      var allowed = DoseUnit().Matches(allowedLower).Any(a => UnitKey(a.Groups["unit"].Value) == unit);
      if (!allowed) return $"dose unit {unit} not in the record";
    }

    if (ClaimBreak(lower) is { } claim) return claim;

    if (BodyScaleScores().IsMatch(lower)) return "scale score";
    if (Calories().IsMatch(lower)) return "calorie or macro count";
    if (StressScore().IsMatch(lower)) return "stress score";
    if (DrugHints().IsMatch(lower)) return "drug hint";
    if (OxygenReassurance().IsMatch(lower)) return "oxygen reassurance";
    if (PredictedLab().IsMatch(lower)) return "predicted lab value";
    if (Gamification().IsMatch(lower)) return "score or streak";
    if (NasalZinc().IsMatch(lower)) return "nasal zinc";

    foreach (var remedy in Remedies)
    {
      if (ContainsWord(lower, remedy) && !ContainsWord(allowedLower, remedy)) return $"remedy {remedy} not in the playbook or the record";
    }
    return null;
  }

  /// <summary>
  /// The diagnosis, condition, detection and cause rules alone, on lowercased text: what a photo
  /// description must also pass (docs/photo-design.md). Null when none is broken.
  /// </summary>
  public static string? ClaimBreak(string lower)
  {
    if (Diagnostic().IsMatch(lower)) return "diagnostic language";
    if (YouHaveCondition().IsMatch(lower)) return "names a condition";
    if (YouAreCondition().IsMatch(lower)) return "names a condition";
    if (Detected().IsMatch(lower)) return "detection claim";
    if (CauseClaim().IsMatch(lower)) return "cause claim";
    return null;
  }

  /// <summary>Plain hyphens and commas only: an em or en dash from the model is rewritten, not refused.</summary>
  public static string Sanitise(string? text)
  {
    if (string.IsNullOrWhiteSpace(text)) return string.Empty;
    var t = Normalise(text);
    t = SpacedDash().Replace(t, ", ");
    t = t.Replace('\u2013', '-').Replace("\u2014", ", ");
    return MultiSpace().Replace(t, " ").Trim();
  }

  private static string Normalise(string text) =>
    text.Replace('\u2019', '\'').Replace('\u2018', '\'').Replace('\u201C', '"').Replace('\u201D', '"');

  private static bool Negated(string text, int index)
  {
    var start = Math.Max(0, index - 16);
    var before = text[start..index];
    return NegationBefore().IsMatch(before);
  }

  private static bool ContainsWord(string haystack, string word) =>
    Regex.IsMatch(haystack, @"(?<![a-z0-9])" + Regex.Escape(word) + @"s?(?![a-z0-9])", RegexOptions.CultureInvariant);

  private static string UnitKey(string unit) => unit switch
  {
    "milligram" or "milligrams" => "mg",
    "microgram" or "micrograms" or "\u00b5g" or "ug" => "mcg",
    "gram" or "grams" => "g",
    "unit" or "units" => "iu",
    "tablet" or "tablets" or "pill" or "pills" or "capsule" or "capsules" => "tablet",
    "drop" or "drops" => "drop",
    _ => unit,
  };

  // Red flags. Matched on lowercased text.
  [GeneratedRegex(@"\bchest\s+(?:pain|pains|tightness|pressure|hurts|is\s+tight)|\b(?:pain|tightness|pressure)\s+in\s+(?:my|the)\s+chest|\bcrushing\s+(?:chest|feeling\s+in\s+my\s+chest)|\bheart\s+attack", RegexOptions.CultureInvariant)]
  private static partial Regex ChestPain();

  [GeneratedRegex(@"\b(?:can'?t|cannot|can\s+not|couldn'?t|struggling\s+to|hard\s+to|trouble|difficulty|unable\s+to)\s+(?:breathe|breathing|catch\s+my\s+breath)|\bshort(?:ness)?\s+of\s+breath|\bbreathless|\bgasping\s+for\s+(?:air|breath)|\blips\s+(?:are\s+|went\s+)?(?:blue|grey|gray)", RegexOptions.CultureInvariant)]
  private static partial Regex Breathing();

  [GeneratedRegex(@"\bfaint(?:ed|ing)\b|\bpass(?:ed)?\s+out\b|\bblack(?:ed)?\s+out\b|\blost\s+consciousness|\bcollapsed\b", RegexOptions.CultureInvariant)]
  private static partial Regex Fainting();

  [GeneratedRegex(@"\b(?:face|mouth)\s+(?:is\s+|has\s+|went\s+)?(?:droop|drooping|dropped|numb)|\bdroop(?:ing|y)\s+(?:face|mouth|eye)|\bslurred\s+speech|\bslurring\s+my\s+words|\bcan'?t\s+(?:speak|talk|lift\s+my\s+arm)|\b(?:arm|leg|side\s+of\s+my\s+face)\s+(?:is\s+|went\s+|feels\s+|has\s+gone\s+)?(?:numb|weak|limp)|\bnumb(?:ness)?\s+(?:on|down)\s+one\s+side|\bsudden(?:ly)?\s+(?:can'?t\s+see|lost\s+my\s+vision|confused|blind)|\bworst\s+headache", RegexOptions.CultureInvariant)]
  private static partial Regex Stroke();

  [GeneratedRegex(@"\bcough(?:ing|ed)?\s+(?:up\s+)?blood|\bvomit(?:ing|ed)?\s+blood|\bthrow(?:ing|n)?\s+up\s+blood|\bblood\s+in\s+(?:my\s+)?(?:vomit|stool|poo)|\bblack\s+(?:stool|poo)|\bbleeding\s+(?:heavily|a\s+lot|won'?t\s+stop|that\s+won'?t\s+stop)|\bsevere\s+bleeding", RegexOptions.CultureInvariant)]
  private static partial Regex Bleeding();

  [GeneratedRegex(@"\bseizure|\bconvuls", RegexOptions.CultureInvariant)]
  private static partial Regex Seizure();

  [GeneratedRegex(@"\b(?:throat|tongue|lips|face)\s+(?:is\s+|are\s+|feels\s+)?(?:swelling|swollen|closing)|\banaphyla", RegexOptions.CultureInvariant)]
  private static partial Regex Anaphylaxis();

  [GeneratedRegex(@"\boverdos|\b(?:took|taken|swallowed)\s+too\s+many", RegexOptions.CultureInvariant)]
  private static partial Regex Overdose();

  [GeneratedRegex(@"\bsuicid|\bkill(?:ing)?\s+myself|\bend\s+(?:my\s+life|it\s+all)|\btake\s+my\s+(?:own\s+)?life|\bwant(?:ed)?\s+to\s+die|\bdon'?t\s+want\s+to\s+(?:live|be\s+alive|be\s+here\s+any\s*more|wake\s+up)|\bbetter\s+off\s+dead|\bself[\s-]?harm|\bhurt(?:ing)?\s+myself|\bcut(?:ting)?\s+myself", RegexOptions.CultureInvariant)]
  private static partial Regex SelfHarm();

  [GeneratedRegex(@"(?:\bno|\bnot|\bwithout|\bnever|n't\s+have|n't\s+had|\bdenies)\s+(?:\w+\s+)?$", RegexOptions.CultureInvariant)]
  private static partial Regex NegationBefore();

  // Reply rules. Matched on lowercased text.
  [GeneratedRegex(@"(?<![a-z])(?<amount>\d+(?:[.,]\d+)?)\s*(?<unit>mg|mcg|\u00b5g|ug|g|iu|ml|units?|tablets?|capsules?|pills?|drops?|milligrams?|micrograms?|grams?)(?![a-z])", RegexOptions.CultureInvariant)]
  private static partial Regex DoseUnit();

  [GeneratedRegex(@"\bdiagnos\w*", RegexOptions.CultureInvariant)]
  private static partial Regex Diagnostic();

  [GeneratedRegex(@"\byou(?:'ve)?\s+(?:may\s+|might\s+|probably\s+|likely\s+|definitely\s+|clearly\s+)?(?:have|got|caught)\s+(?:a\s+|an\s+|the\s+|some\s+)?(?:\w+\s+){0,2}?(?:\w*(?:itis|osis|emia|aemia|pathy|algia)\b|disease|disorder|syndrome|infection|virus|flu|covid|cold|fever|apnea|apnoea|anemia|anaemia|hypertension|arrhythmia|depression|anxiety|diabetes|illness|condition|cancer|tumou?r|deficiency)\b", RegexOptions.CultureInvariant)]
  private static partial Regex YouHaveCondition();

  [GeneratedRegex(@"\byou(?:'re|\s+are)\s+(?:probably\s+|likely\s+|definitely\s+|clearly\s+)?(?:anaemic|anemic|diabetic|prediabetic|pre-diabetic|depressed|hypertensive|hypothyroid|hyperthyroid|deficient|infected|sick|ill|coming\s+down\s+with)\b|\bthis\s+(?:is|means|confirms)\s+(?:a\s+|an\s+)?(?:cold|flu|covid|infection|virus|disease|condition)\b|\bsign\s+of\s+(?:a\s+|an\s+)?(?:disease|disorder|condition|infection|deficiency)", RegexOptions.CultureInvariant)]
  private static partial Regex YouAreCondition();

  [GeneratedRegex(@"\b(?:infection|illness|covid|virus|cold|flu)\s+(?:is\s+|was\s+|has\s+been\s+)?detected\b", RegexOptions.CultureInvariant)]
  private static partial Regex Detected();

  [GeneratedRegex(@"\bcaused\s+by\b|\b(?:is|are|was|were)\s+causing\b|\bcauses\s+your\b|\b(?:is|are|was)\s+(?:damaging|harming|ruining|hurting\s+your)\b|\bthat(?:'s|\s+is)\s+why\s+your\b|\bthe\s+reason\s+(?:for\s+)?your\b|\bis\s+the\s+reason\b|\bled\s+to\s+your\b|\b(?:because\s+of|due\s+to)\s+(?:the\s+|your\s+|that\s+)?(?:alcohol|caffeine|drinks?|drinking|coffee|beer|wine|stress|training|workout|food|diet|meal|medication|medicine|supplement|sugar)\b", RegexOptions.CultureInvariant)]
  private static partial Regex CauseClaim();

  [GeneratedRegex(@"\bmetabolic\s+age|\bvisceral\s+fat|\bmuscle\s+quality|\bbody\s+age\b", RegexOptions.CultureInvariant)]
  private static partial Regex BodyScaleScores();

  [GeneratedRegex(@"\bcalori|\bkcal\b|\bkilocalori|\bmacros?\b|\bmacronutrient", RegexOptions.CultureInvariant)]
  private static partial Regex Calories();

  [GeneratedRegex(@"\bstress\s+(?:score|level\s+(?:is|was|of))", RegexOptions.CultureInvariant)]
  private static partial Regex StressScore();

  [GeneratedRegex(@"\bffmi\b|\bnatural\s+limit|\bsteroids?\b|\bpeds?\b|\banabolic", RegexOptions.CultureInvariant)]
  private static partial Regex DrugHints();

  [GeneratedRegex(@"\b(?:oxygen|spo2|saturation)\w*\s+(?:levels?\s+)?(?:is|are|was|were|looks?|seems?)\s+(?:fine|normal|good|healthy|great|ok|okay|reassuring)|\bapno?ea\s+(?:score|risk)|\b(?:don'?t|do\s+not)\s+have\s+(?:sleep\s+)?apno?ea", RegexOptions.CultureInvariant)]
  private static partial Regex OxygenReassurance();

  [GeneratedRegex(@"\byour\s+(?:ldl|hdl|hba1c|a1c|cholesterol|triglycerides|ferritin|vitamin\s+d|testosterone|glucose|crp|hs-crp|apob|tsh|iron|b12)\b[^.]{0,40}?\b(?:should|will|would)\s+(?:now\s+|soon\s+|probably\s+)?(?:be|drop|fall|rise|go|reach|come|end\s+up)\b", RegexOptions.CultureInvariant)]
  private static partial Regex PredictedLab();

  [GeneratedRegex(@"\bstreaks?\b|\bbadges?\b|\bscore\s+(?:of|out\s+of)\b|\bout\s+of\s+100\b|/100\b", RegexOptions.CultureInvariant)]
  private static partial Regex Gamification();

  [GeneratedRegex(@"\b(?:nasal|intranasal)\s+zinc|\bzinc\s+nasal", RegexOptions.CultureInvariant)]
  private static partial Regex NasalZinc();

  [GeneratedRegex(@"(?:\b(?:will|'ll)\s+(?:definitely\s+|surely\s+|certainly\s+)?(?:show|improve|boost|raise|lower|fix|cure|help|make|be\s+(?:better|higher|lower|up|down|fine))\b)|\bguarantee|\bdefinitely\b|\bfor\s+sure\b|\bsure\s+to\b|\bbound\s+to\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex Promise();

  [GeneratedRegex(@"\s+[\u2014\u2013]\s+")]
  private static partial Regex SpacedDash();

  [GeneratedRegex(@"[ \t]{2,}")]
  private static partial Regex MultiSpace();
}
