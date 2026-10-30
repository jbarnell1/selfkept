using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Mcp;

/// <summary>
/// What /mcp exposes and how every call is checked (docs/connect-design.md). Kept apart from the
/// SDK wiring so it is testable without an HTTP host.
/// <list type="bullet">
///   <item>The caller comes only from the validated token; no argument names the caller.</item>
///   <item>Read tools need health:read; remember (the only write) needs memory:write.</item>
///   <item>The optional person argument resolves a household member by name; their data is read
///   through the same services as the app, so the share grant is enforced in the repository layer.
///   A tool that cannot read says so plainly instead of returning an empty answer.</item>
///   <item>Every call writes one audit row (tool, client, whose data, row count, outcome; never
///   values). Failures reach the model as isError results with a short message, never a trace.</item>
/// </list>
/// </summary>
public sealed class McpToolService
{
  /// <summary>Larger results are refused with a hint to narrow the window, so a prompt never carries a data dump.</summary>
  public const int MaxResultCharacters = 60_000;

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
  };

  private readonly IReadOnlyDictionary<string, IMcpTool> _tools;
  private readonly IHouseholdService _households;
  private readonly IAuditService _audit;
  private readonly ILogger<McpToolService> _logger;

  public McpToolService(IEnumerable<IMcpTool> tools, IHouseholdService households, IAuditService audit, ILogger<McpToolService> logger)
  {
    _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
    _households = households;
    _audit = audit;
    _logger = logger;
  }

  public IReadOnlyCollection<IMcpTool> Tools => _tools.Values.ToList();

  public ListToolsResult ListTools() => new()
  {
    Tools = _tools.Values
      .OrderBy(t => t.ReadOnly ? 0 : 1)
      .ThenBy(t => t.Name, StringComparer.Ordinal)
      .Select(t => new Tool
      {
        Name = t.Name,
        Description = t.Description,
        InputSchema = Schema(t),
        Annotations = new ToolAnnotations { ReadOnlyHint = t.ReadOnly, DestructiveHint = false, OpenWorldHint = false },
      })
      .ToList(),
  };

  public static JsonElement Schema(IMcpTool tool)
  {
    var properties = new Dictionary<string, object>();
    var required = new List<string>();
    foreach (var (name, p) in tool.Parameters)
    {
      properties[name] = p.Enum is { Count: > 0 }
        ? new { type = p.Type, description = p.Description, @enum = p.Enum }
        : new { type = p.Type, description = p.Description };
      if (p.Required) required.Add(name);
    }
    if (tool.AcceptsPerson)
    {
      properties["person"] = new
      {
        type = "string",
        description = "A household member's name, to read data they have shared with the caller. Leave out to read the caller's own data.",
      };
    }
    object schema = required.Count > 0
      ? new { type = "object", properties, required }
      : new { type = "object", properties };
    return JsonSerializer.SerializeToElement(schema);
  }

  public async Task<CallToolResult> CallToolAsync(ClaimsPrincipal? user, CallToolRequestParams? request, string? ipAddress, CancellationToken ct = default)
  {
    var caller = McpCaller.From(user);
    if (caller == null)
      return Error("Not authenticated.");

    var name = request?.Name ?? string.Empty;
    if (!_tools.TryGetValue(name, out var tool))
      return Error($"Unknown tool: {name}.");

    var needed = tool.ReadOnly ? ConnectScopes.HealthRead : ConnectScopes.MemoryWrite;
    if (!caller.Has(needed))
      return Error($"This connection was not granted {needed}. Reconnect the assistant and allow it.");

    var arguments = new McpArguments(request?.Arguments is { } raw ? new Dictionary<string, JsonElement>(raw) : null);
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    McpTarget? target = null;
    var rows = 0;
    string? failure = null;
    CallToolResult result;
    try
    {
      target = await ResolveTargetAsync(caller, tool, arguments, ct);
      var output = await tool.ExecuteAsync(new McpToolContext(caller, target), arguments, ct);
      rows = output.Rows;
      var json = JsonSerializer.Serialize(output.Data, JsonOptions);
      if (json.Length > MaxResultCharacters)
        throw new McpToolException("That answer is too large. Ask for a shorter date range.");
      result = new CallToolResult { IsError = false, Content = [new TextContentBlock { Text = json }] };
    }
    catch (McpToolException ex)
    {
      failure = ex.Message;
      result = Error(ex.Message);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogError(ex, "MCP tool {Tool} failed for user {UserId}", name, caller.UserId);
      failure = "error";
      result = Error("The tool could not complete the request.");
    }

    var ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    _logger.LogInformation("MCP tools/call {Tool} by {Client} for {Target}: {Outcome} in {Ms} ms, {Rows} rows",
      name, caller.ClientName, target == null ? "unresolved" : target.IsSelf ? "self" : "member", failure == null ? "ok" : "refused", Math.Round(ms), rows);
    await _audit.RecordAsync(new AuditEvent(
      AuditActions.McpToolCall,
      caller.UserId,
      caller.HouseholdId,
      "mcp_tool",
      name,
      new
      {
        client = caller.ClientName,
        clientId = caller.ClientId,
        target = target == null ? null : target.IsSelf ? "self" : target.PersonId.ToString(),
        ok = failure == null,
        rows,
        reason = failure,
      },
      ipAddress), ct);
    return result;
  }

  private async Task<McpTarget> ResolveTargetAsync(McpCaller caller, IMcpTool tool, McpArguments arguments, CancellationToken ct)
  {
    var household = await _households.GetForUserAsync(caller.UserId, ct);
    var self = household?.Members.FirstOrDefault(m => m.PersonId == caller.PersonId);
    var selfTarget = new McpTarget(caller.PersonId, self?.DisplayName ?? "you", true);

    var personName = arguments.String("person");
    if (personName == null)
      return selfTarget;
    if (!tool.AcceptsPerson)
      throw new McpToolException($"{tool.Name} only reads the caller's own information; leave out person.");

    var members = household?.Members ?? [];
    var match = members.FirstOrDefault(m => string.Equals(m.DisplayName, personName, StringComparison.OrdinalIgnoreCase))
      ?? SingleOrNull(members.Where(m => FirstWord(m.DisplayName).Equals(FirstWord(personName), StringComparison.OrdinalIgnoreCase)));
    if (match == null)
      throw new McpToolException($"No household member is called {personName}. Members: {string.Join(", ", members.Select(m => m.DisplayName))}.");
    return match.PersonId == caller.PersonId ? selfTarget : new McpTarget(match.PersonId, match.DisplayName, false);
  }

  private static T? SingleOrNull<T>(IEnumerable<T> items) where T : class
  {
    var list = items.Take(2).ToList();
    return list.Count == 1 ? list[0] : null;
  }

  private static string FirstWord(string value) => value.Trim().Split(' ', 2)[0];

  private static CallToolResult Error(string message) => new()
  {
    IsError = true,
    Content = [new TextContentBlock { Text = message }],
  };
}
