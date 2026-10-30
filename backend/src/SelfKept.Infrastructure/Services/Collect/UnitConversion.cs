using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Services.Collect;

/// <summary>
/// The short allowlist of unit conversions the server performs on ingest (docs/ingest-contract.md)
/// and on lab results (docs/labs-design.md). Anything not listed here, and not already the
/// catalogue unit, is rejected per sample or per result.
/// </summary>
public static class UnitConversion
{
  public const double MetresPerMile = 1609.344;
  public const double MetresPerKilometre = 1000;
  public const double KilogramsPerPound = 0.45359237;
  public const double CentimetresPerInch = 2.54;
  public const double MmolPerMgDlGlucose = 0.0555;

  // Lab units, posted unit to catalogue unit. Molar to mass factors are the molecular weights the
  // design lists; mass units of the same quantity (ug/L to ng/mL) are 1.
  public const double MgDlPerMmolCholesterol = 38.67;
  public const double MgDlPerMmolTriglycerides = 88.57;
  public const double NgDlPerNmolTestosterone = 28.84;
  public const double PgMlPerPmolFreeTestosterone = 0.2884;
  public const double PgMlPerPmolFreeT3 = 0.651;
  public const double PgMlPerPmolEstradiol = 0.2724;
  public const double NgMlPerNmolProgesterone = 0.3145;
  public const double UgDlPerNmolCortisol = 0.03625;
  public const double NgDlPerPmolFreeT4 = 0.0777;
  public const double NgMlPerNmolVitaminD = 0.4;
  public const double MgDlPerUmolCreatinine = 0.0113;
  public const double MgDlPerUmolUricAcid = 0.0168;

  /// <summary>(metric key, posted unit) to factor into the catalogue unit, for the lab markers.</summary>
  private static readonly Dictionary<(string Key, string Unit), double> LabFactors = new()
  {
    [(LabMetricKeys.TotalCholesterol, "mmol/l")] = MgDlPerMmolCholesterol,
    [(LabMetricKeys.Ldl, "mmol/l")] = MgDlPerMmolCholesterol,
    [(LabMetricKeys.Hdl, "mmol/l")] = MgDlPerMmolCholesterol,
    [(LabMetricKeys.RemnantCholesterol, "mmol/l")] = MgDlPerMmolCholesterol,
    [(LabMetricKeys.Triglycerides, "mmol/l")] = MgDlPerMmolTriglycerides,
    [(LabMetricKeys.ApoB, "g/l")] = 100,
    [(LabMetricKeys.TotalTestosterone, "nmol/l")] = NgDlPerNmolTestosterone,
    [(LabMetricKeys.FreeTestosterone, "pmol/l")] = PgMlPerPmolFreeTestosterone,
    [(LabMetricKeys.FreeT3, "pmol/l")] = PgMlPerPmolFreeT3,
    [(LabMetricKeys.Estradiol, "pmol/l")] = PgMlPerPmolEstradiol,
    [(LabMetricKeys.Progesterone, "nmol/l")] = NgMlPerNmolProgesterone,
    [(LabMetricKeys.Cortisol, "nmol/l")] = UgDlPerNmolCortisol,
    [(LabMetricKeys.FreeT4, "pmol/l")] = NgDlPerPmolFreeT4,
    [(LabMetricKeys.VitaminD, "nmol/l")] = NgMlPerNmolVitaminD,
    [(LabMetricKeys.Ferritin, "ug/l")] = 1,
    [(LabMetricKeys.HsCrp, "mg/dl")] = 10,
    [(LabMetricKeys.Albumin, "g/l")] = 0.1,
    [(LabMetricKeys.Hemoglobin, "g/l")] = 0.1,
    [(LabMetricKeys.Creatinine, "umol/l")] = MgDlPerUmolCreatinine,
    [(LabMetricKeys.UricAcid, "umol/l")] = MgDlPerUmolUricAcid,
  };

  /// <summary>
  /// Converts value from the posted unit into the metric's stored unit. Returns null when the
  /// posted unit is not accepted for that metric.
  /// </summary>
  public static double? Convert(Metric metric, string unit, double value)
  {
    var posted = Normalise(unit);
    var stored = Normalise(metric.Unit);
    if (posted == stored) return value;

    if (metric.IsLab)
    {
      return LabFactors.TryGetValue((metric.Key, posted), out var factor) ? value * factor : null;
    }

    return (stored, posted) switch
    {
      ("bpm", "count/min") => value,
      ("m", "mi") => value * MetresPerMile,
      ("m", "km") => value * MetresPerKilometre,
      ("kg", "lb") => value * KilogramsPerPound,
      ("cm", "in") => value * CentimetresPerInch,
      ("c", "f") => IsTemperatureDelta(metric) ? value * 5 / 9 : (value - 32) * 5 / 9,
      ("mmol/l", "mg/dl") when metric.Key == MetricKeys.BloodGlucoseMmol => value * MmolPerMgDlGlucose,
      _ => null,
    };
  }

  /// <summary>True when the unit is the metric's own or one Convert accepts for it.</summary>
  public static bool Accepts(Metric metric, string unit) => Convert(metric, unit, 1) != null;

  // A wrist temperature delta is a difference, so Fahrenheit to Celsius is a scale without the offset.
  private static bool IsTemperatureDelta(Metric metric) => metric.Key.Contains("delta", StringComparison.Ordinal);

  /// <summary>Lowercase, with the spellings labs and OCR produce folded onto the catalogue's.</summary>
  public static string Normalise(string unit)
  {
    var u = unit.Trim().ToLowerInvariant()
      .Replace('µ', 'u').Replace("μ", "u", StringComparison.Ordinal)
      .Replace("mcg", "ug", StringComparison.Ordinal).Replace("mcmol", "umol", StringComparison.Ordinal)
      .Replace("²", "2", StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
    return u switch
    {
      "°f" or "degf" or "fahrenheit" => "f",
      "°c" or "degc" or "celsius" => "c",
      "lbs" => "lb",
      "percent" or "pct" => "%",
      "meters" or "metres" => "m",
      "miles" => "mi",
      "beats/min" => "bpm",
      "uiu/ml" or "miu/ml" or "mu/l" => "miu/l",
      "iu/l" => "u/l",
      "ml/min/1.73m2" or "ml/min/1,73m2" or "ml/min" => "ml/min/1.73m2",
      _ => u,
    };
  }
}
