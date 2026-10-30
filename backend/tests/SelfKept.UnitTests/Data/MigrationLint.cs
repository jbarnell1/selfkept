using System.Text.RegularExpressions;
using SelfKept.Infrastructure.Data;

namespace SelfKept.UnitTests.Data;

public sealed record MigrationFile(string Name, string Sql);

/// <summary>
/// Rules every SQL migration must pass (see Migrations/Sql/README.md). Pure functions over
/// (name, sql) pairs so the rules are tested with fixtures and then run over the real folder.
/// </summary>
public static class MigrationLint
{
  private static readonly Regex NameShape = new(@"^(?<num>\d{4})_[a-z0-9_]+\.up\.sql$", RegexOptions.Compiled);
  private static readonly Regex ReviewedMarker = new(
    @"^[ \t]*--[ \t]*selfkept:reviewed-destructive:[ \t]*\S", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
  private static readonly Regex DropObject = new(@"\bDROP\s+(TABLE|COLUMN|SCHEMA)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
  private static readonly Regex Truncate = new(@"\bTRUNCATE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
  // COLUMN is optional in PostgreSQL: "ALTER TABLE t ALTER c TYPE bigint" is the same statement.
  private static readonly Regex AlterType = new(
    @"\bALTER\s+(COLUMN\s+)?(""[^""]+""|\w+)\s+(SET\s+DATA\s+)?TYPE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
  // "ALTER TABLE t DROP c" drops a column too. Excludes the non-destructive DROP forms
  // (CONSTRAINT, DEFAULT, NOT NULL, IDENTITY, EXPRESSION).
  private static readonly Regex AlterTable = new(@"\bALTER\s+TABLE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
  private static readonly Regex DropColumnShort = new(
    @"\bDROP\s+(COLUMN\s+)?(IF\s+EXISTS\s+)?(?!(CONSTRAINT|DEFAULT|NOT|IDENTITY|EXPRESSION)\b)(""[^""]+""|\w+)",
    RegexOptions.IgnoreCase | RegexOptions.Compiled);
  // Anywhere in the statement, so "WITH ... DELETE FROM" is caught too.
  private static readonly Regex DeleteFrom = new(@"\bDELETE\s+FROM\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
  private static readonly Regex QuotedLiteral = new(@"'(?:[^']|'')*'", RegexOptions.Compiled);
  private static readonly Regex Where = new(@"\bWHERE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
  private static readonly Regex NeedsNoTransaction = new(
    @"\b(CREATE\s+(UNIQUE\s+)?INDEX\s+CONCURRENTLY|DROP\s+INDEX\s+CONCURRENTLY|REINDEX\s+[^;]*CONCURRENTLY|VACUUM|CREATE\s+DATABASE|DROP\s+DATABASE|ALTER\s+SYSTEM)\b",
    RegexOptions.IgnoreCase | RegexOptions.Compiled);
  private static readonly Regex TxnControl = new(
    @"^[ \t]*(COMMIT|ROLLBACK|END|BEGIN|START\s+TRANSACTION)\b[^;\r\n]*;", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

  public static IReadOnlyList<string> Check(IReadOnlyCollection<MigrationFile> ups, IReadOnlyCollection<string> downNames)
  {
    var problems = new List<string>();
    var downs = new HashSet<string>(downNames, StringComparer.Ordinal);

    foreach (var f in ups.OrderBy(x => x.Name, StringComparer.Ordinal))
    {
      var m = NameShape.Match(f.Name);
      if (!m.Success)
      {
        problems.Add($"{f.Name}: name must look like NNNN_snake_case.up.sql");
        continue;
      }

      var downName = f.Name[..^".up.sql".Length] + ".down.sql";
      if (!downs.Contains(downName))
      {
        problems.Add($"{f.Name}: missing down script {downName}");
      }

      CheckDestructive(f, problems);
      CheckTransactionRules(f, problems);
    }

    foreach (var g in ups.GroupBy(x => x.Name[..Math.Min(4, x.Name.Length)]).Where(g => g.Count() > 1))
    {
      var names = g.Select(x => x.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
      problems.Add($"duplicate migration number {g.Key}: {string.Join(", ", names)}");
    }

    var numbers = ups.Select(x => NameShape.Match(x.Name)).Where(m => m.Success).Select(m => int.Parse(m.Groups["num"].Value)).Distinct().OrderBy(n => n).ToList();
    for (var i = 1; i < numbers.Count; i++)
    {
      if (numbers[i] != numbers[i - 1] + 1)
      {
        problems.Add($"migration numbers must be consecutive: {numbers[i - 1]:0000} is followed by {numbers[i]:0000}");
      }
    }

    return problems;
  }

  private static void CheckDestructive(MigrationFile f, List<string> problems)
  {
    if (ReviewedMarker.IsMatch(f.Sql))
    {
      return;
    }

    var code = MigrationScript.StripComments(f.Sql);
    var hits = new List<string>();
    if (DropObject.Match(code) is { Success: true } d) hits.Add(d.Value.ToUpperInvariant());
    else if (code.Split(';').Any(stmt => AlterTable.IsMatch(stmt) && DropColumnShort.IsMatch(stmt))) hits.Add("DROP COLUMN");
    if (Truncate.IsMatch(code)) hits.Add("TRUNCATE");
    if (AlterType.IsMatch(code)) hits.Add("ALTER COLUMN ... TYPE");
    if (code.Split(';').Any(stmt => DeleteFrom.IsMatch(stmt) && !HasOuterWhere(stmt))) hits.Add("DELETE without WHERE");

    if (hits.Count > 0)
    {
      problems.Add(
        $"{f.Name}: destructive statement ({string.Join(", ", hits)}). Forward migrations are additive; " +
        $"if this is deliberate and no running version uses it, add a line '{MigrationScript.ReviewedDestructiveMarker} <reason>'.");
    }
  }

  /// <summary>
  /// True only when a WHERE appears outside every parenthesis, so a WHERE inside a subquery or CTE
  /// does not count. Conservative: when the outer predicate cannot be proven, the statement is
  /// flagged and needs the reviewed marker.
  /// </summary>
  private static bool HasOuterWhere(string stmt)
  {
    var text = QuotedLiteral.Replace(stmt, "''");
    var depth = 0;
    var outer = new System.Text.StringBuilder();
    foreach (var c in text)
    {
      if (c == '(') depth++;
      else if (c == ')') depth = Math.Max(0, depth - 1);
      else if (depth == 0) outer.Append(c);
    }
    return Where.IsMatch(outer.ToString());
  }

  private static void CheckTransactionRules(MigrationFile f, List<string> problems)
  {
    var topLevel = MigrationScript.TopLevelSql(f.Sql);
    var optedOut = MigrationScript.HasNoTransactionMarker(f.Sql);

    if (NeedsNoTransaction.IsMatch(topLevel) && !optedOut)
    {
      problems.Add($"{f.Name}: contains a statement that cannot run in a transaction; add the line '{MigrationScript.NoTransactionMarker}'.");
    }

    var controls = TxnControl.Matches(topLevel).Select(m => m.Value.Trim().ToUpperInvariant()).ToList();
    if (controls.Count == 0) return;

    if (optedOut)
    {
      problems.Add($"{f.Name}: scripts with '{MigrationScript.NoTransactionMarker}' must not manage their own transaction ({controls[0]}).");
    }
    else if (controls.Any(c => c.StartsWith("BEGIN") || c.StartsWith("START")))
    {
      problems.Add($"{f.Name}: new scripts must not open their own BEGIN; the runner wraps each script in a transaction.");
    }
    else
    {
      problems.Add($"{f.Name}: remove {controls[0]}; the runner wraps each script in a transaction.");
    }
  }
}
