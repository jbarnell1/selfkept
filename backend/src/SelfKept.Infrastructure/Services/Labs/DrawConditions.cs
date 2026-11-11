using System.Globalization;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;

namespace SelfKept.Infrastructure.Services.Labs;

/// <summary>
/// The draw conditions on a panel (docs/labs-design.md, "Draw conditions"): validation, the
/// provider defaults, and the wire shape. Rythm asks for a morning draw after a fast of 8 or more
/// hours, so a Rythm panel is pre-filled that way and marked as defaulted until the person
/// changes it. Every other provider starts with nothing known.
/// </summary>
public static class DrawConditions
{
  /// <summary>The Rythm pre-fill: fasting, 8 hours (meaning 8 or more), a morning draw.</summary>
  public const short RythmFastingHours = 8;

  public const int MaxFastingHours = 72;

  /// <summary>
  /// Applies a create request's conditions to a new report. With nothing given, a Rythm panel
  /// takes the Rythm defaults; fields given override the defaults one by one.
  /// </summary>
  public static void ApplyOnCreate(LabReport report, DrawConditionsInput? input)
  {
    var given = Parse(input);
    var isRythm = report.Provider == LabProviders.Rythm;
    report.Fasting = given.Fasting ?? (isRythm ? FastingStates.Yes : null);
    report.FastingHours = given.FastingHours ?? (isRythm && (given.Fasting ?? FastingStates.Yes) == FastingStates.Yes ? RythmFastingHours : null);
    report.DrawTime = given.DrawTime;
    report.DrawPeriod = given.DrawPeriod ?? (isRythm ? DrawPeriods.Morning : null);
    report.HardTraining = given.HardTraining;
    report.Unwell = given.Unwell;
    report.ConditionsDefaulted = isRythm && given.IsEmpty;
  }

  /// <summary>Replaces every condition with what the person gave. Nothing is defaulted after an edit.</summary>
  public static void Replace(LabReport report, DrawConditionsInput? input)
  {
    var given = Parse(input);
    report.Fasting = given.Fasting;
    report.FastingHours = given.FastingHours;
    report.DrawTime = given.DrawTime;
    report.DrawPeriod = given.DrawPeriod;
    report.HardTraining = given.HardTraining;
    report.Unwell = given.Unwell;
    report.ConditionsDefaulted = false;
  }

  public static DrawConditionsDto ToDto(LabReport report) => new(
    report.Fasting ?? FastingStates.Unknown,
    report.FastingHours,
    report.DrawTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
    report.DrawPeriod ?? DrawPeriods.Unknown,
    report.HardTraining,
    report.Unwell,
    report.ConditionsDefaulted);

  /// <summary>The report's conditions as the rules and the review read them.</summary>
  public static PanelConditions Of(LabReport report) =>
    new(report.Fasting ?? FastingStates.Unknown, report.FastingHours, report.DrawPeriod ?? DrawPeriods.Unknown, report.HardTraining, report.Unwell);

  private sealed record Parsed(string? Fasting, short? FastingHours, TimeOnly? DrawTime, string? DrawPeriod, bool? HardTraining, bool? Unwell)
  {
    public bool IsEmpty => Fasting == null && FastingHours == null && DrawTime == null && DrawPeriod == null && HardTraining == null && Unwell == null;
  }

  private static Parsed Parse(DrawConditionsInput? input)
  {
    if (input == null) return new Parsed(null, null, null, null, null, null);

    string? fasting = null;
    if (!string.IsNullOrWhiteSpace(input.Fasting))
    {
      fasting = input.Fasting.Trim().ToLowerInvariant();
      if (!FastingStates.All.Contains(fasting)) throw new DomainValidationException($"fasting must be one of {string.Join(", ", FastingStates.All)}.");
    }

    short? hours = null;
    if (input.FastingHours is { } h)
    {
      if (h < 0 || h > MaxFastingHours) throw new DomainValidationException($"fastingHours must be between 0 and {MaxFastingHours}.");
      if (fasting == FastingStates.No) throw new DomainValidationException("fastingHours goes with fasting \"yes\" or \"unknown\", not \"no\".");
      hours = (short)h;
    }

    TimeOnly? time = null;
    if (!string.IsNullOrWhiteSpace(input.DrawTime))
    {
      if (!TimeOnly.TryParseExact(input.DrawTime.Trim(), ["HH:mm", "HH:mm:ss", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
      {
        throw new DomainValidationException("drawTime must be a local time of day as HH:mm.");
      }
      time = new TimeOnly(parsed.Hour, parsed.Minute);
    }

    string? period = null;
    if (!string.IsNullOrWhiteSpace(input.DrawPeriod))
    {
      period = input.DrawPeriod.Trim().ToLowerInvariant();
      if (!DrawPeriods.All.Contains(period)) throw new DomainValidationException($"drawPeriod must be one of {string.Join(", ", DrawPeriods.All)}.");
    }
    if (time is { } t) period = DrawPeriods.For(t);

    return new Parsed(fasting, hours, time, period, input.HardTraining, input.Unwell);
  }
}
