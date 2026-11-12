using System.Text.Json.Serialization;

namespace SelfKept.Core.DTOs;

// Photos read by the home model (docs/photo-design.md). A proposal only: nothing here is saved
// until the person confirms it through the ordinary routes (day log, medicines, measurements, labs).

/// <summary>
/// POST /api/assistant/photos. Exactly one of Meal, Label, Medicine, Readings and Lab is present,
/// matching Kind; none for "other". Absent parts are left out of the JSON.
/// </summary>
public sealed record PhotoAnalysisDto(
  string Kind,
  string Description,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PhotoMealDto? Meal = null,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PhotoLabelDto? Label = null,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PhotoMedicineDto? Medicine = null,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PhotoReadingDto>? Readings = null,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ParseResultDto? Lab = null);

/// <summary>A meal: what is on the plate, day log food tags, a portion and the model's guess for the whole meal.</summary>
public sealed record PhotoMealDto(
  string Text,
  IReadOnlyList<string> Tags,
  string? Portion,
  double? EnergyKcal,
  double? ProteinG,
  double? CarbsG,
  double? FatG);

/// <summary>A nutrition label as printed: the numbers are per serving.</summary>
public sealed record PhotoLabelDto(
  string? Product,
  string? ServingSize,
  double? ServingsPerContainer,
  double? EnergyKcal,
  double? ProteinG,
  double? CarbsG,
  double? FatG);

/// <summary>A medicine or supplement label, shaped to fill the medicines form.</summary>
public sealed record PhotoMedicineDto(
  string Kind,
  string? Brand,
  string Product,
  string? Dose,
  IReadOnlyList<MedicationIngredientDto> Ingredients);

/// <summary>One number on a device display, already in the metric's unit. Label is the metric's display name.</summary>
public sealed record PhotoReadingDto(string MetricKey, double Value, string Unit, string Label);
