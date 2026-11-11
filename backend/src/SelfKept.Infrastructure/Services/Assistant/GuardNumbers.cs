using System.Text.RegularExpressions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret.Ai;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// Numbers as the guard compares them: digits in canonical form (PhraserValidator.Numbers) plus
/// the common number words, so "three days" counts as 3. "one" is left out on purpose: "one
/// thing" is not a figure.
/// </summary>
public static partial class GuardNumbers
{
  private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
  {
    ["two"] = "2", ["three"] = "3", ["four"] = "4", ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8",
    ["nine"] = "9", ["ten"] = "10", ["eleven"] = "11", ["twelve"] = "12", ["thirteen"] = "13", ["fourteen"] = "14",
    ["fifteen"] = "15", ["sixteen"] = "16", ["seventeen"] = "17", ["eighteen"] = "18", ["nineteen"] = "19",
    ["twenty"] = "20", ["thirty"] = "30", ["forty"] = "40", ["fifty"] = "50", ["sixty"] = "60", ["seventy"] = "70",
    ["eighty"] = "80", ["ninety"] = "90", ["hundred"] = "100", ["thousand"] = "1000", ["dozen"] = "12",
    ["twice"] = "2", ["double"] = "2", ["triple"] = "3",
  };

  public static HashSet<string> Of(string? text)
  {
    if (string.IsNullOrEmpty(text)) return new HashSet<string>(StringComparer.Ordinal);
    var set = PhraserValidator.Numbers(text);
    foreach (Match m in WordToken().Matches(text))
    {
      if (Words.TryGetValue(m.Value, out var digits)) set.Add(digits);
    }
    return set;
  }

  /// <summary>A guard context from any number of allowed texts: the brief, playbook entries, the person's own message.</summary>
  public static GuardContext Context(params string?[] texts)
  {
    var numbers = new HashSet<string>(StringComparer.Ordinal);
    foreach (var text in texts) numbers.UnionWith(Of(text));
    return new GuardContext(numbers, string.Join("\n", texts.Where(t => !string.IsNullOrEmpty(t))));
  }

  [GeneratedRegex(@"\b[a-z]+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex WordToken();
}
