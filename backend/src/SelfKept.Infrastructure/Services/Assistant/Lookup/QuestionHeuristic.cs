using System.Text.RegularExpressions;

namespace SelfKept.Infrastructure.Services.Assistant.Lookup;

/// <summary>
/// Whether a chat message reads as a question, for the lookup offer under the reply. Small and
/// deterministic, and careful with the statements people log here ("Had oats", "Did 5k", "Could
/// barely sleep", "When I woke up my head hurt"): a question mark anywhere; a sentence that opens
/// with what, why, how, which, whose or does; "when", "where" or "who" followed by a verb such as
/// "should"; a verb such as "is", "can" or "should" followed by who or what it asks about ("Is
/// it", "Can I", "Should my"); or "I wonder", "tell me", "do you know".
/// </summary>
public static partial class QuestionHeuristic
{
  private static readonly HashSet<string> Openers = new(StringComparer.OrdinalIgnoreCase)
  {
    "what", "whats", "what's", "why", "how", "hows", "how's", "which", "whose", "does", "explain",
  };

  private static readonly HashSet<string> PlaceOpeners = new(StringComparer.OrdinalIgnoreCase) { "when", "where", "who", "whom" };

  private static readonly HashSet<string> Auxiliaries = new(StringComparer.OrdinalIgnoreCase)
  {
    "is", "are", "am", "was", "were", "do", "does", "did", "can", "could", "should", "would", "will", "may", "might", "has", "have", "shall",
  };

  private static readonly HashSet<string> Subjects = new(StringComparer.OrdinalIgnoreCase)
  {
    "i", "you", "it", "we", "they", "he", "she", "this", "that", "these", "those", "there", "my", "your", "our", "their", "his", "her",
    "the", "anyone", "someone", "people", "taking", "eating", "drinking",
  };

  private static readonly HashSet<string> Exclamations = new(StringComparer.OrdinalIgnoreCase) { "a", "an" };

  public static bool IsQuestion(string? message)
  {
    if (string.IsNullOrWhiteSpace(message)) return false;
    var text = message.Trim();
    if (text.Contains('?')) return true;
    if (Asking().IsMatch(text)) return true;
    foreach (var sentence in SentenceSplit().Split(text))
    {
      var words = Word().Matches(sentence).Select(m => m.Value.Replace('’', '\'')).ToList();
      if (words.Count < 3) continue;
      var (first, second) = (words[0], words[1]);
      if (Openers.Contains(first) && !Exclamations.Contains(second)) return true;
      if (PlaceOpeners.Contains(first) && Auxiliaries.Contains(second)) return true;
      if (Auxiliaries.Contains(first) && Subjects.Contains(second)) return true;
    }
    return false;
  }

  [GeneratedRegex(@"\b(?:I\s+wonder|I\s+was\s+wondering|tell\s+me|do\s+you\s+know|is\s+it\s+true)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex Asking();

  [GeneratedRegex(@"(?<=[.!])\s+|\n+")]
  private static partial Regex SentenceSplit();

  [GeneratedRegex(@"[\p{L}\p{N}'’]+")]
  private static partial Regex Word();
}
