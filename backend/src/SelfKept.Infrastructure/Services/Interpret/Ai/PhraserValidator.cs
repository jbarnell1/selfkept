using System.Globalization;
using System.Text.RegularExpressions;

namespace SelfKept.Infrastructure.Services.Interpret.Ai;

/// <summary>
/// The gate between the model and the screen. Every number in the output must already be in
/// the input (so the model restates, never invents), the text stays short, carries no
/// diagnostic language and no em dash. Anything else falls back to the templated sentence.
/// </summary>
public static class PhraserValidator
{
  public const int MaxLength = 480;

  // Integers and decimals, with optional thousands separators ("1,200") and a sign.
  private static readonly Regex NumberToken = new(@"[-+]?\d{1,3}(?:,\d{3})+(?:\.\d+)?|[-+]?\d+(?:\.\d+)?", RegexOptions.Compiled);
  private static readonly Regex Diagnostic = new(@"\bdiagnos\w*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
  // "you have (a|an|the)? <condition>": a named condition after "you have" reads as a verdict.
  private static readonly Regex YouHaveCondition = new(
    @"\byou\s+(?:may\s+|might\s+|probably\s+|likely\s+)?have\s+(?:a\s+|an\s+|the\s+|some\s+)?(?:\w+\s+){0,2}?" +
    @"(?:\w*(?:itis|osis|emia|aemia|pathy|algia)\b|disease|disorder|syndrome|infection|virus|flu|covid|cold|fever|apnea|apnoea|anemia|anaemia|" +
    @"hypertension|arrhythmia|depression|anxiety|diabetes|illness|condition|cancer|tumou?r|deficiency)\b",
    RegexOptions.IgnoreCase | RegexOptions.Compiled);

  public static ValidationResult Validate(string input, string? output)
  {
    if (string.IsNullOrWhiteSpace(output)) return ValidationResult.Fail("empty");
    var text = output.Trim();
    if (text.Length > MaxLength) return ValidationResult.Fail("too long");
    if (text.Contains('\u2014') || text.Contains('\u2013')) return ValidationResult.Fail("dash");
    if (Diagnostic.IsMatch(text)) return ValidationResult.Fail("diagnostic language");
    if (YouHaveCondition.IsMatch(text)) return ValidationResult.Fail("names a condition");

    var allowed = Numbers(input);
    foreach (var number in Numbers(text))
    {
      if (!allowed.Contains(number)) return ValidationResult.Fail($"number {number} not in input");
    }
    return ValidationResult.Ok(text);
  }

  /// <summary>Numbers as canonical strings: "1,200" and "1200.0" both become "1200"; "7.50" becomes "7.5".</summary>
  public static HashSet<string> Numbers(string text)
  {
    var set = new HashSet<string>(StringComparer.Ordinal);
    foreach (Match m in NumberToken.Matches(text))
    {
      var raw = m.Value.Replace(",", string.Empty).TrimStart('+');
      if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
      {
        set.Add(Canonical(value));
      }
    }
    return set;
  }

  private static string Canonical(decimal value)
  {
    var normalised = value / 1.000000000000000000000000000000000m; // strips trailing zeros
    var text = normalised.ToString("0.############################", CultureInfo.InvariantCulture);
    return text == "-0" ? "0" : text;
  }
}

public readonly record struct ValidationResult(bool IsValid, string? Text, string? Reason)
{
  public static ValidationResult Ok(string text) => new(true, text, null);
  public static ValidationResult Fail(string reason) => new(false, null, reason);
}
