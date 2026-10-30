using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Services.Labs;

/// <summary>
/// The panel as the Labs screen lays it out: groups in Rythm order, markers in order within each
/// group, and the short "what moves it" text per marker (docs/labs-design.md, "Labs screen").
/// </summary>
public static class LabPanel
{
  public static class Groups
  {
    public const string Hormones = "hormones";
    public const string Thyroid = "thyroid";
    public const string Heart = "heart";
    public const string Metabolic = "metabolic";
    public const string Blood = "blood";
    public const string Kidney = "kidney";
    public const string Liver = "liver";
  }

  /// <summary>Every lab marker in display order with its group.</summary>
  public static readonly IReadOnlyList<(string MetricKey, string Group)> Order =
  [
    (LabMetricKeys.TotalTestosterone, Groups.Hormones),
    (LabMetricKeys.FreeTestosterone, Groups.Hormones),
    (LabMetricKeys.Estradiol, Groups.Hormones),
    (LabMetricKeys.Progesterone, Groups.Hormones),
    (LabMetricKeys.Shbg, Groups.Hormones),
    (LabMetricKeys.Cortisol, Groups.Hormones),
    (LabMetricKeys.Tsh, Groups.Thyroid),
    (LabMetricKeys.FreeT3, Groups.Thyroid),
    (LabMetricKeys.FreeT4, Groups.Thyroid),
    (LabMetricKeys.ApoB, Groups.Heart),
    (LabMetricKeys.Ldl, Groups.Heart),
    (LabMetricKeys.Hdl, Groups.Heart),
    (LabMetricKeys.LdlApoBRatio, Groups.Heart),
    (LabMetricKeys.HsCrp, Groups.Heart),
    (LabMetricKeys.TotalCholesterol, Groups.Heart),
    (LabMetricKeys.RemnantCholesterol, Groups.Heart),
    (LabMetricKeys.TcHdlRatio, Groups.Heart),
    (LabMetricKeys.Triglycerides, Groups.Heart),
    (LabMetricKeys.TgHdlRatio, Groups.Heart),
    (LabMetricKeys.VitaminD, Groups.Metabolic),
    (LabMetricKeys.Ferritin, Groups.Metabolic),
    (LabMetricKeys.Fructosamine, Groups.Metabolic),
    (LabMetricKeys.Albumin, Groups.Metabolic),
    (LabMetricKeys.UricAcid, Groups.Metabolic),
    (LabMetricKeys.Hematocrit, Groups.Blood),
    (LabMetricKeys.Hemoglobin, Groups.Blood),
    (LabMetricKeys.Creatinine, Groups.Kidney),
    (LabMetricKeys.Egfr, Groups.Kidney),
    (LabMetricKeys.Alp, Groups.Liver),
    (LabMetricKeys.Ggt, Groups.Liver),
  ];

  private static readonly Dictionary<string, int> Index = Order.Select((o, i) => (o.MetricKey, i)).ToDictionary(x => x.MetricKey, x => x.i, StringComparer.Ordinal);
  private static readonly Dictionary<string, string> GroupOf = Order.ToDictionary(o => o.MetricKey, o => o.Group, StringComparer.Ordinal);

  public static int OrderOf(string metricKey) => Index.TryGetValue(metricKey, out var i) ? i : int.MaxValue;

  public static string GroupFor(string metricKey) => GroupOf.TryGetValue(metricKey, out var g) ? g : "other";

  /// <summary>The ratio markers the server derives, with the numerator and denominator keys.</summary>
  public static readonly IReadOnlyList<(string Ratio, string Numerator, string Denominator)> DerivedRatios =
  [
    (LabMetricKeys.LdlApoBRatio, LabMetricKeys.Ldl, LabMetricKeys.ApoB),
    (LabMetricKeys.TcHdlRatio, LabMetricKeys.TotalCholesterol, LabMetricKeys.Hdl),
    (LabMetricKeys.TgHdlRatio, LabMetricKeys.Triglycerides, LabMetricKeys.Hdl),
  ];

  private static readonly Dictionary<string, string> WhatMovesIt = new(StringComparer.Ordinal)
  {
    [LabMetricKeys.TotalTestosterone] = "Sleep, body fat, heavy training blocks, alcohol and the time of day the blood was drawn. Morning values run highest.",
    [LabMetricKeys.FreeTestosterone] = "Follows total testosterone and moves the opposite way to SHBG. The assay matters, so compare values from the same lab.",
    [LabMetricKeys.Estradiol] = "Cycle phase and menopause in women; body fat and testosterone in men. Varies a lot between draws.",
    [LabMetricKeys.Progesterone] = "Cycle timing almost entirely: it rises after ovulation and falls before a period.",
    [LabMetricKeys.Shbg] = "Thyroid hormone, oestrogen and low body fat raise it; insulin resistance and higher body fat lower it.",
    [LabMetricKeys.Cortisol] = "Time of day first, then poor sleep, stress, illness and some medicines. Compare morning draws with morning draws.",
    [LabMetricKeys.Tsh] = "Iodine, thyroid autoimmunity, some medicines and recent illness. It moves slowly and reads with free T4.",
    [LabMetricKeys.FreeT3] = "Calorie restriction and illness lower it; it reads with TSH and free T4 rather than on its own.",
    [LabMetricKeys.FreeT4] = "Thyroid output and thyroid medicine. Read together with TSH.",
    [LabMetricKeys.ApoB] = "Saturated fat, body weight, insulin resistance and genetics. Statins and some other medicines lower it. Changes over months, not days.",
    [LabMetricKeys.Ldl] = "Diet, weight, genetics and medicines. Moves with ApoB most of the time.",
    [LabMetricKeys.Hdl] = "Exercise, body weight, alcohol and genetics. Hard to move by much on purpose.",
    [LabMetricKeys.LdlApoBRatio] = "Particle size. A low ratio with normal LDL can mean more small particles than the LDL number suggests.",
    [LabMetricKeys.HsCrp] = "Any infection or injury in the last couple of weeks, body fat, sleep and gum health. Repeat before reading much into one value.",
    [LabMetricKeys.TotalCholesterol] = "Diet, weight, genetics and medicines. Interpreted through LDL, HDL and ApoB.",
    [LabMetricKeys.RemnantCholesterol] = "Triglycerides, fasting state, weight and alcohol. Calculated from the other three cholesterol numbers.",
    [LabMetricKeys.TcHdlRatio] = "Either side of the fraction: total cholesterol and HDL.",
    [LabMetricKeys.VitaminD] = "Sun exposure, season, supplements and body fat. Takes weeks to change.",
    [LabMetricKeys.Ferritin] = "Iron intake, blood loss and inflammation, which pushes it up regardless of iron stores.",
    [LabMetricKeys.Fructosamine] = "Average blood glucose over the last two to three weeks. Low albumin pulls it down.",
    [LabMetricKeys.Albumin] = "Hydration, liver and kidney health, inflammation and protein intake.",
    [LabMetricKeys.Triglycerides] = "A recent meal, alcohol, sugar and refined carbohydrate, weight. Fasting values are the ones to compare.",
    [LabMetricKeys.TgHdlRatio] = "Triglycerides mostly, so the same things: recent food, alcohol, weight and insulin sensitivity.",
    [LabMetricKeys.UricAcid] = "Alcohol, fructose, red meat and seafood, dehydration, some diuretics and kidney function.",
    [LabMetricKeys.Hematocrit] = "Hydration on the day, altitude, iron stores and smoking. Reads with hemoglobin.",
    [LabMetricKeys.Hemoglobin] = "Iron, B12 and folate, blood loss, hydration and altitude.",
    [LabMetricKeys.Creatinine] = "Muscle mass, a big protein meal or creatine the day before, hydration and kidney function.",
    [LabMetricKeys.Egfr] = "Derived from creatinine, so the same things, with age built in. Repeat before reading one low value.",
    [LabMetricKeys.Alp] = "Liver and bone turnover. Higher in growing teenagers and in pregnancy.",
    [LabMetricKeys.Ggt] = "Alcohol in the last weeks, some medicines, liver fat.",
  };

  public static string WhatMoves(string metricKey) => WhatMovesIt.TryGetValue(metricKey, out var text) ? text : string.Empty;
}
