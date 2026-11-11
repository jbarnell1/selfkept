namespace SelfKept.Core.Entities;

/// <summary>
/// One confirmed panel (docs/labs-design.md). The source image and the notes are encrypted with
/// IEncryptionService; a report's results also exist as lab.* samples so they show on Trends.
/// </summary>
public class LabReport
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }

  /// <summary>rythm, manual or other. See <see cref="LabProviders"/>.</summary>
  public string Provider { get; set; } = LabProviders.Manual;

  public DateOnly CollectedOn { get; set; }
  public DateOnly? ReceivedOn { get; set; }

  /// <summary>The source photo or screenshot, encrypted. Null for manual entry.</summary>
  public byte[]? SourceImageCipher { get; set; }

  /// <summary>Hex SHA-256 of the plain image, so the same photo is recognisable without decrypting.</summary>
  public string? ImageSha256 { get; set; }

  public byte[]? NotesCipher { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime? ConfirmedAt { get; set; }

  // Draw conditions (docs/labs-design.md, "Draw conditions"). All nullable: null means never asked.

  /// <summary>yes, no or unknown. See <see cref="FastingStates"/>.</summary>
  public string? Fasting { get; set; }

  /// <summary>Whole hours since the last meal, when the person knows. With a Rythm default, 8 means "8 or more".</summary>
  public short? FastingHours { get; set; }

  /// <summary>Local time of day the blood was drawn, when the person knows it.</summary>
  public TimeOnly? DrawTime { get; set; }

  /// <summary>morning, afternoon, evening or unknown. Worked out from <see cref="DrawTime"/> when it is given. See <see cref="DrawPeriods"/>.</summary>
  public string? DrawPeriod { get; set; }

  /// <summary>A hard training session in the days before the draw.</summary>
  public bool? HardTraining { get; set; }

  /// <summary>Unwell (an infection or similar) around the draw.</summary>
  public bool? Unwell { get; set; }

  /// <summary>True while the conditions are the provider's pre-filled defaults and the person has not changed them.</summary>
  public bool ConditionsDefaulted { get; set; }

  public List<LabResult> Results { get; set; } = [];
}

public static class FastingStates
{
  public const string Yes = "yes";
  public const string No = "no";
  public const string Unknown = "unknown";
  public static readonly string[] All = [Yes, No, Unknown];
}

/// <summary>
/// When in the day the blood was drawn. Morning is before 12:00 local time, afternoon 12:00 to
/// 17:59, evening from 18:00: the app's own split, so two draws can be compared like with like.
/// </summary>
public static class DrawPeriods
{
  public const string Morning = "morning";
  public const string Afternoon = "afternoon";
  public const string Evening = "evening";
  public const string Unknown = "unknown";
  public static readonly string[] All = [Morning, Afternoon, Evening, Unknown];

  public static string For(TimeOnly time) => time.Hour switch
  {
    < 12 => Morning,
    < 18 => Afternoon,
    _ => Evening,
  };
}

/// <summary>A panel's draw conditions as the rules and the review compare them. Fasting and the period are never null ("unknown").</summary>
public sealed record PanelConditions(string Fasting, short? FastingHours, string DrawPeriod, bool? HardTraining, bool? Unwell)
{
  public static readonly PanelConditions Unknown = new(FastingStates.Unknown, null, DrawPeriods.Unknown, null, null);
}

public static class LabProviders
{
  public const string Rythm = "rythm";
  public const string Manual = "manual";
  public const string Other = "other";
  public static readonly string[] All = [Rythm, Manual, Other];
}

/// <summary>One marker on one report, stored in the catalogue unit. Unique per (report, metric).</summary>
public class LabResult
{
  public Guid Id { get; set; }
  public Guid ReportId { get; set; }
  public Guid PersonId { get; set; }
  public string MetricKey { get; set; } = string.Empty;
  public double Value { get; set; }
  public string Unit { get; set; } = string.Empty;

  /// <summary>low, high, normal or none, as the lab reported it. See <see cref="LabFlags"/>.</summary>
  public string Flag { get; set; } = LabFlags.None;

  /// <summary>What the scanner read, for the review trail. Null for typed values.</summary>
  public string? RawText { get; set; }

  public bool Confirmed { get; set; } = true;

  /// <summary>The lab's own range when it was visible on the report.</summary>
  public double? ReferenceLow { get; set; }
  public double? ReferenceHigh { get; set; }

  /// <summary>True when the server computed the value from other results (the three ratios).</summary>
  public bool Derived { get; set; }

  public DateTime CreatedAt { get; set; }
}

public static class LabFlags
{
  public const string Low = "low";
  public const string High = "high";
  public const string Normal = "normal";
  public const string None = "none";
  public static readonly string[] All = [Low, High, Normal, None];
}

/// <summary>A name a lab prints for a marker, so OCR text can be matched to a catalogue key.</summary>
public class LabMarkerAlias
{
  public string MetricKey { get; set; } = string.Empty;
  public string Alias { get; set; } = string.Empty;
}

/// <summary>
/// An approximate adult population range for one marker and sex, from docs/lab-ranges.md. The
/// app shades it behind a person's own values and labels it approximate; it is not the lab's range.
/// </summary>
public class LabReference
{
  public string MetricKey { get; set; } = string.Empty;

  /// <summary>both, female or male. See <see cref="SexApplicabilities"/>.</summary>
  public string Sex { get; set; } = SexApplicabilities.Both;

  /// <summary>Null when the source gives only an upper bound.</summary>
  public double? Low { get; set; }

  /// <summary>Null when the source gives only a lower bound, or no range at all.</summary>
  public double? High { get; set; }

  public double? OptimalLow { get; set; }
  public double? OptimalHigh { get; set; }

  /// <summary>up, down or range: which way is better. See <see cref="GoodDirections"/>.</summary>
  public string GoodDirection { get; set; } = GoodDirections.Range;

  /// <summary>watch, clinician or none: how an out-of-range value is flagged. See <see cref="LabTiers"/>.</summary>
  public string Tier { get; set; } = LabTiers.Watch;

  public string SourceUrl { get; set; } = string.Empty;

  /// <summary>
  /// When the source gives a second interval from an age upwards (cystatin C at 50): the age in
  /// whole years where the older interval starts. Null for the usual single adult interval.
  /// </summary>
  public int? AgeSplitAt { get; set; }

  public double? OlderLow { get; set; }
  public double? OlderHigh { get; set; }

  public bool HasRange => Low != null || High != null;
}

public static class LabTiers
{
  public const string Watch = "watch";
  public const string Clinician = "clinician";
  public const string None = "none";
  public static readonly string[] All = [Watch, Clinician, None];
}

/// <summary>Lab metric keys the server itself refers to (ratios, trend markers).</summary>
public static class LabMetricKeys
{
  public const string TotalTestosterone = "lab.total_testosterone";
  public const string FreeTestosterone = "lab.free_testosterone";
  public const string Estradiol = "lab.estradiol";
  public const string Progesterone = "lab.progesterone";
  public const string Shbg = "lab.shbg";
  public const string Cortisol = "lab.cortisol";
  public const string Tsh = "lab.tsh";
  public const string FreeT3 = "lab.free_t3";
  public const string FreeT4 = "lab.free_t4";
  public const string ApoB = "lab.apob";
  public const string Ldl = "lab.ldl";
  public const string Hdl = "lab.hdl";
  public const string LdlApoBRatio = "lab.ldl_apob_ratio";
  public const string HsCrp = "lab.hscrp";
  public const string TotalCholesterol = "lab.total_cholesterol";
  public const string RemnantCholesterol = "lab.remnant_cholesterol";
  public const string TcHdlRatio = "lab.tc_hdl_ratio";
  public const string VitaminD = "lab.vitamin_d";
  public const string Ferritin = "lab.ferritin";
  public const string Fructosamine = "lab.fructosamine";
  public const string Albumin = "lab.albumin";
  public const string Triglycerides = "lab.triglycerides";
  public const string TgHdlRatio = "lab.tg_hdl_ratio";
  public const string UricAcid = "lab.uric_acid";
  public const string Hematocrit = "lab.hematocrit";
  public const string Hemoglobin = "lab.hemoglobin";
  public const string Creatinine = "lab.creatinine";
  public const string Alp = "lab.alp";
  public const string Ggt = "lab.ggt";
  public const string Egfr = "lab.egfr";
  public const string Alt = "lab.alt";
  public const string Ast = "lab.ast";
  public const string Platelets = "lab.platelets";
  public const string Wbc = "lab.wbc";
  public const string Rbc = "lab.rbc";
  public const string Mcv = "lab.mcv";
  public const string HbA1c = "lab.hba1c";
  public const string FastingGlucose = "lab.glucose_fasting";
  public const string CystatinC = "lab.cystatin_c";
  public const string TransferrinSaturation = "lab.transferrin_saturation";
  public const string Iron = "lab.iron";
  public const string Tibc = "lab.tibc";

  /// <summary>The markers added with the cross-reference package (migrations 0019 and 0020).</summary>
  public static readonly string[] CrossReferenceAdditions =
    [Alt, Ast, Platelets, Wbc, Rbc, Mcv, HbA1c, FastingGlucose, CystatinC, TransferrinSaturation, Iron, Tibc];

  /// <summary>The markers lab_trend watches (docs/labs-design.md, "Insights").</summary>
  public static readonly string[] TrendMarkers = [ApoB, Ldl, HsCrp, Fructosamine, Triglycerides, Ferritin, Tsh, Ggt];
}

/// <summary>The per-person system source that lab results are written under.</summary>
public static class LabSource
{
  public const string DeviceName = "Lab results";
  public const string Platform = "server";
}
