using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Labs;

/// <summary>
/// Turns OCR or pasted lines into marker candidates (docs/labs-design.md, "Parsing"). Pure: the
/// alias table and the catalogue go in, nothing is stored.
///
/// 1. Normalise each line (collapse spaces, unify decimal separators, strip thousands separators).
/// 2. Match a marker by alias: case-insensitive, punctuation-blind token matching, a prefix match
///    preferred over one later in the line, the longest alias preferred over a shorter one.
/// 3. The value is the first number after the alias; the next token is the unit when the
///    catalogue or a convertible unit accepts it; a flag token (L, H, Low, High, arrows) is kept.
/// 4. Confidence 1.0 with alias, value and unit; 0.7 without a unit; 0.4 when the value sits on
///    the next line (a unit-only line after that is picked up too).
/// 5. Ratios are derived by the report service from the confirmed components, not here.
/// </summary>
public sealed class LabParser
{
  public const double ConfidenceFull = 1.0;
  public const double ConfidenceNoUnit = 0.7;
  public const double ConfidenceNextLine = 0.4;

  private static readonly Regex Spaces = new(@"[ \t ]+", RegexOptions.Compiled);
  private static readonly Regex Thousands = new(@"(?<=\d),(?=\d{3}(?!\d))", RegexOptions.Compiled);
  private static readonly Regex DecimalComma = new(@"(?<=\d),(?=\d)", RegexOptions.Compiled);
  private static readonly Regex Word = new(@"[A-Za-z0-9]+", RegexOptions.Compiled);
  // A number not glued to a letter before it (T3) and not a hyphenated label after it (25-OH).
  private static readonly Regex Number = new(@"(?<![A-Za-z0-9.])(?<prefix>[<>]\s?)?(?<num>[-+]?\d+(?:\.\d+)?)(?![0-9.]|-[A-Za-z])", RegexOptions.Compiled);
  private static readonly Regex Token = new(@"\S+", RegexOptions.Compiled);
  private static readonly Regex UnitOnlyLine = new(@"^[A-Za-zµμ%][A-Za-zµμ0-9%/.²]*$", RegexOptions.Compiled);

  private static readonly Dictionary<string, string> FlagWords = new(StringComparer.OrdinalIgnoreCase)
  {
    ["h"] = LabFlags.High, ["hi"] = LabFlags.High, ["high"] = LabFlags.High, ["↑"] = LabFlags.High, ["above"] = LabFlags.High, ["(h)"] = LabFlags.High, ["*h"] = LabFlags.High, ["h*"] = LabFlags.High,
    ["l"] = LabFlags.Low, ["lo"] = LabFlags.Low, ["low"] = LabFlags.Low, ["↓"] = LabFlags.Low, ["below"] = LabFlags.Low, ["(l)"] = LabFlags.Low, ["*l"] = LabFlags.Low, ["l*"] = LabFlags.Low,
    ["n"] = LabFlags.Normal, ["normal"] = LabFlags.Normal, ["ok"] = LabFlags.Normal, ["optimal"] = LabFlags.Normal, ["(n)"] = LabFlags.Normal,
  };

  private readonly IMetricCatalogue _catalogue;
  private readonly List<AliasEntry> _aliases;

  public LabParser(IMetricCatalogue catalogue, IEnumerable<LabMarkerAlias> aliases)
  {
    _catalogue = catalogue;
    _aliases = aliases
      .Where(a => catalogue.Find(a.MetricKey) != null)
      .Select(a => new AliasEntry(a.MetricKey, a.Alias, Word.Matches(a.Alias).Select(m => m.Value.ToLowerInvariant()).ToArray()))
      .Where(a => a.Tokens.Length > 0)
      .ToList();
  }

  public ParseResultDto Parse(string? text) =>
    Parse((text ?? string.Empty).Replace("\r\n", "\n").Split('\n'));

  public ParseResultDto Parse(IReadOnlyList<string> rawLines)
  {
    var lines = rawLines.Select(Normalise).ToArray();
    var consumed = new bool[lines.Length];
    var candidates = new List<LabCandidateDto>();
    var seenKeys = new HashSet<string>(StringComparer.Ordinal);

    for (var i = 0; i < lines.Length; i++)
    {
      if (consumed[i] || lines[i].Length == 0) continue;
      var match = MatchAlias(lines[i]);
      if (match == null) continue;
      var metric = _catalogue.Find(match.Value.Entry.MetricKey)!;
      if (seenKeys.Contains(metric.Key)) continue;

      var remainder = lines[i][match.Value.End..];
      var read = ReadValue(metric, remainder);
      if (read != null)
      {
        consumed[i] = true;
        seenKeys.Add(metric.Key);
        var sameLineUnit = read.Unit ?? ImpliedUnit(metric);
        candidates.Add(new LabCandidateDto(metric.Key, metric.DisplayName, read.Value, sameLineUnit, read.Flag, lines[i],
          sameLineUnit == null ? ConfidenceNoUnit : ConfidenceFull, i));
        continue;
      }

      // Alias on its own line: the value is on the next non-empty line, a unit maybe on the one after.
      var next = NextLine(lines, consumed, i);
      if (next < 0) continue;
      var nextRead = ReadValue(metric, lines[next]);
      if (nextRead == null) continue;
      var raw = lines[i] + " | " + lines[next];
      consumed[i] = true;
      consumed[next] = true;
      var unit = nextRead.Unit ?? ImpliedUnit(metric);
      if (unit == null)
      {
        var after = NextLine(lines, consumed, next);
        if (after >= 0 && UnitOnlyLine.IsMatch(lines[after]) && UnitConversion.Accepts(metric, lines[after]))
        {
          unit = lines[after];
          consumed[after] = true;
          raw += " | " + lines[after];
        }
      }
      seenKeys.Add(metric.Key);
      candidates.Add(new LabCandidateDto(metric.Key, metric.DisplayName, nextRead.Value, unit, nextRead.Flag, raw, ConfidenceNextLine, i));
    }

    var unmatched = new List<string>();
    for (var i = 0; i < lines.Length; i++)
    {
      if (!consumed[i] && lines[i].Length > 0) unmatched.Add(lines[i]);
    }
    return new ParseResultDto(candidates, unmatched);
  }

  /// <summary>Step 1: one line, trimmed, single spaces, "1,234" to "1234", "4,2" to "4.2", micro sign to u.</summary>
  public static string Normalise(string line)
  {
    var s = line.Replace('µ', 'u').Replace('μ', 'u').Replace('\t', ' ');
    s = Spaces.Replace(s, " ").Trim();
    s = Thousands.Replace(s, string.Empty);
    s = DecimalComma.Replace(s, ".");
    return s;
  }

  private (AliasEntry Entry, int End)? MatchAlias(string line)
  {
    var words = Word.Matches(line);
    if (words.Count == 0) return null;
    var tokens = words.Select(w => w.Value.ToLowerInvariant()).ToArray();

    (AliasEntry Entry, int Start, int End)? best = null;
    foreach (var alias in _aliases)
    {
      for (var start = 0; start + alias.Tokens.Length <= tokens.Length; start++)
      {
        if (!Matches(tokens, start, alias.Tokens)) continue;
        var end = words[start + alias.Tokens.Length - 1].Index + words[start + alias.Tokens.Length - 1].Length;
        if (best == null || start < best.Value.Start || (start == best.Value.Start && alias.Tokens.Length > best.Value.Entry.Tokens.Length)
            || (start == best.Value.Start && alias.Tokens.Length == best.Value.Entry.Tokens.Length && alias.Alias.Length > best.Value.Entry.Alias.Length))
        {
          best = (alias, start, end);
        }
        break;
      }
    }
    return best == null ? null : (best.Value.Entry, best.Value.End);
  }

  private static bool Matches(string[] tokens, int start, string[] alias)
  {
    for (var i = 0; i < alias.Length; i++)
    {
      if (!string.Equals(tokens[start + i], alias[i], StringComparison.Ordinal)) return false;
    }
    return true;
  }

  private static ReadResult? ReadValue(Metric metric, string text)
  {
    var number = Number.Match(text);
    if (!number.Success) return null;
    if (!double.TryParse(number.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) return null;

    string? unit = null;
    string? flag = null;
    var tail = text[(number.Index + number.Length)..];
    var tokens = Token.Matches(tail).Select(m => m.Value).Take(4).ToList();
    foreach (var token in tokens)
    {
      var clean = token.Trim('(', ')', ',', ';', ':');
      if (flag == null && FlagWords.TryGetValue(token, out var f)) { flag = f; continue; }
      if (flag == null && FlagWords.TryGetValue(clean, out var f2)) { flag = f2; continue; }
      if (unit == null && clean.Length > 0 && UnitConversion.Accepts(metric, clean)) { unit = clean; continue; }
      // Anything else (a reference range, a note) ends the scan once we have a unit.
      if (unit != null && flag != null) break;
    }
    // A flag glyph may sit between the alias and the value: "ApoB H 128 mg/dL".
    if (flag == null)
    {
      var before = text[..number.Index].Trim();
      var lastBefore = Token.Matches(before).Select(m => m.Value).LastOrDefault();
      if (lastBefore != null && FlagWords.TryGetValue(lastBefore, out var f3)) flag = f3;
    }
    return new ReadResult(value, unit, flag);
  }

  /// <summary>A ratio has no printed unit; the catalogue's "ratio" is implied.</summary>
  private static string? ImpliedUnit(Metric metric) => metric.Unit == "ratio" ? metric.Unit : null;

  private static int NextLine(string[] lines, bool[] consumed, int from)
  {
    for (var j = from + 1; j < lines.Length; j++)
    {
      if (lines[j].Length == 0) continue;
      return consumed[j] ? -1 : j;
    }
    return -1;
  }

  private sealed record AliasEntry(string MetricKey, string Alias, string[] Tokens);

  private sealed record ReadResult(double Value, string? Unit, string? Flag);
}
