using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using SelfKept.Core.Auth;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;

namespace SelfKept.Infrastructure.Mcp;

/// <summary>
/// Who is calling a tool, taken only from the validated token (an MCP access token or a personal
/// access token). Arguments never name the caller.
/// </summary>
public sealed record McpCaller(Guid UserId, Guid PersonId, Guid? HouseholdId, IReadOnlySet<string> Scopes, string ClientId, string ClientName)
{

  public bool Has(string scope) => Scopes.Contains(scope);

  /// <summary>Null when the principal lacks a user or person id.</summary>
  public static McpCaller? From(ClaimsPrincipal? user)
  {
    if (user?.Identity?.IsAuthenticated != true)
      return null;
    if (!Guid.TryParse(user.FindFirst(TokenClaims.Subject)?.Value, out var userId))
      return null;
    if (!Guid.TryParse(user.FindFirst(TokenClaims.Person)?.Value, out var personId))
      return null;
    Guid? household = Guid.TryParse(user.FindFirst(TokenClaims.Household)?.Value, out var h) ? h : null;
    var scopes = user.FindAll(TokenClaims.Scope)
      .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      .ToHashSet(StringComparer.Ordinal);
    var clientId = user.FindFirst(TokenClaims.ClientId)?.Value ?? "unknown";
    var clientName = user.FindFirst(TokenClaims.ClientName)?.Value ?? clientId;
    return new McpCaller(userId, personId, household, scopes, clientId, clientName);
  }
}

/// <summary>Whose data a call reads: the caller, or a household member named in the person argument.</summary>
public sealed record McpTarget(Guid PersonId, string DisplayName, bool IsSelf);

public sealed record McpToolContext(McpCaller Caller, McpTarget Target);

/// <summary>What a tool returns: the payload serialised for the model, and a row count for the audit log.</summary>
public sealed record McpToolOutput(object Data, int Rows);

/// <summary>An expected failure: shown to the model as an error result with this message.</summary>
public sealed class McpToolException(string message) : Exception(message);

/// <summary>
/// One MCP tool. Read tools need health:read; the one write tool (remember) needs memory:write.
/// A tool that reads someone's data lists the share category it reads so the service can say
/// plainly when it is not shared; the data services enforce the grant in the repository layer.
/// </summary>
public interface IMcpTool
{
  string Name { get; }
  string Description { get; }

  /// <summary>Only remember is false.</summary>
  bool ReadOnly { get; }

  /// <summary>Whether the tool takes the optional person argument (another household member's data).</summary>
  bool AcceptsPerson { get; }

  /// <summary>JSON Schema properties (without person, which the service adds) and the required names.</summary>
  IReadOnlyDictionary<string, McpParameter> Parameters { get; }

  Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct);
}

public sealed record McpParameter(string Type, string Description, bool Required = false, IReadOnlyList<string>? Enum = null);

/// <summary>Typed access to a tool call's arguments, with plain error messages for the model.</summary>
public sealed class McpArguments
{
  private readonly IReadOnlyDictionary<string, JsonElement> _values;

  public McpArguments(IReadOnlyDictionary<string, JsonElement>? values) =>
    _values = values ?? new Dictionary<string, JsonElement>();

  public string? String(string name)
  {
    if (!_values.TryGetValue(name, out var v)) return null;
    return v.ValueKind switch
    {
      JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString()!.Trim(),
      JsonValueKind.Number => v.GetRawText(),
      JsonValueKind.Null or JsonValueKind.Undefined => null,
      _ => throw new McpToolException($"{name} must be a string."),
    };
  }

  public string RequiredString(string name) => String(name) ?? throw new McpToolException($"{name} is required.");

  public int? Int(string name)
  {
    if (!_values.TryGetValue(name, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
    if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
    throw new McpToolException($"{name} must be a whole number.");
  }

  /// <summary>Dates are YYYY-MM-DD. "today" and "yesterday" are accepted too, in the caller's sense of UTC.</summary>
  public DateOnly? Date(string name, DateOnly today)
  {
    var raw = String(name);
    if (raw == null) return null;
    if (raw.Equals("today", StringComparison.OrdinalIgnoreCase)) return today;
    if (raw.Equals("yesterday", StringComparison.OrdinalIgnoreCase)) return today.AddDays(-1);
    if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
    if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)) return DateOnly.FromDateTime(dt);
    throw new McpToolException($"{name} must be a date like 2026-10-24.");
  }

  public DateOnly RequiredDate(string name, DateOnly today) => Date(name, today) ?? throw new McpToolException($"{name} is required.");
}

/// <summary>Formatting shared by the tools: rounded numbers, so the model repeats them as they are.</summary>
public static class McpFormat
{
  /// <summary>How the 1 to 6 mood reads, given beside every mood value.</summary>
  public const string MoodScale = "1 rough to 6 great";

  public static double Round(double value) => Math.Abs(value) >= 100 ? Math.Round(value, 0) : Math.Abs(value) >= 10 ? Math.Round(value, 1) : Math.Round(value, 2);

  public static double? Round(double? value) => value is { } v ? Round(v) : null;

  /// <summary>The metric key with or without its lab prefix, checked against the catalogue.</summary>
  public static string NormaliseMetricKey(string key) => key.Trim().ToLowerInvariant().Replace(' ', '_');

  public static string CategoryWord(ShareCategory category) => category.ToString().ToLowerInvariant();
}
