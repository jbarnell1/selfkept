using FluentAssertions;
using SelfKept.Infrastructure.Data;
using Xunit;

namespace SelfKept.UnitTests.Data;

public class MigrationLintTests
{
  private static MigrationFile Up(string name, string sql = "ALTER TABLE person ADD COLUMN IF NOT EXISTS nickname varchar(40);") =>
    new(name, sql);

  private static IReadOnlyList<string> Lint(MigrationFile[] ups, params string[] extraDowns)
  {
    var downs = ups.Select(u => u.Name.Replace(".up.sql", ".down.sql")).Concat(extraDowns).ToArray();
    return MigrationLint.Check(ups, downs);
  }

  [Fact]
  public void CleanScript_Passes()
  {
    Lint(new[] { Up("0003_add_thing.up.sql") }).Should().BeEmpty();
  }

  [Fact]
  public void DuplicateNumber_IsRejected()
  {
    var problems = Lint(new[] { Up("0003_add_a.up.sql"), Up("0003_add_b.up.sql") });

    problems.Should().ContainSingle().Which.Should().Contain("duplicate migration number 0003");
  }

  [Fact]
  public void GapInNumbering_IsRejected()
  {
    var problems = Lint(new[] { Up("0001_a.up.sql"), Up("0003_c.up.sql") });

    problems.Should().ContainSingle().Which.Should().Contain("consecutive");
  }

  [Fact]
  public void MissingDownScript_IsRejected()
  {
    var problems = MigrationLint.Check(new[] { Up("0003_add_thing.up.sql") }, Array.Empty<string>());

    problems.Should().ContainSingle().Which.Should().Contain("missing down script 0003_add_thing.down.sql");
  }

  [Theory]
  [InlineData("DROP TABLE old_thing;", "DROP TABLE")]
  [InlineData("ALTER TABLE person DROP COLUMN nickname;", "DROP COLUMN")]
  [InlineData("TRUNCATE audit_log;", "TRUNCATE")]
  [InlineData("DELETE FROM audit_log;", "DELETE without WHERE")]
  [InlineData("WITH x AS (SELECT 1 WHERE true) DELETE FROM audit_log;", "DELETE without WHERE")]
  [InlineData("ALTER TABLE person DROP nickname;", "DROP COLUMN")]
  [InlineData("ALTER TABLE person ALTER COLUMN nickname TYPE text;", "ALTER COLUMN ... TYPE")]
  [InlineData("DO $$ BEGIN\n  ALTER TABLE person ALTER COLUMN nickname TYPE text;\nEND $$;", "ALTER COLUMN ... TYPE")]
  public void DestructiveStatement_IsRejected(string sql, string expected)
  {
    var problems = Lint(new[] { Up("0003_x.up.sql", sql) });

    problems.Should().ContainSingle().Which.Should().Contain("destructive statement").And.Contain(expected);
  }

  [Fact]
  public void DestructiveStatement_WithReviewedMarker_Passes()
  {
    var sql = "-- selfkept:reviewed-destructive: column unused since 0.1.0, no running version reads it\nALTER TABLE person DROP COLUMN nickname;";

    Lint(new[] { Up("0003_x.up.sql", sql) }).Should().BeEmpty();
  }

  [Fact]
  public void ReviewedMarker_WithoutAReason_DoesNotCount()
  {
    var sql = "-- selfkept:reviewed-destructive:\nDROP TABLE old_thing;";

    Lint(new[] { Up("0003_x.up.sql", sql) }).Should().ContainSingle().Which.Should().Contain("destructive");
  }

  [Theory]
  [InlineData("DELETE FROM audit_log WHERE occurred_at < now() - interval '1 year';")]
  [InlineData("-- DROP TABLE mentioned in a comment\nSELECT 1;")]
  [InlineData("DROP INDEX IF EXISTS ix_old;")]
  [InlineData("ALTER TABLE person DROP CONSTRAINT IF EXISTS ck_old;")]
  [InlineData("ALTER TABLE person ALTER COLUMN nickname DROP NOT NULL;")]
  public void SafeStatements_Pass(string sql)
  {
    Lint(new[] { Up("0003_x.up.sql", sql) }).Should().BeEmpty();
  }

  [Fact]
  public void ConcurrentIndex_WithoutMarker_IsRejected_WithMarker_Passes()
  {
    const string sql = "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix ON person (nickname);";

    Lint(new[] { Up("0003_x.up.sql", sql) }).Should().ContainSingle().Which.Should().Contain("cannot run in a transaction");
    Lint(new[] { Up("0003_x.up.sql", "-- selfkept:no-transaction\n" + sql) }).Should().BeEmpty();
  }

  [Fact]
  public void Script_WithOwnBeginOrCommit_IsRejected()
  {
    Lint(new[] { Up("0003_x.up.sql", "BEGIN;\nSELECT 1;\nCOMMIT;") }).Should().Contain(p => p.Contains("must not open their own BEGIN"));
    Lint(new[] { Up("0003_x.up.sql", "SELECT 1;\nCOMMIT;") }).Should().Contain(p => p.Contains("remove COMMIT"));
  }

  [Fact]
  public void BadName_IsRejected()
  {
    Lint(new[] { Up("3_thing.up.sql") }).Should().ContainSingle().Which.Should().Contain("name must look like");
    Lint(new[] { Up("0003_CamelCase.up.sql") }).Should().ContainSingle().Which.Should().Contain("name must look like");
  }

  [Fact]
  public void Classify_WrapsPlainScripts_AndHonoursOptOut()
  {
    MigrationScript.Classify("SELECT 1;").Should().Be(MigrationTransactionMode.Wrapped);
    MigrationScript.Classify("-- selfkept:no-transaction\nVACUUM;").Should().Be(MigrationTransactionMode.OptOut);
    MigrationScript.Classify("BEGIN;\nSELECT 1;\nCOMMIT;").Should().Be(MigrationTransactionMode.SelfManaged);
    MigrationScript.Classify("DO $$ BEGIN PERFORM 1; END $$;").Should().Be(MigrationTransactionMode.Wrapped, "a plpgsql BEGIN is not a transaction");
  }

  // --- the real folder ---

  public static string SqlFolder()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
      var candidate = Path.Combine(dir.FullName, "src", "SelfKept.Infrastructure", "Migrations", "Sql");
      if (Directory.Exists(candidate)) return candidate;
      dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate backend/src/SelfKept.Infrastructure/Migrations/Sql.");
  }

  [Fact]
  public void RealMigrations_PassTheLint()
  {
    var folder = SqlFolder();
    var ups = Directory.GetFiles(folder, "*.up.sql")
      .Select(f => new MigrationFile(Path.GetFileName(f), File.ReadAllText(f)))
      .ToArray();
    var downs = Directory.GetFiles(folder, "*.down.sql").Select(f => Path.GetFileName(f)!).ToArray();

    ups.Should().NotBeEmpty();
    ups.Select(u => u.Name).Should().Contain("0001_initial.up.sql");
    MigrationLint.Check(ups, downs).Should().BeEmpty("every migration must satisfy the lint (see MigrationLint)");
  }

  [Fact]
  public void RealMigrations_AreEmbeddedInTheInfrastructureAssembly()
  {
    var embedded = typeof(SqlMigrationRunner).Assembly.GetManifestResourceNames()
      .Where(n => n.EndsWith(".up.sql", StringComparison.Ordinal))
      .ToList();
    var onDisk = Directory.GetFiles(SqlFolder(), "*.up.sql").Select(Path.GetFileName).ToList();

    embedded.Should().HaveCount(onDisk.Count);
    foreach (var file in onDisk)
    {
      embedded.Should().Contain(n => n.EndsWith(file!, StringComparison.Ordinal));
    }
    embedded.Should().NotContain(n => n.EndsWith(".down.sql", StringComparison.Ordinal), "downs are never embedded");
  }

  [Fact]
  public void EveryRealScript_IsWrappedOrOptedOut()
  {
    foreach (var f in Directory.GetFiles(SqlFolder(), "*.up.sql"))
    {
      MigrationScript.Classify(File.ReadAllText(f)).Should().NotBe(MigrationTransactionMode.SelfManaged, f);
    }
  }
}
