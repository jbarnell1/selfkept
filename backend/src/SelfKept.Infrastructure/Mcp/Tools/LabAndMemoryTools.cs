using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Mcp.Tools;

public sealed class GetLabMarkersTool(ILabService labs) : IMcpTool
{
  public string Name => "get_lab_markers";
  public string Description =>
    "The latest blood panel result for each lab marker with the date drawn, the previous value, the population range for the person's sex, where the latest value sits (below, inside, above) and how it is flagged (watch, or clinician meaning worth raising with a clinician). Markers with no result are left out. Also gives the most recent panel date.";
  public bool ReadOnly => true;
  public bool AcceptsPerson => true;
  public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>();

  public async Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
  {
    var markers = await labs.GetMarkersAsync(context.Caller.PersonId, context.Target.PersonId, ct) ?? throw NotShared.For(context.Target, "lab results");
    var withValues = markers.Where(m => m.LatestValue != null && m.Applicable).ToList();
    var rows = withValues.Select(m => new
    {
      marker = m.MetricKey,
      name = m.DisplayName,
      group = m.Group,
      unit = m.Unit,
      latest = m.LatestValue,
      drawnOn = m.LatestDate,
      previous = m.PreviousValue,
      previousDrawnOn = m.PreviousDate,
      usualRange = m.PopulationRange,
      optimalRange = m.OptimalRange,
      position = m.Position,
      flag = m.Severity,
    }).ToList();
    var lastPanel = withValues.Select(m => m.LatestDate).Where(d => d != null).DefaultIfEmpty().Max();
    return new McpToolOutput(new
    {
      person = context.Target.DisplayName,
      lastPanelDate = lastPanel,
      note = rows.Count == 0 ? "No lab results yet." : "Ranges are approximate adult population ranges; the lab's own range on the report wins where they differ.",
      markers = rows,
    }, rows.Count);
  }
}

public sealed class GetLabHistoryTool(ILabService labs, IMetricCatalogue catalogue) : IMcpTool
{
  public string Name => "get_lab_history";
  public string Description =>
    "Every result for one lab marker with dates, plus what the marker is, what tends to move it, the population range for the person's sex and where that range comes from.";
  public bool ReadOnly => true;
  public bool AcceptsPerson => true;
  public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>
  {
    ["marker"] = new("string", "Marker key, for example lab.apob, lab.hscrp, lab.ferritin, lab.tsh (the lab. prefix is optional).", Required: true),
  };

  public async Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
  {
    var key = McpFormat.NormaliseMetricKey(arguments.RequiredString("marker"));
    if (!key.StartsWith("lab.", StringComparison.Ordinal)) key = "lab." + key;
    if (catalogue.Find(key) == null)
      throw new McpToolException($"Unknown marker {key}. Use get_lab_markers for the keys.");
    var detail = await labs.GetMarkerAsync(context.Caller.PersonId, context.Target.PersonId, key, ct)
      ?? throw NotShared.For(context.Target, "lab results");
    var m = detail.Marker;
    return new McpToolOutput(new
    {
      person = context.Target.DisplayName,
      marker = m.MetricKey,
      name = m.DisplayName,
      unit = m.Unit,
      about = detail.Blurb,
      whatMovesIt = detail.WhatMovesIt,
      usualRange = m.PopulationRange,
      optimalRange = m.OptimalRange,
      rangeSource = detail.SourceUrl,
      history = detail.History.Select(p => new { drawnOn = p.Date, value = p.Value, labFlag = p.Flag == "none" ? null : p.Flag, p.Derived }),
    }, detail.History.Count);
  }
}

public sealed class RecallMemoryTool(IAiMemoryService memory) : IMcpTool
{
  public string Name => "recall_memory";
  public string Description =>
    "What the person has asked assistants to remember about them: goals, conditions, medications, preferences, life events and household details. Call it at the start of a conversation about their health. topic narrows the list.";
  public bool ReadOnly => true;
  public bool AcceptsPerson => false;
  public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>
  {
    ["topic"] = new("string", "Only items mentioning this word, for example sleep or medication."),
  };

  public async Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
  {
    var items = await memory.RecallAsync(context.Caller.PersonId, arguments.String("topic"), ct);
    var rows = items.Select(m => new { m.Type, m.Key, m.Value, m.DisplayText, rememberedBy = m.Source, m.UpdatedAt }).ToList();
    return new McpToolOutput(new { memories = rows }, rows.Count);
  }
}

public sealed class RememberTool(IAiMemoryService memory) : IMcpTool
{
  public string Name => "remember";
  public string Description =>
    "Stores one durable fact the person told you so every connected assistant and the app can recall it: a goal, a condition, a medication, a preference, a life event or a household detail. The same type and key replaces the old value. It stores a note only; it never changes health data. Only remember what the person said, never a guess.";
  public bool ReadOnly => false;
  public bool AcceptsPerson => false;
  public IReadOnlyDictionary<string, McpParameter> Parameters { get; } = new Dictionary<string, McpParameter>
  {
    ["type"] = new("string", "What kind of fact.", Required: true, Enum: AiMemoryTypes.All),
    ["key"] = new("string", "A short stable name for the fact, for example sleep_goal or sertraline.", Required: true),
    ["value"] = new("string", "The fact in the person's words, up to 1000 characters.", Required: true),
    ["display_text"] = new("string", "One short line the app shows for it, optional."),
  };

  public async Task<McpToolOutput> ExecuteAsync(McpToolContext context, McpArguments arguments, CancellationToken ct)
  {
    try
    {
      var saved = await memory.RememberAsync(
        context.Caller.PersonId,
        arguments.RequiredString("type"),
        arguments.RequiredString("key"),
        arguments.RequiredString("value"),
        arguments.String("display_text"),
        context.Caller.ClientName,
        ct);
      return new McpToolOutput(new { remembered = true, saved.Type, saved.Key, saved.UpdatedAt }, 1);
    }
    catch (DomainValidationException ex)
    {
      throw new McpToolException(ex.Message);
    }
  }
}
