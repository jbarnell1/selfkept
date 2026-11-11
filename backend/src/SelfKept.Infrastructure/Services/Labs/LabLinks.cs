using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Services.Labs;

/// <summary>
/// One tie between two readings (docs/cross-reference.md). From and To are metric keys, or for a
/// draw condition or a substance on the medicines list, "condition:..." and "substance:...".
/// Direction is how To moves when From is higher or present: same, opposite, modifies (changes how
/// To should be read, with no fixed direction) or none (named because it does not move To).
/// </summary>
public sealed record LabLink(
  string Id,
  string From,
  string To,
  string Direction,
  string Explanation,
  string Source,
  string SourceUrl,
  string Evidence);

public static class LinkDirections
{
  public const string Same = "same";
  public const string Opposite = "opposite";
  public const string Modifies = "modifies";
  public const string None = "none";
  public static readonly string[] All = [Same, Opposite, Modifies, None];
}

/// <summary>The draw conditions a link can start from.</summary>
public static class DrawConditionKeys
{
  public const string Prefix = "condition:";
  public const string NotFasting = Prefix + "not_fasting";
  public const string NotMorning = Prefix + "not_morning";
  public static readonly string[] All = [NotFasting, NotMorning];

  /// <summary>Whether the condition holds for a panel. Unknown is not "holds".</summary>
  public static bool Holds(string key, PanelConditions conditions) => key switch
  {
    NotFasting => conditions.Fasting == FastingStates.No,
    NotMorning => conditions.DrawPeriod is DrawPeriods.Afternoon or DrawPeriods.Evening,
    _ => false,
  };

  /// <summary>Whether the two panels are known to differ on the condition (both answered, answers differ).</summary>
  public static bool Differs(string key, PanelConditions a, PanelConditions b) => key switch
  {
    NotFasting => a.Fasting != FastingStates.Unknown && b.Fasting != FastingStates.Unknown && a.Fasting != b.Fasting,
    NotMorning => a.DrawPeriod != DrawPeriods.Unknown && b.DrawPeriod != DrawPeriods.Unknown && Holds(key, a) != Holds(key, b),
    _ => false,
  };
}

/// <summary>
/// The link catalogue as code: one reviewed list that the review, the related readings, the rules
/// and docs/cross-reference.md all read. Only links whose source was opened and whose figures
/// were seen there are listed; the rest of the proposal's map is in the doc as "not included".
/// Sizes in the wording are the source's own.
/// </summary>
public static class LabLinks
{
  public const string SubstancePrefix = "substance:";

  public static string Substance(string tag) => SubstancePrefix + tag;

  private const string Marston = "Marston 2022, JAMA Cardiol (PMID 34773460); Johannesen 2021, JACC (PMID 33736827)";
  private const string MarstonUrl = "https://pubmed.ncbi.nlm.nih.gov/34773460/";
  private const string Sampson = "Sampson 2020, JAMA Cardiol (PMC7240357); Martin 2013, JAMA (PMID 24240933)";
  private const string SampsonUrl = "https://pmc.ncbi.nlm.nih.gov/articles/PMC7240357/";
  private const string Who = "WHO 2020 guideline on ferritin concentrations (eLENA)";
  private const string WhoUrl = "https://www.who.int/tools/elena/interventions/ferritin-concentrations";
  private const string Valenti = "Valenti 2023, Nat Rev Endocrinol 19:299-310 (metabolic hyperferritinaemia consensus)";
  private const string ValentiUrl = "https://pmc.ncbi.nlm.nih.gov/articles/PMC9936492/";
  private const string Thyroid = "NICE NG238 recommendation 1.4.3; Abreu 2017, Endocr Connect 6(3):188-199";
  private const string ThyroidUrl = "https://www.nice.org.uk/guidance/ng238/chapter/Recommendations";
  private const string Antonio = "Antonio 2021, J Int Soc Sports Nutr 18:13";
  private const string AntonioUrl = "https://pmc.ncbi.nlm.nih.gov/articles/PMC7871530/";
  private const string Biotin = "Li 2017, JAMA (PMID 28973622); FDA, Biotin interference with troponin lab tests (2022)";
  private const string BiotinUrl = "https://pubmed.ncbi.nlm.nih.gov/28973622/";
  private const string Nordestgaard = "Nordestgaard 2016, Eur Heart J 37(25):1944-1958 (EAS and EFLM consensus)";
  private const string NordestgaardUrl = "https://pmc.ncbi.nlm.nih.gov/articles/PMC4929379/";
  private const string Brambilla = "Brambilla 2009, J Clin Endocrinol Metab 94(3):907-913";
  private const string BrambillaUrl = "https://pmc.ncbi.nlm.nih.gov/articles/PMC2681273/";
  private const string Bhasin = "Bhasin 2018, J Clin Endocrinol Metab 103(5):1715-1744 (Endocrine Society guideline)";
  private const string BhasinUrl = "https://pubmed.ncbi.nlm.nih.gov/29562364/";

  private const string Strong = "strong";
  private const string StrongGuideline = "strong; guideline";
  private const string ModerateToStrong = "moderate to strong";
  private const string StrongToModerate = "strong to moderate";
  private const string Moderate = "moderate";

  private const string NotFastingLipid =
    "One to six hours after a usual meal, triglycerides run up to about 26 mg/dL (0.3 mmol/L) higher and total, LDL and non-HDL cholesterol about 8 mg/dL (0.2 mmol/L) lower, on average.";
  private const string BiotinText =
    "Biotin can interfere with lab tests whose method uses biotin. In a small study, 10 mg a day made some results read falsely high and others falsely low. Tell whoever orders or reads the test.";
  private const string HighFerritin =
    "A ferritin above 300 ng/mL in men or 200 in women, alongside metabolic features such as high triglycerides, low HDL or higher weight, usually reflects metabolic health rather than iron overload.";

  public static readonly IReadOnlyList<LabLink> All =
  [
    new("apob_ldl", LabMetricKeys.ApoB, LabMetricKeys.Ldl, LinkDirections.Same,
      "ApoB and LDL usually move together. When they disagree, ApoB tracks heart risk more closely: in 389,529 UK adults only ApoB was linked with heart attack once non-HDL cholesterol and triglycerides were in the same model, and in people on statins a high ApoB with a lower LDL carried the extra risk.",
      Marston, MarstonUrl, Strong),
    new("ldl_triglycerides", LabMetricKeys.Triglycerides, LabMetricKeys.Ldl, LinkDirections.Modifies,
      "Most labs calculate LDL from total cholesterol, HDL and triglycerides. Labs usually do not report a calculated LDL when triglycerides are 400 mg/dL or more, because the estimate carries large errors there.",
      Sampson, SampsonUrl, Strong),
    new("ferritin_hscrp", LabMetricKeys.HsCrp, LabMetricKeys.Ferritin, LinkDirections.Modifies,
      "Inflammation raises ferritin and can hide low iron. In people with an infection or inflammation, WHO guidance says a ferritin below 70 ug/L in adults may be used to indicate iron deficiency.",
      Who, WhoUrl, StrongGuideline),
    new("ferritin_triglycerides", LabMetricKeys.Triglycerides, LabMetricKeys.Ferritin, LinkDirections.Same, HighFerritin, Valenti, ValentiUrl, ModerateToStrong),
    new("ferritin_hdl", LabMetricKeys.Hdl, LabMetricKeys.Ferritin, LinkDirections.Opposite, HighFerritin, Valenti, ValentiUrl, ModerateToStrong),
    new("ferritin_weight", MetricKeys.WeightKg, LabMetricKeys.Ferritin, LinkDirections.Same, HighFerritin, Valenti, ValentiUrl, ModerateToStrong),
    new("ferritin_transferrin_saturation", LabMetricKeys.TransferrinSaturation, LabMetricKeys.Ferritin, LinkDirections.Modifies,
      "Transferrin saturation helps tell a metabolic high ferritin from iron overload: a saturation persistently above 50 % rules out the metabolic explanation.",
      Valenti, ValentiUrl, ModerateToStrong),
    new("tsh_ldl", LabMetricKeys.Tsh, LabMetricKeys.Ldl, LinkDirections.Same,
      "An underactive thyroid raises cholesterol, and guidelines list it among the common causes to rule out. Treating mild cases lowered LDL by about 0.62 mmol/L (about 24 mg/dL) in pooled trials.",
      Thyroid, ThyroidUrl, StrongToModerate),
    new("tsh_total_cholesterol", LabMetricKeys.Tsh, LabMetricKeys.TotalCholesterol, LinkDirections.Same,
      "An underactive thyroid raises cholesterol, and guidelines list it among the common causes to rule out. Treating mild cases lowered total cholesterol by about 0.62 mmol/L (about 24 mg/dL) in pooled trials.",
      Thyroid, ThyroidUrl, StrongToModerate),
    new("creatine_creatinine", Substance(SubstanceTags.Creatine), LabMetricKeys.Creatinine, LinkDirections.Same,
      "Creatine supplements can raise blood creatinine. A rise like that is unlikely to mean the kidneys are working less well.",
      Antonio, AntonioUrl, Moderate),
    new("creatine_egfr", Substance(SubstanceTags.Creatine), LabMetricKeys.Egfr, LinkDirections.Opposite,
      "eGFR is worked out from creatinine, so creatine supplements can lower it on paper without any change in how the kidneys filter.",
      Antonio, AntonioUrl, Moderate),
    new("biotin_tsh", Substance(SubstanceTags.Biotin), LabMetricKeys.Tsh, LinkDirections.Modifies, BiotinText, Biotin, BiotinUrl, "strong; depends on the lab method"),
    new("biotin_free_t4", Substance(SubstanceTags.Biotin), LabMetricKeys.FreeT4, LinkDirections.Modifies, BiotinText, Biotin, BiotinUrl, "strong; depends on the lab method"),
    new("biotin_free_t3", Substance(SubstanceTags.Biotin), LabMetricKeys.FreeT3, LinkDirections.Modifies, BiotinText, Biotin, BiotinUrl, "strong; depends on the lab method"),
    new("not_fasting_triglycerides", DrawConditionKeys.NotFasting, LabMetricKeys.Triglycerides, LinkDirections.Same, NotFastingLipid, Nordestgaard, NordestgaardUrl, Strong),
    new("not_fasting_ldl", DrawConditionKeys.NotFasting, LabMetricKeys.Ldl, LinkDirections.Opposite, NotFastingLipid, Nordestgaard, NordestgaardUrl, Strong),
    new("not_fasting_total_cholesterol", DrawConditionKeys.NotFasting, LabMetricKeys.TotalCholesterol, LinkDirections.Opposite, NotFastingLipid, Nordestgaard, NordestgaardUrl, Strong),
    new("not_fasting_remnant_cholesterol", DrawConditionKeys.NotFasting, LabMetricKeys.RemnantCholesterol, LinkDirections.Same,
      "One to six hours after a usual meal, remnant cholesterol runs about 8 mg/dL (0.2 mmol/L) higher on average.", Nordestgaard, NordestgaardUrl, Strong),
    new("not_fasting_hdl", DrawConditionKeys.NotFasting, LabMetricKeys.Hdl, LinkDirections.None,
      "HDL is not affected by a usual meal, so a fasting and a non-fasting HDL compare.", Nordestgaard, NordestgaardUrl, Strong),
    new("not_fasting_apob", DrawConditionKeys.NotFasting, LabMetricKeys.ApoB, LinkDirections.None,
      "ApoB is not affected by a usual meal, so a fasting and a non-fasting ApoB compare.", Nordestgaard, NordestgaardUrl, Strong),
    new("not_morning_testosterone", DrawConditionKeys.NotMorning, LabMetricKeys.TotalTestosterone, LinkDirections.Opposite,
      "Testosterone falls through the day. In men aged 30 to 40 it was 20 to 25 % lower at 16:00 than at 08:00; by 70 the gap was about 10 %.",
      Brambilla, BrambillaUrl, "strong; 66 men"),
    new("not_fasting_testosterone", DrawConditionKeys.NotFasting, LabMetricKeys.TotalTestosterone, LinkDirections.Modifies,
      "Guidelines recommend a fasting, morning testosterone as the test to compare, with a repeat to confirm a low value.",
      Bhasin, BhasinUrl, StrongGuideline),
  ];

  public static bool IsMetricKey(string key) => !key.StartsWith(SubstancePrefix, StringComparison.Ordinal) && !key.StartsWith(DrawConditionKeys.Prefix, StringComparison.Ordinal);

  /// <summary>Links that touch a reading (either end), in catalogue order.</summary>
  public static IReadOnlyList<LabLink> For(string metricKey) =>
    All.Where(l => l.From == metricKey || l.To == metricKey).ToList();

  /// <summary>The draw-condition and substance links that end at a marker.</summary>
  public static IReadOnlyList<LabLink> ContextFor(string metricKey) =>
    All.Where(l => l.To == metricKey && !IsMetricKey(l.From) && l.Direction != LinkDirections.None).ToList();

  public static LabLink Get(string id) => All.First(l => l.Id == id);
}
