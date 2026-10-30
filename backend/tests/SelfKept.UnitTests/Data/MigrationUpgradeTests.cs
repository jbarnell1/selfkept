using FluentAssertions;
using Npgsql;
using SelfKept.Infrastructure.Data;
using Xunit;

namespace SelfKept.UnitTests.Data;

/// <summary>Runs only when SELFKEPT_TEST_CONNECTION points at a scratch Postgres; skips otherwise.</summary>
public sealed class RequiresPostgresFactAttribute : FactAttribute
{
  public const string EnvVar = "SELFKEPT_TEST_CONNECTION";

  public RequiresPostgresFactAttribute()
  {
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
    {
      Skip = $"{EnvVar} is not set; set it to a scratch Postgres connection string to run this test.";
    }
  }

  public static string ConnectionString => Environment.GetEnvironmentVariable(EnvVar)!;
}

/// <summary>Empty database to head: every up script applies, the journal is complete, the schema exists.</summary>
public class MigrationUpgradeTests
{
  private static readonly string[] ExpectedTables =
    ["person", "household", "user_account", "household_member", "household_invite", "share_grant", "refresh_token", "audit_log", "schemaversions",
     "metric", "source", "sample", "workout", "checkin", "daily_summary",
     "baseline", "insight", "ai_call", "today_cache",
     "lab_report", "lab_result", "lab_marker_alias", "lab_reference",
     "oauth_client", "oauth_authorization_request", "oauth_refresh_token", "personal_access_token", "ai_memory",
     "oura_connection", "oura_auth_state"];

  [RequiresPostgresFact]
  public async Task EmptyDatabase_ToHead_CreatesEveryTable_AndIsIdempotent()
  {
    var scratch = await CreateScratchDatabaseAsync();
    try
    {
      SqlMigrationRunner.Run(scratch);
      // A second run finds nothing pending and changes nothing.
      SqlMigrationRunner.Run(scratch);

      await using var connection = new NpgsqlConnection(scratch);
      await connection.OpenAsync();

      var tables = new List<string>();
      await using (var cmd = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname = 'public'", connection))
      await using (var reader = await cmd.ExecuteReaderAsync())
      {
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
      }
      tables.Should().Contain(ExpectedTables);

      var upCount = Directory.GetFiles(MigrationLintTests.SqlFolder(), "*.up.sql").Length;
      await using var journal = new NpgsqlCommand("SELECT count(*) FROM schemaversions", connection);
      ((long)(await journal.ExecuteScalarAsync())!).Should().Be(upCount, "each up script is journaled exactly once");

      // 0004 seeds the whole catalogue; 0003 installs the partition helper and sample is partitioned.
      await using var metrics = new NpgsqlCommand("SELECT count(*) FROM metric", connection);
      ((long)(await metrics.ExecuteScalarAsync())!).Should().Be(MetricSeed.All.Count);
      // 0007 seeds the reference ranges and the aliases.
      await using var references = new NpgsqlCommand("SELECT count(*) FROM lab_reference", connection);
      ((long)(await references.ExecuteScalarAsync())!).Should().Be(LabSeed.References.Count);
      await using var aliases = new NpgsqlCommand("SELECT count(*) FROM lab_marker_alias", connection);
      ((long)(await aliases.ExecuteScalarAsync())!).Should().Be(LabSeed.Aliases.Count);
      await using var partitioned = new NpgsqlCommand("SELECT relkind FROM pg_class WHERE relname = 'sample'", connection);
      ((char)(await partitioned.ExecuteScalarAsync())!).Should().Be('p', "sample is a partitioned table");
      await using (var ensure = new NpgsqlCommand("SELECT ensure_sample_partition('2026-10-15'::date)", connection))
      {
        await ensure.ExecuteNonQueryAsync();
        await ensure.ExecuteNonQueryAsync(); // idempotent
      }
      await using var exists = new NpgsqlCommand("SELECT to_regclass('sample_202610') IS NOT NULL", connection);
      ((bool)(await exists.ExecuteScalarAsync())!).Should().BeTrue("the partition helper creates sample_YYYYMM");
    }
    finally
    {
      await DropScratchDatabaseAsync(scratch);
    }
  }

  private static async Task<string> CreateScratchDatabaseAsync()
  {
    var baseline = new NpgsqlConnectionStringBuilder(RequiresPostgresFactAttribute.ConnectionString);
    var name = "selfkept_test_" + Guid.NewGuid().ToString("N")[..12];
    await using var connection = new NpgsqlConnection(baseline.ConnectionString);
    await connection.OpenAsync();
    await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
    await cmd.ExecuteNonQueryAsync();
    return new NpgsqlConnectionStringBuilder(baseline.ConnectionString) { Database = name }.ConnectionString;
  }

  private static async Task DropScratchDatabaseAsync(string scratch)
  {
    NpgsqlConnection.ClearAllPools();
    var scratchBuilder = new NpgsqlConnectionStringBuilder(scratch);
    var name = scratchBuilder.Database;
    await using var connection = new NpgsqlConnection(RequiresPostgresFactAttribute.ConnectionString);
    await connection.OpenAsync();
    await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
    await cmd.ExecuteNonQueryAsync();
  }
}
