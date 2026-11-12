using System.Text.Json;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>What a medication row keeps encrypted: everything that names the product.</summary>
public sealed record MedicationDetail(
  string? Brand,
  string Product,
  string? Dose,
  string? ScheduleNote,
  IReadOnlyList<MedicationIngredientDto> Ingredients);

/// <summary>Entity to wire shape for the body package. Decryption happens here and nowhere else.</summary>
public static class BodyMapping
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static GoalDto ToDto(this Goal goal, IEncryptionService encryption) => new(
    goal.Id,
    goal.Slot,
    goal.Kind,
    Title(goal, encryption),
    goal.MetricKey,
    goal.StartValue,
    goal.TargetValue,
    goal.PacePerWeek,
    goal.StartedOn,
    goal.TargetDate,
    goal.Status,
    goal.EndedAt);

  /// <summary>The person's own title when they gave one, else the plain name of the kind.</summary>
  public static string Title(Goal goal, IEncryptionService encryption)
  {
    var own = goal.TitleCipher == null ? null : encryption.TryDecrypt(goal.TitleCipher);
    return string.IsNullOrWhiteSpace(own) ? GoalKinds.DisplayName(goal.Kind) : own;
  }

  public static TargetDto ToDto(this DailyTarget target, IMetricCatalogue catalogue) => new(
    target.Id,
    target.GoalId,
    target.Label,
    target.Kind,
    target.MetricKey,
    target.Tag,
    target.Comparator,
    target.Value,
    target.Period,
    target.Active,
    target.SortOrder,
    TargetTally.UnitFor(target, SummaryUnit(target.MetricKey, catalogue)));

  /// <summary>The unit of a metric's daily figure. Sleep stages are summed into minutes asleep.</summary>
  public static string? SummaryUnit(string? metricKey, IMetricCatalogue catalogue) => metricKey switch
  {
    null => null,
    MetricKeys.SleepStage => "min",
    _ => catalogue.Find(metricKey)?.Unit,
  };

  public static JournalEntryDto ToDto(this JournalEntry entry, IEncryptionService encryption) => new(
    entry.Id,
    entry.At,
    entry.Day,
    entry.Kind,
    entry.TextCipher == null ? null : encryption.TryDecrypt(CollectMapping.FromCipherBytes(entry.TextCipher)),
    entry.Tags,
    entry.Portion,
    entry.Source,
    entry.MessageId,
    entry.NutritionBasis == null ? null : new JournalNutritionDto(
      entry.NutritionBasis,
      (double?)entry.EnergyKcal,
      (double?)entry.ProteinG,
      (double?)entry.CarbsG,
      (double?)entry.FatG));

  public static MedicationDto ToDto(this Medication medication, IEncryptionService encryption, DateOnly today)
  {
    var detail = ReadDetail(medication, encryption);
    return new MedicationDto(
      medication.Id,
      medication.Kind,
      detail.Brand,
      detail.Product,
      detail.Dose,
      detail.ScheduleNote,
      medication.Generic,
      medication.Schedule,
      detail.Ingredients,
      medication.Substances,
      medication.StartedOn,
      medication.StoppedOn,
      medication.ActiveOn(today));
  }

  public static string WriteDetail(MedicationDetail detail, IEncryptionService encryption) =>
    encryption.Encrypt(JsonSerializer.Serialize(detail, Json));

  /// <summary>A row whose detail cannot be read (a rotated key) still lists, as an unnamed item.</summary>
  public static MedicationDetail ReadDetail(Medication medication, IEncryptionService encryption)
  {
    var plain = encryption.TryDecrypt(medication.DetailCipher);
    if (plain != null)
    {
      try
      {
        var detail = JsonSerializer.Deserialize<MedicationDetail>(plain, Json);
        if (detail != null) return detail with { Ingredients = detail.Ingredients ?? [] };
      }
      catch (JsonException)
      {
        // Falls through to the unnamed item below.
      }
    }
    return new MedicationDetail(null, "Unreadable item", null, null, []);
  }

  public static StrengthSetDto ToDto(this StrengthSet set) => new(
    set.Id,
    set.Day,
    set.Lift,
    Lifts.DisplayName(set.Lift),
    set.WeightKg,
    set.Reps,
    set.Rpe,
    StrengthMath.EstimatedMaxKg(set.WeightKg, set.Reps));
}
