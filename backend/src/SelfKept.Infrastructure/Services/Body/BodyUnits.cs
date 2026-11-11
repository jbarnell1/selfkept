using System.Globalization;
using SelfKept.Core.Enums;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// Turns a stored value into words for the person reading. Everything is stored metric; a
/// sentence the server builds is written in the reader's own unit system, so someone who thinks
/// in pounds never has to convert a figure in their head.
/// </summary>
public static class BodyUnits
{
  public const double LbPerKg = 2.2046226218;
  public const double CmPerInch = 2.54;

  /// <summary>"82.4 kg", "181.7 lb", "18.5 %", "7 h 30 min", "0.18 °C". Unknown units print as they are.</summary>
  public static string Format(double value, string? unit, UnitSystem units, int decimals = 1)
  {
    switch (unit)
    {
      case "kg" when units == UnitSystem.Imperial:
        return Number(value * LbPerKg, decimals) + " lb";
      case "cm" when units == UnitSystem.Imperial:
        return Number(value / CmPerInch, decimals) + " in";
      case "min":
        return Duration(value);
      case null or "":
        return Number(value, decimals);
      case "count":
        return Number(value, 0);
      case "C":
        return Number(value, decimals) + " °C";
      default:
        return Number(value, decimals) + " " + unit;
    }
  }

  /// <summary>A change with its direction in words: "0.4 kg down", "1.1 lb up".</summary>
  public static string Change(double value, string? unit, UnitSystem units, int decimals = 1) =>
    Format(Math.Abs(value), unit, units, decimals) + (value < 0 ? " down" : " up");

  private static string Duration(double minutes)
  {
    var whole = (int)Math.Round(minutes);
    if (whole < 60) return $"{whole} min";
    return whole % 60 == 0 ? $"{whole / 60} h" : $"{whole / 60} h {whole % 60} min";
  }

  private static string Number(double value, int decimals)
  {
    var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
    // 82.0 reads as 82; 1250 reads as 1,250.
    var format = rounded == Math.Floor(rounded) ? "#,0" : "#,0." + new string('0', decimals);
    return rounded.ToString(format, CultureInfo.InvariantCulture);
  }
}
