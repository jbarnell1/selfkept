using System.Text;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// Collects streamed pieces and hands back whole sentences, so each one can be checked by the
/// guard before it reaches the screen. A sentence ends at ".", "!" or "?" followed by white space,
/// or at a line break. A full stop between digits ("7.5") or after a single capital ("A.") does
/// not end one. Whatever is left when the stream ends comes back from <see cref="Flush"/>.
/// </summary>
public sealed class SentenceBuffer
{
  private readonly StringBuilder _pending = new();

  public IReadOnlyList<string> Push(string piece)
  {
    _pending.Append(piece);
    var sentences = new List<string>();
    var text = _pending.ToString();
    var start = 0;
    for (var i = 0; i < text.Length; i++)
    {
      var c = text[i];
      var end = false;
      if (c == '\n')
      {
        end = true;
      }
      else if ((c == '.' || c == '!' || c == '?') && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
      {
        end = !(c == '.' && IsAbbreviation(text, i));
      }
      if (!end) continue;

      var sentence = text[start..(i + 1)].Trim();
      if (sentence.Length > 0) sentences.Add(sentence);
      start = i + 1;
    }
    _pending.Clear().Append(text, start, text.Length - start);
    return sentences;
  }

  public string Flush()
  {
    var rest = _pending.ToString().Trim();
    _pending.Clear();
    return rest;
  }

  // "e.g. ", "approx. " and initials such as "J. " keep the sentence going (the word before the
  // full stop is read as letters only, so "e.g." is seen as "g").
  private static bool IsAbbreviation(string text, int dot)
  {
    var wordStart = dot;
    while (wordStart > 0 && char.IsLetter(text[wordStart - 1])) wordStart--;
    var word = text[wordStart..dot];
    if (word.Length == 1) return true;
    return word is "eg" or "ie" or "approx" or "vs" or "Dr" or "Mr" or "Mrs" or "Ms";
  }
}
