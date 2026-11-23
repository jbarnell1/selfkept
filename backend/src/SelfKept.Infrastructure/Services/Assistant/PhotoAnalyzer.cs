using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Body;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Assistant;

/// <summary>
/// Reads one photo with the home model (docs/photo-design.md). A sort call says what kind of
/// photo it is (skipped when the person named the kind), a read call per kind asks for that
/// kind's JSON, and code checks every answer before it becomes a proposal: ranges, units, tags,
/// lengths and the guard's claim rules on the description. The model never decides what is saved.
/// The photo is held in memory for the request only: it is never logged, stored or written to
/// ai_call, which gets one row per model call with latency and the verdict and never any text.
/// </summary>
public sealed partial class PhotoAnalyzer : IPhotoAnalyzer
{
  public const int MaxBytes = 6 * 1024 * 1024;
  public const int MaxNoteLength = 300;
  public const int MaxDescriptionLength = 300;
  public const int MaxMealTextLength = 200;
  public const int MaxLabelTextLength = 80;
  public const int MaxLabLines = 200;
  public const int MaxLabLineLength = 200;

  public const string FallbackDescription = "A photo.";
  public const string NotOn = "Photos are not turned on for this server.";
  public const string Unreadable = "The photo could not be read. Try a closer, sharper picture.";
  public const string NotAnswering = "The home model is not answering, so the photo could not be read. Nothing was saved.";

  /// <summary>The servings on a label: 0.25 to 200.</summary>
  public static readonly (double Min, double Max) ServingsRange = (0.25, 200);

  public const string SortPrompt =
    "Look at the photo and answer with JSON only: {\"kind\": one of \"meal\", \"nutrition_label\", \"medicine_label\", \"device_display\", \"lab_report\", \"other\", " +
    "\"description\": one or two plain sentences saying what is visible}. Do not name any illness.";

  public static readonly string MealPrompt =
    "You look at a photo of a meal. Answer with JSON only: {\"text\": a short plain list of the food you can see, " +
    "\"tags\": a list of the words that fit, chosen only from " + string.Join(", ", JournalTags.Food.Select(t => "\"" + t + "\"")) + ", " +
    "\"portion\": \"small\", \"usual\" or \"large\", \"energy_kcal\": number, \"protein_g\": number, \"carbs_g\": number, \"fat_g\": number}. " +
    "The four numbers are your best guess for everything on the plate. Use null for anything you cannot tell.";

  public const string LabelPrompt =
    "You read nutrition labels. Answer with JSON only: {\"product\": the product name or null, \"serving_size\": the serving size as printed or null, " +
    "\"servings_per_container\": number or null, \"energy_kcal\": number, \"protein_g\": number, \"carbs_g\": number, \"fat_g\": number}. " +
    "The numbers are per serving, as printed. Use null for anything you cannot read.";

  public const string MedicinePrompt =
    "You read medicine and supplement labels. Answer with JSON only: {\"kind\": \"medication\" or \"supplement\", \"brand\": the brand or null, " +
    "\"product\": the product name, \"dose\": the dose as printed or null, \"ingredients\": [{\"name\": text, \"amount\": number or null, \"unit\": text or null}]}. " +
    "Copy only what is printed. Use null for anything you cannot read.";

  public const string DisplayPrompt =
    "You read the screen of a home health device. Answer with JSON only: {\"readings\": [{\"what\": one of \"weight\", \"body_fat\", " +
    "\"blood_pressure_systolic\", \"blood_pressure_diastolic\", \"pulse\", \"temperature\", \"blood_glucose\", \"oxygen\", \"other\", " +
    "\"value\": number, \"unit\": the unit exactly as shown, or null}]}. Only numbers you can see.";

  public const string LabPrompt =
    "You read lab reports. Answer with JSON only: {\"lines\": [each result line as printed, with the test name, the value and the unit]}. " +
    "Copy the lines exactly and add nothing.";

  private const int SortTokens = 200;
  private const int ReadTokens = 500;
  private const int LabTokens = 1500;
  private const int MaxTags = 8;
  private const int MaxIngredients = 40;

  /// <summary>What the display prompt calls a reading, to the metric it lands on.</summary>
  private static readonly Dictionary<string, string> ReadingMetrics = new(StringComparer.Ordinal)
  {
    ["weight"] = MetricKeys.WeightKg,
    ["body_fat"] = MetricKeys.BodyFatPct,
    ["bodyfat"] = MetricKeys.BodyFatPct,
    ["fat"] = MetricKeys.BodyFatPct,
    ["blood_pressure_systolic"] = MetricKeys.BloodPressureSys,
    ["systolic"] = MetricKeys.BloodPressureSys,
    ["sys"] = MetricKeys.BloodPressureSys,
    ["blood_pressure_diastolic"] = MetricKeys.BloodPressureDia,
    ["diastolic"] = MetricKeys.BloodPressureDia,
    ["dia"] = MetricKeys.BloodPressureDia,
    ["pulse"] = MetricKeys.HeartRateBpm,
    ["heart_rate"] = MetricKeys.HeartRateBpm,
    ["temperature"] = MetricKeys.BodyTempC,
    ["body_temperature"] = MetricKeys.BodyTempC,
    ["blood_glucose"] = MetricKeys.BloodGlucoseMmol,
    ["glucose"] = MetricKeys.BloodGlucoseMmol,
    ["oxygen"] = MetricKeys.Spo2Pct,
    ["spo2"] = MetricKeys.Spo2Pct,
    ["blood_oxygen"] = MetricKeys.Spo2Pct,
  };

  /// <summary>
  /// The units read for each metric (normalised as UnitConversion does). The first is used when the
  /// model gives no unit and the metric has only one; a unit not listed drops the reading.
  /// </summary>
  private static readonly Dictionary<string, string[]> ReadingUnits = new(StringComparer.Ordinal)
  {
    [MetricKeys.WeightKg] = ["kg", "lb"],
    [MetricKeys.BodyFatPct] = ["%"],
    [MetricKeys.BloodPressureSys] = ["mmhg"],
    [MetricKeys.BloodPressureDia] = ["mmhg"],
    [MetricKeys.HeartRateBpm] = ["bpm"],
    [MetricKeys.BodyTempC] = ["c", "f"],
    // mg/dL only through the conversion UnitConversion already has for blood glucose.
    [MetricKeys.BloodGlucoseMmol] = ["mmol/l", "mg/dl"],
    [MetricKeys.Spo2Pct] = ["%"],
  };

  private readonly IChatModel _model;
  private readonly ILabService _labs;
  private readonly IMetricCatalogue _catalogue;
  private readonly IAiCallRepository _calls;
  private readonly ModelRewording _rewording;
  private readonly IAuditService _audit;
  private readonly ILogger<PhotoAnalyzer> _logger;

  public PhotoAnalyzer(
    IChatModel model,
    ILabService labs,
    IMetricCatalogue catalogue,
    IAiCallRepository calls,
    ModelRewording rewording,
    IAuditService audit,
    ILogger<PhotoAnalyzer> logger)
  {
    _model = model;
    _labs = labs;
    _catalogue = catalogue;
    _calls = calls;
    _rewording = rewording;
    _audit = audit;
    _logger = logger;
  }

  public bool Enabled => _model.VisionEnabled;

  public async Task<PhotoAnalysisDto> AnalyzeAsync(Actor actor, byte[]? image, string? kind, string? note, CancellationToken ct = default)
  {
    if (image == null || image.Length == 0) throw new DomainValidationException("Attach a photo.");
    if (image.Length > MaxBytes) throw new DomainValidationException("The photo is larger than 6 MB.");
    var mediaType = MediaTypeOf(image) ?? throw new DomainValidationException("That file is not a JPEG, PNG or WebP photo.");
    var requested = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim();
    if (requested != null && !PhotoKinds.IsKnown(requested)) throw new DomainValidationException(PhotoKinds.KindMessage);
    var said = string.IsNullOrWhiteSpace(note) ? null : Regex.Replace(note, @"\s+", " ").Trim();
    if (said is { Length: > MaxNoteLength }) throw new DomainValidationException($"A note is at most {MaxNoteLength} characters.");
    if (!Enabled) throw new ConflictException(NotOn);

    var photo = new ChatImage(mediaType, image);
    var userText = said == null ? "Here is the photo." : "Here is the photo. The person says: " + said;

    string? description = null;
    string chosen;
    if (requested != null) chosen = requested;
    else (chosen, description) = await SortAsync(actor, photo, userText, ct);

    var result = chosen == PhotoKinds.Other ? null : await ReadAsync(actor, chosen, photo, userText, ct);
    if (result == null)
    {
      // Other, or a kind whose read came back empty: the description is the answer.
      description ??= (await SortAsync(actor, photo, userText, ct)).Description;
      result = new PhotoAnalysisDto(PhotoKinds.Other, description);
    }
    else
    {
      result = result with { Description = description ?? DefaultDescription(chosen) };
    }

    await _audit.RecordAsync(new AuditEvent(AssistantAuditActions.PhotoAnalyzed, actor.UserId, actor.HouseholdId,
      "assistant_photo", null, new { kind = result.Kind }, actor.Info.IpAddress), ct);
    return result;
  }

  /// <summary>JPEG, PNG or WebP told by the first bytes, as a media type. Null for anything else.</summary>
  public static string? MediaTypeOf(ReadOnlySpan<byte> bytes)
  {
    if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
    if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
    if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
    return null;
  }

  // --- the calls ---

  private async Task<(string Kind, string Description)> SortAsync(Actor actor, ChatImage photo, string userText, CancellationToken ct)
  {
    var root = await AskAsync(actor, AssistantAiPurposes.PhotoSort, SortPrompt, userText, photo, SortTokens, ct);
    var kind = Text(root, 40, "kind")?.ToLowerInvariant().Replace(' ', '_');
    return (PhotoKinds.IsKnown(kind) ? kind! : PhotoKinds.Other, CleanDescription(Text(root, 2000, "description")));
  }

  /// <summary>The proposal for one kind, or null when the read came back empty.</summary>
  private async Task<PhotoAnalysisDto?> ReadAsync(Actor actor, string kind, ChatImage photo, string userText, CancellationToken ct)
  {
    switch (kind)
    {
      case PhotoKinds.Meal:
        return Meal(await AskAsync(actor, AssistantAiPurposes.PhotoRead, MealPrompt, userText, photo, ReadTokens, ct)) is { } meal
          ? new PhotoAnalysisDto(kind, string.Empty, Meal: meal) : null;
      case PhotoKinds.NutritionLabel:
        return Label(await AskAsync(actor, AssistantAiPurposes.PhotoRead, LabelPrompt, userText, photo, ReadTokens, ct)) is { } label
          ? new PhotoAnalysisDto(kind, string.Empty, Label: label) : null;
      case PhotoKinds.MedicineLabel:
        return Medicine(await AskAsync(actor, AssistantAiPurposes.PhotoRead, MedicinePrompt, userText, photo, ReadTokens, ct)) is { } medicine
          ? new PhotoAnalysisDto(kind, string.Empty, Medicine: medicine) : null;
      case PhotoKinds.DeviceDisplay:
        var readings = Readings(await AskAsync(actor, AssistantAiPurposes.PhotoRead, DisplayPrompt, userText, photo, ReadTokens, ct));
        return readings.Count > 0 ? new PhotoAnalysisDto(kind, string.Empty, Readings: readings) : null;
      case PhotoKinds.LabReport:
        var lines = LabLines(await AskAsync(actor, AssistantAiPurposes.PhotoRead, LabPrompt, userText, photo, LabTokens, ct));
        if (lines.Count == 0) return null;
        var parsed = _labs.Parse(string.Join("\n", lines));
        return parsed.Candidates.Count > 0 ? new PhotoAnalysisDto(kind, string.Empty, Lab: parsed) : null;
      default:
        return null;
    }
  }

  /// <summary>
  /// One photo call, asked once more when the answer is not usable JSON. Every attempt is an
  /// ai_call row with its latency and verdict; the photo and the text never are.
  /// </summary>
  private async Task<JsonElement> AskAsync(Actor actor, string purpose, string system, string userText, ChatImage photo, int maxTokens, CancellationToken ct)
  {
    var turns = new List<ChatTurn>
    {
      new(ChatTurn.System, system),
      new(ChatTurn.User, userText, [photo]),
    };
    var options = new ChatOptions(Temperature: 0.1, MaxTokens: maxTokens, Background: false, JsonMode: true, Vision: true);
    for (var attempt = 0; attempt < 2; attempt++)
    {
      var call = _rewording.NewCall(actor.PersonId, purpose, system + "\n" + userText);
      var watch = Stopwatch.StartNew();
      try
      {
        var completion = await _model.CompleteAsync(turns, options, ct);
        call.PromptTokens = completion.PromptTokens;
        call.CompletionTokens = completion.CompletionTokens;
        if (ExtractJson(completion.Content) is { } root)
        {
          call.Ok = true;
          return root;
        }
        call.Ok = false;
        call.Error = "not usable JSON";
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        call.Ok = false;
        call.Error = "cancelled";
        throw;
      }
      catch (Exception ex)
      {
        call.Ok = false;
        call.Error = ModelRewording.Describe(ex);
        _logger.LogWarning("Photo call for {Purpose} failed: {Error}", purpose, call.Error);
        throw new ServiceUnavailableException(NotAnswering);
      }
      finally
      {
        call.LatencyMs = (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds);
        await _calls.RecordAsync(call, CancellationToken.None);
      }
    }
    throw new UnprocessableException(Unreadable);
  }

  // --- reading the answers ---

  /// <summary>
  /// The JSON object in a model's answer: inside a code fence when there is one, else from the
  /// first brace to the last. Null when there is no usable object.
  /// </summary>
  public static JsonElement? ExtractJson(string? content)
  {
    if (string.IsNullOrWhiteSpace(content)) return null;
    var text = ThinkBlock().Replace(content, string.Empty).Trim();
    if (Fence().Match(text) is { Success: true } fence) text = fence.Groups["body"].Value.Trim();
    var start = text.IndexOf('{');
    var end = text.LastIndexOf('}');
    if (start < 0 || end <= start) return null;
    try
    {
      using var doc = JsonDocument.Parse(text[start..(end + 1)]);
      return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  public PhotoMealDto? Meal(JsonElement root)
  {
    var text = Text(root, MaxMealTextLength, "text", "food", "items", "dish");
    if (text == null) return null;
    var portion = Text(root, 16, "portion")?.ToLowerInvariant();
    return new PhotoMealDto(
      text,
      FoodTags(Find(root, "tags")),
      portion != null && Portions.All.Contains(portion) ? portion : null,
      Ranged(Number(root, "energy_kcal", "energy", "kcal", "calories"), NutritionSamples.Energy),
      Ranged(Number(root, "protein_g", "protein"), NutritionSamples.Protein),
      Ranged(Number(root, "carbs_g", "carbs", "carbohydrate", "carbohydrates", "total_carbohydrate"), NutritionSamples.Carbs),
      Ranged(Number(root, "fat_g", "fat", "total_fat"), NutritionSamples.Fat));
  }

  public static PhotoLabelDto? Label(JsonElement root)
  {
    var servings = Number(root, "servings_per_container", "servings");
    var label = new PhotoLabelDto(
      Text(root, MaxLabelTextLength, "product", "name"),
      Text(root, MaxLabelTextLength, "serving_size"),
      servings is { } s && s >= ServingsRange.Min && s <= ServingsRange.Max ? s : null,
      Ranged(Number(root, "energy_kcal", "energy", "kcal", "calories"), NutritionSamples.Energy),
      Ranged(Number(root, "protein_g", "protein"), NutritionSamples.Protein),
      Ranged(Number(root, "carbs_g", "carbs", "carbohydrate", "carbohydrates", "total_carbohydrate"), NutritionSamples.Carbs),
      Ranged(Number(root, "fat_g", "fat", "total_fat"), NutritionSamples.Fat));
    var empty = label.Product == null && label.EnergyKcal == null && label.ProteinG == null && label.CarbsG == null && label.FatG == null;
    return empty ? null : label;
  }

  public static PhotoMedicineDto? Medicine(JsonElement root)
  {
    var product = Text(root, MedicationService.MaxNameLength, "product", "name");
    if (product == null) return null;
    var kind = Text(root, 16, "kind")?.ToLowerInvariant() is "medication" or "medicine" or "drug" ? MedicationKinds.Medication : MedicationKinds.Supplement;
    var ingredients = new List<MedicationIngredientDto>();
    if (Find(root, "ingredients") is { ValueKind: JsonValueKind.Array } list)
    {
      foreach (var item in list.EnumerateArray())
      {
        if (ingredients.Count >= MaxIngredients) break;
        if (item.ValueKind == JsonValueKind.String)
        {
          if (Clean(item.GetString(), MedicationService.MaxNameLength) is { } plain) ingredients.Add(new MedicationIngredientDto(plain, null, null));
          continue;
        }
        if (item.ValueKind != JsonValueKind.Object || Text(item, MedicationService.MaxNameLength, "name") is not { } name) continue;
        var amount = Number(item, "amount");
        ingredients.Add(new MedicationIngredientDto(
          name,
          amount is { } a && a > 0 && a < 1_000_000 ? a : null,
          Text(item, MedicationService.MaxUnitLength, "unit")));
      }
    }
    return new PhotoMedicineDto(kind, Text(root, MedicationService.MaxNameLength, "brand"), product, Text(root, 80, "dose"), ingredients);
  }

  /// <summary>The readings that map to a metric, in a listed unit and inside the typed-in range, converted to the metric's unit. One per metric.</summary>
  public IReadOnlyList<PhotoReadingDto> Readings(JsonElement root)
  {
    var result = new List<PhotoReadingDto>();
    if (Find(root, "readings") is not { ValueKind: JsonValueKind.Array } list) return result;
    foreach (var item in list.EnumerateArray())
    {
      if (item.ValueKind != JsonValueKind.Object) continue;
      var what = Text(item, 40, "what", "name", "type")?.ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
      if (what == null || !ReadingMetrics.TryGetValue(what, out var key)) continue;
      if (result.Any(r => r.MetricKey == key)) continue;
      if (Number(item, "value") is not { } value) continue;
      if (_catalogue.Find(key) is not { } metric) continue;

      var units = ReadingUnits[key];
      var shown = Text(item, 16, "unit");
      var unit = shown == null ? (units.Length == 1 ? units[0] : null) : ReadingUnit(shown);
      if (unit == null || !units.Contains(unit)) continue;
      if (UnitConversion.Convert(metric, unit, value) is not { } converted) continue;
      converted = Math.Round(converted, 2, MidpointRounding.AwayFromZero);
      var (min, max) = MeasurementService.Allowed[key];
      if (converted < min || converted > max) continue;
      result.Add(new PhotoReadingDto(key, converted, metric.Unit, metric.DisplayName));
    }
    return result;
  }

  public static IReadOnlyList<string> LabLines(JsonElement root)
  {
    var lines = new List<string>();
    if (Find(root, "lines") is not { ValueKind: JsonValueKind.Array } list) return lines;
    foreach (var item in list.EnumerateArray())
    {
      if (lines.Count >= MaxLabLines) break;
      var raw = item.ValueKind switch
      {
        JsonValueKind.String => item.GetString(),
        // A model that writes {name, value, unit} objects instead of lines: the values in order.
        JsonValueKind.Object => string.Join(" ", item.EnumerateObject().Select(p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetRawText() : null).Where(v => !string.IsNullOrWhiteSpace(v))),
        _ => null,
      };
      if (Clean(raw, MaxLabLineLength) is { } line) lines.Add(line);
    }
    return lines;
  }

  /// <summary>
  /// One or two sentences, at most 300 characters, that pass the guard's diagnosis, detection and
  /// cause rules. Anything else becomes "A photo.".
  /// </summary>
  public static string CleanDescription(string? text)
  {
    var clean = Clean(text, 2000);
    if (clean == null) return FallbackDescription;
    var sentences = SentenceEnd().Split(clean).Where(s => s.Length > 0).Take(2);
    clean = string.Join(" ", sentences);
    if (clean.Length > MaxDescriptionLength) clean = CutAtWord(clean, MaxDescriptionLength);
    if (ReplyGuard.ClaimBreak(clean.ToLowerInvariant()) != null) return FallbackDescription;
    return clean;
  }

  public static string DefaultDescription(string kind) => kind switch
  {
    PhotoKinds.Meal => "A photo of a meal.",
    PhotoKinds.NutritionLabel => "A photo of a nutrition label.",
    PhotoKinds.MedicineLabel => "A photo of a medicine label.",
    PhotoKinds.DeviceDisplay => "A photo of a device display.",
    PhotoKinds.LabReport => "A photo of a lab report.",
    _ => FallbackDescription,
  };

  // --- helpers ---

  private static string? ReadingUnit(string shown)
  {
    var unit = UnitConversion.Normalise(shown);
    return unit switch
    {
      "/min" or "beats/min" or "pul/min" or "pulse/min" or "bpm" => "bpm",
      "kgs" => "kg",
      "°" => null,
      _ => unit,
    };
  }

  private static IReadOnlyList<string> FoodTags(JsonElement? tags)
  {
    var result = new List<string>();
    if (tags is not { ValueKind: JsonValueKind.Array } list) return result;
    foreach (var item in list.EnumerateArray())
    {
      if (item.ValueKind != JsonValueKind.String) continue;
      var tag = item.GetString()?.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
      if (tag != null && JournalTags.Food.Contains(tag) && !result.Contains(tag)) result.Add(tag);
      if (result.Count >= MaxTags) break;
    }
    return result;
  }

  private static double? Ranged(double? value, NutritionSamples.Field field) =>
    value is { } v && NutritionSamples.InRange(field, v) ? Math.Round(v, 1, MidpointRounding.AwayFromZero) : null;

  /// <summary>A property by any of its names, ignoring case, underscores and spaces.</summary>
  private static JsonElement? Find(JsonElement obj, params string[] names)
  {
    if (obj.ValueKind != JsonValueKind.Object) return null;
    foreach (var name in names)
    {
      var wanted = Key(name);
      foreach (var p in obj.EnumerateObject())
      {
        if (Key(p.Name) == wanted) return p.Value;
      }
    }
    return null;
  }

  private static string Key(string name) => name.ToLowerInvariant().Replace("_", string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);

  /// <summary>A number, or a string that starts with one ("8 g", "1,200"). Null when there is none.</summary>
  private static double? Number(JsonElement obj, params string[] names)
  {
    if (Find(obj, names) is not { } value) return null;
    if (value.ValueKind == JsonValueKind.Number) return value.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
    if (value.ValueKind != JsonValueKind.String) return null;
    var text = Thousands().Replace(value.GetString() ?? string.Empty, string.Empty);
    var m = LeadingNumber().Match(text);
    if (!m.Success) return null;
    return double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) ? parsed : null;
  }

  /// <summary>Text trimmed and cut to its limit; a list of strings is joined with commas. Null when empty.</summary>
  private static string? Text(JsonElement obj, int max, params string[] names)
  {
    if (Find(obj, names) is not { } value) return null;
    return value.ValueKind switch
    {
      JsonValueKind.String => Clean(value.GetString(), max),
      JsonValueKind.Number => Clean(value.GetRawText(), max),
      JsonValueKind.Array => Clean(string.Join(", ", value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString())), max),
      _ => null,
    };
  }

  private static string? Clean(string? text, int max)
  {
    var clean = ReplyGuard.Sanitise(text);
    clean = Regex.Replace(clean, @"\s+", " ").Trim();
    if (clean.Length == 0 || clean.Equals("null", StringComparison.OrdinalIgnoreCase) || clean.Equals("unknown", StringComparison.OrdinalIgnoreCase)) return null;
    return clean.Length > max ? CutAtWord(clean, max) : clean;
  }

  private static string CutAtWord(string text, int max)
  {
    var cut = text[..max];
    var space = cut.LastIndexOf(' ');
    return (space > max / 2 ? cut[..space] : cut).TrimEnd(' ', ',', ';', ':');
  }

  [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
  private static partial Regex ThinkBlock();

  [GeneratedRegex(@"```[a-zA-Z]*\s*(?<body>.*?)```", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
  private static partial Regex Fence();

  [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant)]
  private static partial Regex SentenceEnd();

  [GeneratedRegex(@"(?<=\d),(?=\d{3}(?!\d))", RegexOptions.CultureInvariant)]
  private static partial Regex Thousands();

  [GeneratedRegex(@"^\s*-?\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant)]
  private static partial Regex LeadingNumber();
}
