using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace SelfKept.IntegrationTests;

/// <summary>
/// Runs the API in-process. With SELFKEPT_TEST_CONNECTION set, each factory gets its own scratch
/// Postgres database (created here, migrated by the API on startup, dropped on dispose). Without
/// it, the EF InMemory provider stands in so the HTTP surface is still exercised.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
  public const string TestConnectionEnvVar = "SELFKEPT_TEST_CONNECTION";

  private readonly string? _scratchConnection;

  public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
  {
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
  };

  public ApiFactory()
  {
    var baseline = Environment.GetEnvironmentVariable(TestConnectionEnvVar);
    if (!string.IsNullOrWhiteSpace(baseline))
    {
      _scratchConnection = CreateScratchDatabase(baseline);
    }
  }

  public bool UsesPostgres => _scratchConnection != null;

  /// <summary>The scratch database this factory runs against, for tests that inspect the schema directly. Null on InMemory.</summary>
  public string? ScratchConnection => _scratchConnection;

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Testing");
    // UseSetting lands in host configuration, which Program.cs sees before Build(); values added
    // through ConfigureAppConfiguration only arrive at Build() and would miss the startup checks.
    var settings = new Dictionary<string, string?>
    {
      ["Jwt:Key"] = "integration-test-signing-key-9f8e7d6c5b4a39281706f5e4d3c2b1a0",
      ["Encryption:Key"] = "integration-test-encryption-key-0a1b2c3d4e5f60718293a4b5c6d7e8f9",
      ["Auth:RequireSecureCookie"] = "false",
      ["OpenApi:Enabled"] = "false",
      // The test server answers on http://localhost; the OAuth issuer and MCP resource follow it.
      ["App:BaseUrl"] = "http://localhost",
    };
    if (_scratchConnection != null)
    {
      settings["Database:Provider"] = "postgres";
      settings["ConnectionStrings:SelfKept"] = _scratchConnection;
    }
    else
    {
      settings["Database:Provider"] = "inmemory";
      settings["Database:InMemoryName"] = "tests-" + Guid.NewGuid().ToString("N");
    }
    foreach (var (key, value) in settings)
    {
      builder.UseSetting(key, value);
    }
  }

  protected override void Dispose(bool disposing)
  {
    base.Dispose(disposing);
    if (disposing && _scratchConnection != null)
    {
      DropScratchDatabase(_scratchConnection);
    }
  }

  private static string CreateScratchDatabase(string baseline)
  {
    var name = "selfkept_it_" + Guid.NewGuid().ToString("N")[..12];
    using var connection = new NpgsqlConnection(baseline);
    connection.Open();
    using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
    cmd.ExecuteNonQuery();
    return new NpgsqlConnectionStringBuilder(baseline) { Database = name }.ConnectionString;
  }

  private static void DropScratchDatabase(string scratch)
  {
    NpgsqlConnection.ClearAllPools();
    var name = new NpgsqlConnectionStringBuilder(scratch).Database;
    using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable(TestConnectionEnvVar)!);
    connection.Open();
    using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
    cmd.ExecuteNonQuery();
  }
}

internal static class HttpExtensions
{
  public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
    client.PostAsJsonAsync(url, body, ApiFactory.Json);

  public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
    client.PutAsJsonAsync(url, body, ApiFactory.Json);

  public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
  {
    var text = await response.Content.ReadAsStringAsync();
    return JsonDocument.Parse(text).RootElement.Clone();
  }

  public static void UseBearer(this HttpClient client, string token) =>
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
