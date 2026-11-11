using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;

namespace SelfKept.Infrastructure.Services.Assistant.Lookup;

/// <summary>Who and what counts as a hard identifier: the household's names and the server's own host name.</summary>
public sealed record LookupScrubContext(IReadOnlyList<string> Names, string? HostName)
{
  public static readonly LookupScrubContext Empty = new([], null);
}

/// <summary>The scrubbed text and what was taken out or changed, by kind.</summary>
public sealed record LookupScrubResult(string Text, IReadOnlyList<LookupChangeDto> Changes);

/// <summary>What the "With my details" mode adds: age as a decade, sex when set, and the brief's fact lines. Never the birth date.</summary>
public sealed record LookupAboutMe(int? Age, SexAtBirth Sex, IReadOnlyList<string> Facts);

/// <summary>
/// Builds the text of a cloud lookup by code (docs/lookup-design.md, "The two modes"). Pure and
/// deterministic, and idempotent: scrubbing its own output changes nothing.
///
/// Always (hard identifiers): the household's names as whole words in any case, email addresses,
/// phone numbers, web addresses and the server's own host name. Anonymized also removes calendar
/// dates, turns a stated age into a decade and rounds every other number to two significant
/// figures. With my details keeps the question as typed (minus hard identifiers) and adds an
/// "About me" block. It does not understand prose: a street address or a rare condition passes,
/// which is why the person always reads the text before it is sent.
/// </summary>
public static partial class LookupScrubber
{
  public const string NameReplacement = "someone";
  public const string EmailReplacement = "(email removed)";
  public const string PhoneReplacement = "(phone number removed)";
  public const string LinkReplacement = "(link removed)";
  public const string DateReplacement = "recently";
  public const string YearReplacement = "a past year";
  public const string AboutMeHeading = "About me:";

  private const string AboutMeSeparator = "\n\n" + AboutMeHeading;

  /// <summary>Hard identifiers, then dates, ages and numbers.</summary>
  public static LookupScrubResult Anonymize(string text, LookupScrubContext context)
  {
    var changes = new Changes();
    var result = HardIdentifiers(Normalize(text), context, changes);
    result = Dates(result, changes);
    result = Ages(result, changes);
    result = Numbers(result, changes);
    return new LookupScrubResult(Tidy(result), changes.ToList());
  }

  /// <summary>
  /// The question minus hard identifiers, then the "About me" block (itself free of hard
  /// identifiers). A text that already ends in an "About me" block gets no second one.
  /// </summary>
  public static LookupScrubResult Detailed(string question, LookupAboutMe? about, LookupScrubContext context)
  {
    var changes = new Changes();
    var text = Normalize(question);
    var hasBlock = text.Contains(AboutMeSeparator, StringComparison.Ordinal);
    var result = Tidy(HardIdentifiers(text, context, changes));
    if (!hasBlock && about != null)
    {
      var block = AboutMeBlock(about);
      if (block != null)
      {
        result += "\n\n" + Tidy(HardIdentifiers(block, context, new Changes()));
        changes.Add(LookupChangeKinds.Record);
      }
    }
    return new LookupScrubResult(result, changes.ToList());
  }

  /// <summary>Only the hard identifiers: what both modes always take out.</summary>
  public static LookupScrubResult RemoveHardIdentifiers(string text, LookupScrubContext context)
  {
    var changes = new Changes();
    var result = HardIdentifiers(Normalize(text), context, changes);
    return new LookupScrubResult(Tidy(result), changes.ToList());
  }

  /// <summary>The hard identifiers still in a text, by kind: the send check. Empty means it may go.</summary>
  public static IReadOnlyList<LookupChangeDto> FindHardIdentifiers(string text, LookupScrubContext context) =>
    RemoveHardIdentifiers(text, context).Changes;

  /// <summary>"About me:" with "- " lines, or null when there is nothing to say.</summary>
  public static string? AboutMeBlock(LookupAboutMe about)
  {
    var lines = new List<string>();
    if (about.Age is { } age && Decade(age, DecadeForm.FirstPerson) is { } decade) lines.Add(Capitalise(decade) + ".");
    if (about.Sex == SexAtBirth.Female) lines.Add("Sex at birth: female.");
    else if (about.Sex == SexAtBirth.Male) lines.Add("Sex at birth: male.");
    lines.AddRange(about.Facts.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()));
    if (lines.Count == 0) return null;
    return AboutMeHeading + "\n" + string.Join("\n", lines.Select(l => "- " + l));
  }

  /// <summary>Age in whole years on a day, or null without a birth date.</summary>
  public static int? AgeOn(DateOnly? birthDate, DateOnly today)
  {
    if (birthDate is not { } born || born > today) return null;
    var age = today.Year - born.Year;
    if (born.AddYears(age) > today) age--;
    return age;
  }

  // ---- hard identifiers ----

  private static string HardIdentifiers(string text, LookupScrubContext context, Changes changes)
  {
    text = Replace(EmailPattern(), text, LookupChangeKinds.Email, EmailReplacement, changes);
    text = Replace(UrlPattern(), text, LookupChangeKinds.Link, LinkReplacement, changes);
    if (HostPattern(context.HostName) is { } host) text = Replace(host, text, LookupChangeKinds.Link, LinkReplacement, changes);
    text = Replace(BareDomainPattern(), text, LookupChangeKinds.Link, LinkReplacement, changes);
    text = PhonePattern().Replace(text, m =>
    {
      var digits = m.Value.Count(char.IsAsciiDigit);
      if (digits < 7 || digits > 15) return m.Value;
      changes.Add(LookupChangeKinds.Phone);
      return PhoneReplacement;
    });
    if (NamesPattern(context.Names) is { } names) text = Replace(names, text, LookupChangeKinds.Name, NameReplacement, changes);
    return text;
  }

  private static string Replace(Regex pattern, string text, string kind, string replacement, Changes changes) =>
    pattern.Replace(text, _ =>
    {
      changes.Add(kind);
      return replacement;
    });

  /// <summary>Every name and every part of a name of two letters or more, longest first, as whole words in any case.</summary>
  private static Regex? NamesPattern(IReadOnlyList<string> names)
  {
    var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var raw in names)
    {
      var name = Normalize(raw ?? string.Empty).Trim();
      if (name.Length == 0) continue;
      if (name.Count(char.IsLetter) >= 2) words.Add(name);
      foreach (var part in name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
      {
        var trimmed = part.Trim('.', ',', '(', ')', '"', '\'');
        if (trimmed.Count(char.IsLetter) >= 2) words.Add(trimmed);
      }
    }
    if (words.Count == 0) return null;
    var alternatives = words.OrderByDescending(w => w.Length).ThenBy(w => w, StringComparer.Ordinal).Select(w => Regex.Escape(w).Replace("\\ ", "\\s+"));
    return new Regex(@"(?<![\p{L}\p{M}\p{N}_])(?:" + string.Join("|", alternatives) + @")(?![\p{L}\p{M}\p{N}_])",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
  }

  /// <summary>The server's own host name, and its first label when that is a word of its own ("mybox" of mybox.tail1234.ts.net).</summary>
  private static Regex? HostPattern(string? hostName)
  {
    var host = hostName?.Trim().TrimEnd('.');
    if (string.IsNullOrEmpty(host) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return null;
    var alternatives = new List<string> { Regex.Escape(host) + @"(?::\d+)?" };
    var first = host.Split('.')[0];
    if (host.Contains('.') && first.Length >= 3 && first.Any(char.IsLetter)) alternatives.Add(Regex.Escape(first));
    return new Regex(@"(?<![\p{L}\p{N}_.-])(?:" + string.Join("|", alternatives) + @")(?![\p{L}\p{N}_-])",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
  }

  [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}-]+(?:\.[\p{L}\p{N}-]+)*\.\p{L}{2,}", RegexOptions.CultureInvariant)]
  private static partial Regex EmailPattern();

  [GeneratedRegex(@"\b(?:(?:https?|ftp)://|www\.)[^\s<>""]*[^\s<>"".,;:!?)\]'’]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex UrlPattern();

  // A bare host such as example.com/page or box.tail1234.ts.net. Only common top-level domains, so
  // a missing space after a full stop ("badly.Then") is not mistaken for a web address.
  [GeneratedRegex(@"(?<![\p{L}\p{N}_@.-])(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:com|org|net|edu|gov|mil|int|info|biz|io|co|ai|app|dev|me|ly|tv|xyz|site|online|health|uk|us|ca|au|nz|ie|de|fr|nl|be|es|it|pt|se|no|dk|fi|ch|at|pl|jp|in|local|lan|home|internal|arpa)(?![\p{L}\p{N}_-])(?:/[^\s<>""]*[^\s<>"".,;:!?)\]'’])?",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex BareDomainPattern();

  // Phone numbers in the usual shapes; the digit count (7 to 15) is checked on each match.
  [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:\+\d[\d\s().-]{6,}\d|\(\d{2,5}\)[\s.-]?\d{3,4}[\s.-]?\d{3,4}|\d{3}[.-]\d{3,4}[.-]\d{4}|\d{3}\s\d{3}\s\d{4}|0\d{2,4}[\s-]\d{3,4}[\s-]?\d{3,4}|\d{10,15})(?![\p{L}\p{N}])",
    RegexOptions.CultureInvariant)]
  private static partial Regex PhonePattern();

  // ---- dates ----

  private const string Month = @"(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?";
  private const string Weekday = @"(?:(?:mon|tues?|wed(?:nes)?|thu(?:rs)?|fri|sat(?:ur)?|sun)(?:day)?\.?,?\s+)";
  private const string Ordinal = @"\d{1,2}(?:st|nd|rd|th)?";

  private static readonly Regex DatePattern = new(
    @"(?<![\p{L}\p{N}])(?:on\s+)?(?:" +
      Weekday + @"?(?:the\s+)?" + Ordinal + @"(?:\s+of)?\s+" + Month + @"(?:,?\s+\d{4})?" + "|" +
      Weekday + @"?" + Month + @"\s+(?:the\s+)?" + Ordinal + @"(?:,?\s+\d{4})?" + "|" +
      Month + @"\s+\d{4}" + "|" +
      @"\d{4}-\d{1,2}-\d{1,2}" + "|" +
      @"\d{1,2}[/.]\d{1,2}[/.](?:\d{4}|\d{2})" +
    @")(?![\p{L}\p{N}])",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

  [GeneratedRegex(@"(?<=\b(?:in|since|from|until|till|before|after|during|around|back\s+in)\s+)(?:19|20)\d{2}(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex YearPattern();

  private static string Dates(string text, Changes changes)
  {
    text = Replace(DatePattern, text, LookupChangeKinds.Date, DateReplacement, changes);
    return Replace(YearPattern(), text, LookupChangeKinds.Date, YearReplacement, changes);
  }

  // ---- ages ----

  private enum DecadeForm
  {
    FirstPerson,
    ThirdPerson,
    Adjective,
  }

  private const string NotAUnit =
    @"(?!\s*(?:['""’]|(?:kg|kgs|kilos?|lbs?|pounds?|stone|st|cm|mm|m|in|inches|ft|feet|%|percent|mg|mcg|g|ml|l|minutes?|mins?|hours?|hrs?|h|days?|weeks?|wks?|months?|steps|bpm|times|x|°)(?![\p{L}])))";

  [GeneratedRegex(@"(?<![\p{L}])(?<lead>I['’]?m|I\s+am)\s+(?<n>\d{1,3})(?<suffix>(?:\s*|-)(?:years?|yrs?)(?:[\s-]+old)?(?![\p{L}])|\s*y/?o(?![\p{L}]))?(?(suffix)|(?!\d|[.,]\d)" + NotAUnit + ")",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex FirstPersonAge();

  [GeneratedRegex(@"(?<![\p{L}\p{N}])(?<n>\d{1,3})[\s-]year[\s-]old(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex AdjectiveAge();

  [GeneratedRegex(@"(?<![\p{L}\p{N}])(?<n>\d{1,3})(?:\s*(?:years|yrs)[\s-]+old|\s*y/?o)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex YearsOldAge();

  [GeneratedRegex(@"(?<![\p{L}])(?:at\s+)?(?:aged|age)\s*:?\s*(?<n>\d{1,3})(?![\p{N}]|[.,]\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex StatedAge();

  private static string Ages(string text, Changes changes)
  {
    text = FirstPersonAge().Replace(text, m => AgeReplacement(m, DecadeForm.FirstPerson, changes, m.Groups["lead"].Value + " "));
    text = AdjectiveAge().Replace(text, m => AgeReplacement(m, DecadeForm.Adjective, changes, string.Empty));
    text = YearsOldAge().Replace(text, m => AgeReplacement(m, DecadeForm.ThirdPerson, changes, string.Empty));
    return StatedAge().Replace(text, m => AgeReplacement(m, DecadeForm.ThirdPerson, changes, string.Empty));
  }

  private static string AgeReplacement(Match m, DecadeForm form, Changes changes, string lead)
  {
    var age = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
    if (Decade(age, form) is not { } decade) return m.Value;
    changes.Add(LookupChangeKinds.Age);
    return lead + decade;
  }

  /// <summary>"in my 40s", "in their 40s" or "40-something"; "under 10" for a child. Null for a number that is not an age.</summary>
  private static string? Decade(int age, DecadeForm form)
  {
    if (age < 0 || age > 120) return null;
    if (age < 10) return form == DecadeForm.Adjective ? "under-10" : "under 10";
    var decade = (age / 10 * 10).ToString(CultureInfo.InvariantCulture);
    return form switch
    {
      DecadeForm.FirstPerson => $"in my {decade}s",
      DecadeForm.Adjective => $"{decade}-something",
      _ => $"in their {decade}s",
    };
  }

  // ---- numbers ----

  [GeneratedRegex(@"(?<![\p{L}\p{N}.,_])(?<int>\d{1,3}(?:,\d{3})+|\d+)(?:\.(?<frac>\d+))?(?![\p{N}]|[.,]\d)", RegexOptions.CultureInvariant)]
  private static partial Regex NumberPattern();

  private static string Numbers(string text, Changes changes) =>
    NumberPattern().Replace(text, m =>
    {
      var rounded = RoundToTwoFigures(m.Groups["int"].Value, m.Groups["frac"].Success ? m.Groups["frac"].Value : null);
      if (rounded == null || rounded == m.Value) return m.Value;
      changes.Add(LookupChangeKinds.Number);
      return rounded;
    });

  /// <summary>187.4 becomes 190, 7.25 becomes 7.3, 1,234 becomes 1,200. Null when the number already has two significant figures or fewer.</summary>
  internal static string? RoundToTwoFigures(string integerPart, string? fraction)
  {
    var digits = integerPart.Replace(",", string.Empty, StringComparison.Ordinal);
    var significant = fraction == null ? digits.TrimStart('0').TrimEnd('0') : (digits + fraction).TrimStart('0');
    if (significant.Length <= 2) return null;
    if (!decimal.TryParse(fraction == null ? digits : digits + "." + fraction, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value == 0)
    {
      return null;
    }
    var magnitude = Magnitude(value);
    var factor = Pow10(magnitude - 1);
    var rounded = Math.Round(value / factor, MidpointRounding.AwayFromZero) * factor;
    var decimals = Math.Max(0, 1 - Magnitude(rounded));
    if (decimals == 0)
    {
      var whole = decimal.Truncate(rounded);
      return integerPart.Contains(',') && whole >= 1000
        ? whole.ToString("#,0", CultureInfo.InvariantCulture)
        : whole.ToString("0", CultureInfo.InvariantCulture);
    }
    return rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
  }

  private static int Magnitude(decimal value)
  {
    var magnitude = 0;
    var v = Math.Abs(value);
    while (v >= 10m)
    {
      v /= 10m;
      magnitude++;
    }
    while (v < 1m)
    {
      v *= 10m;
      magnitude--;
    }
    return magnitude;
  }

  private static decimal Pow10(int exponent)
  {
    var result = 1m;
    for (var i = 0; i < Math.Abs(exponent); i++) result *= 10m;
    return exponent >= 0 ? result : 1m / result;
  }

  // ---- text ----

  private static string Normalize(string text) => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Normalize(NormalizationForm.FormC);

  /// <summary>Collapses the doubled spaces a replacement can leave and trims each line.</summary>
  private static string Tidy(string text)
  {
    var lines = text.Split('\n').Select(l => MultipleSpaces().Replace(l, " ").Trim());
    return string.Join("\n", lines).Trim();
  }

  [GeneratedRegex(@"[ \t]{2,}")]
  private static partial Regex MultipleSpaces();

  private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

  /// <summary>Counts by kind, in the order the kinds were first seen.</summary>
  private sealed class Changes
  {
    private readonly List<string> _order = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    public void Add(string kind)
    {
      if (_counts.TryGetValue(kind, out var count))
      {
        _counts[kind] = count + 1;
        return;
      }
      _order.Add(kind);
      _counts[kind] = 1;
    }

    public IReadOnlyList<LookupChangeDto> ToList() => _order.Select(k => new LookupChangeDto(k, _counts[k])).ToList();
  }
}
