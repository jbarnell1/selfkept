using System.Reflection;
using DbUp;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SelfKept.Infrastructure.Data;

/// <summary>
/// SQL-first migration runner. Applies hand-written <c>*.up.sql</c> scripts that are embedded in
/// this assembly, in filename order, journaling each to the <c>schemaversions</c> table. There is
/// no EF model snapshot: each migration is a plain, reviewable SQL file.
///
/// Forward (<c>*.up.sql</c>) scripts are the only ones DbUp runs. Paired <c>*.down.sql</c> files
/// are committed beside them for manual, reviewed rollback and are intentionally not embedded.
/// </summary>
public static class SqlMigrationRunner
{
  // Session-level advisory-lock key: serializes migration runs so two app instances booting at
  // once don't race on the DDL. Arbitrary fixed constant.
  private const long MigrationAdvisoryLockKey = 0x5345_4C46_4B50_5401;

  private const string JournalTable = "\"public\".\"schemaversions\"";

  public static void Run(string connectionString, ILogger? logger = null, MigrationSettings? settings = null) =>
    Run(connectionString, logger, settings, processRunner: null, scriptsOverride: null);

  // processRunner and scriptsOverride are test seams (UnitTests has InternalsVisibleTo).
  internal static void Run(
    string connectionString,
    ILogger? logger,
    MigrationSettings? settings,
    IProcessRunner? processRunner,
    IReadOnlyList<DbUp.Engine.SqlScript>? scriptsOverride)
  {
    var assembly = typeof(SqlMigrationRunner).Assembly;

    // Create the database only when it genuinely doesn't exist (fresh local/dev). EnsureDatabase
    // connects to the "postgres" maintenance DB, which a locked-down app user often can't reach,
    // so take that path ONLY on the specific "database does not exist" (3D000) error, never on a
    // transient, auth or network failure (those must surface, not be masked as "create the DB").
    if (DatabaseDoesNotExist(connectionString, logger))
    {
      logger?.LogInformation("Target database does not exist; creating it (fresh environment).");
      EnsureDatabase.For.PostgresqlDatabase(connectionString);
    }

    // Serialize concurrent boots: hold a session-level advisory lock for the whole migration.
    using var lockConnection = new NpgsqlConnection(connectionString);
    lockConnection.Open();
    SetAdvisoryLock(lockConnection, acquire: true);
    try
    {
      var databaseHasState = TableExists(lockConnection, "schemaversions");

      // Transactional by default: DbUp itself runs without a transaction, and each wrapped script
      // is rewritten to BEGIN; <script>; <journal insert>; COMMIT; so the schema change and its
      // journal row are one atomic unit. A script opts out with the "-- selfkept:no-transaction"
      // marker (CREATE INDEX CONCURRENTLY and the like). See MigrationScript.
      var scripts = (scriptsOverride ?? LoadEmbeddedScripts(assembly))
        .OrderBy(s => s.Name, StringComparer.Ordinal)
        .Select(s => MigrationScript.Prepare(s, JournalTable))
        .ToList();
      var builder = DeployChanges.To
        .PostgresqlDatabase(connectionString)
        .JournalToPostgresqlTable("public", "schemaversions");
      // Swap in the duplicate-safe journal (each wrapped script already journals itself).
      builder.Configure(c => c.Journal = new IdempotentJournal(() => c.ConnectionManager, () => c.Log, "public", "schemaversions"));
      var upgrader = builder
        .WithScripts(scripts)
        .WithoutTransaction()
        // Disable DbUp's `$var$` substitution so dollar-quoting in plpgsql bodies is left alone.
        .WithVariablesDisabled()
        .LogToNowhere()
        .Build();

      var pending = upgrader.GetScriptsToExecute();
      logger?.LogInformation("SQL migrations pending: {Count} [{Scripts}]",
        pending.Count, string.Join(", ", pending.Select(s => s.Name)));

      // Safety net for a home server: dump and verify the database before touching an existing one.
      if (settings is { BackupBeforeMigrate: true } && databaseHasState && pending.Count > 0)
      {
        TakeBackupOrRefuse(connectionString, settings, processRunner ?? new SystemProcessRunner(), logger);
      }

      var result = upgrader.PerformUpgrade();
      if (!result.Successful)
      {
        logger?.LogCritical(result.Error, "SQL migration FAILED on script {Script}", result.ErrorScript?.Name);
        throw new InvalidOperationException(
          $"SQL migration failed on '{result.ErrorScript?.Name}': {result.Error?.Message}", result.Error);
      }

      logger?.LogInformation("SQL migrations applied successfully ({Count} executed).", result.Scripts.Count());
    }
    finally
    {
      SetAdvisoryLock(lockConnection, acquire: false);
    }
  }

  private static List<DbUp.Engine.SqlScript> LoadEmbeddedScripts(Assembly assembly) =>
    assembly.GetManifestResourceNames()
      .Where(n => n.EndsWith(".up.sql", StringComparison.OrdinalIgnoreCase))
      .Select(n =>
      {
        using var stream = assembly.GetManifestResourceStream(n)!;
        using var reader = new StreamReader(stream);
        return new DbUp.Engine.SqlScript(n, reader.ReadToEnd());
      })
      .ToList();

  private static void TakeBackupOrRefuse(
    string connectionString, MigrationSettings settings, IProcessRunner runner, ILogger? logger)
  {
    try
    {
      MigrationBackup.Create(connectionString, settings, runner, logger);
    }
    catch (MigrationBackupException ex)
    {
      if (settings.AllowMigrateWithoutBackup)
      {
        logger?.LogCritical(ex,
          "Pre-migration backup FAILED; migrating anyway because Migrations:AllowMigrateWithoutBackup is set.");
        return;
      }

      logger?.LogCritical(ex, "Pre-migration backup FAILED; refusing to migrate.");
      throw new InvalidOperationException(
        "Refusing to apply database migrations: the pre-migration backup failed (" + ex.Message + "). " +
        "Fix the backup, or set Migrations:AllowMigrateWithoutBackup=true to migrate without one.", ex);
    }
  }

  private static void SetAdvisoryLock(NpgsqlConnection openConnection, bool acquire)
  {
    using var cmd = openConnection.CreateCommand();
    cmd.CommandText = acquire
      ? "SELECT pg_advisory_lock(@key)"
      : "SELECT pg_advisory_unlock(@key)";
    cmd.Parameters.AddWithValue("key", MigrationAdvisoryLockKey);
    cmd.ExecuteNonQuery();
  }

  private static bool DatabaseDoesNotExist(string connectionString, ILogger? logger)
  {
    try
    {
      using var c = new NpgsqlConnection(connectionString);
      c.Open();
      return false;
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
    {
      logger?.LogInformation("Target database does not exist yet (3D000); it will be created.");
      return true;
    }
  }

  private static bool TableExists(NpgsqlConnection c, string unquotedTableName)
  {
    using var cmd = c.CreateCommand();
    cmd.CommandText = "SELECT to_regclass(@qualified) IS NOT NULL";
    cmd.Parameters.AddWithValue("qualified", $"public.\"{unquotedTableName}\"");
    return cmd.ExecuteScalar() is bool exists && exists;
  }
}
