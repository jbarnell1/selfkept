using System.Globalization;
using System.Text;
using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Data;

/// <summary>
/// The lab reference ranges and marker aliases as code: the one list that generates the SQL
/// seed migration (0007_lab_seed.up.sql, checked for drift by LabSeedTests) and seeds the
/// InMemory test database, which runs no SQL. The reference rows are the machine-readable CSV
/// block at the end of docs/lab-ranges.md, copied verbatim; the aliases are the Rythm display
/// names from the catalogue plus common synonyms.
/// </summary>
public static class LabSeed
{
  public const string SeedScriptName = "0007_lab_seed.up.sql";

  private const string Both = SexApplicabilities.Both;
  private const string Female = SexApplicabilities.Female;
  private const string Male = SexApplicabilities.Male;
  private const string Watch = LabTiers.Watch;
  private const string Clinician = LabTiers.Clinician;
  private const string NoTier = LabTiers.None;
  private const string Up = GoodDirections.Up;
  private const string Down = GoodDirections.Down;
  private const string Range = GoodDirections.Range;

  public static IReadOnlyList<LabReference> References { get; } = BuildReferences();

  public static IReadOnlyList<LabMarkerAlias> Aliases { get; } = BuildAliases();

  private static List<LabReference> BuildReferences()
  {
    var list = new List<LabReference>();

    void Add(string key, string sex, double? low, double? high, double? optimalLow, double? optimalHigh, string direction, string tier, string url) =>
      list.Add(new LabReference
      {
        MetricKey = key, Sex = sex, Low = low, High = high, OptimalLow = optimalLow, OptimalHigh = optimalHigh,
        GoodDirection = direction, Tier = tier, SourceUrl = url,
      });

    const string Mayo = "https://www.mayocliniclabs.com/test-catalog/overview/";
    const string Medline = "https://medlineplus.gov/";
    const string Cleveland = "https://my.clevelandclinic.org/health/";
    const string Pmc = "https://pmc.ncbi.nlm.nih.gov/articles/";

    // docs/lab-ranges.md, "Machine-readable": key, unit, sex, low, high, optimal_low, optimal_high, good_direction, tier, source_url
    Add(LabMetricKeys.TotalTestosterone, Male, 240, 950, null, null, Range, Watch, Mayo + "83686");
    Add(LabMetricKeys.TotalTestosterone, Female, 8, 60, null, null, Range, Watch, Mayo + "83686");
    Add(LabMetricKeys.FreeTestosterone, Male, 35, 207, null, null, Range, Watch, Mayo + "83686");
    Add(LabMetricKeys.FreeTestosterone, Female, null, 10.8, null, null, Range, Watch, Mayo + "83686");
    Add(LabMetricKeys.Estradiol, Male, 10, 40, null, null, Range, Watch, Mayo + "81816");
    Add(LabMetricKeys.Estradiol, Female, 15, 350, null, null, Range, Watch, Mayo + "81816");
    Add(LabMetricKeys.Progesterone, Male, null, 0.2, null, null, Range, Watch, Mayo + "8141");
    Add(LabMetricKeys.Progesterone, Female, null, 24, null, null, Range, Watch, Mayo + "8141");
    Add(LabMetricKeys.Shbg, Male, 13.3, 89.5, null, null, Range, Watch, Mayo + "608102");
    Add(LabMetricKeys.Shbg, Female, 18.2, 135.5, null, null, Range, Watch, Mayo + "608102");
    Add(LabMetricKeys.Cortisol, Both, 7, 25, null, null, Range, Clinician, Mayo + "8545");
    Add(LabMetricKeys.Tsh, Both, 0.3, 4.2, null, null, Range, Clinician, Mayo + "8939");
    Add(LabMetricKeys.FreeT3, Both, 2.0, 4.4, null, null, Range, Watch, Mayo + "621321");
    Add(LabMetricKeys.FreeT4, Both, 0.8, 2.0, null, null, Range, Clinician, Mayo + "8859");
    Add(LabMetricKeys.ApoB, Male, 66, 133, null, 90, Down, Watch, Cleveland + "diagnostics/24992-apolipoprotein-b-test");
    Add(LabMetricKeys.ApoB, Female, 60, 117, null, 90, Down, Watch, Cleveland + "diagnostics/24992-apolipoprotein-b-test");
    Add(LabMetricKeys.Ldl, Both, null, 100, null, 100, Down, Watch, Cleveland + "articles/24391-ldl-cholesterol");
    Add(LabMetricKeys.Hdl, Male, 40, 80, 60, 80, Up, Watch, Medline + "cholesterollevelswhatyouneedtoknow.html");
    Add(LabMetricKeys.Hdl, Female, 50, 80, 60, 80, Up, Watch, Medline + "cholesterollevelswhatyouneedtoknow.html");
    Add(LabMetricKeys.LdlApoBRatio, Both, null, null, null, null, Range, NoTier, Mayo + "624438");
    Add(LabMetricKeys.HsCrp, Both, null, 3, null, 1, Down, Watch, "https://www.labcorp.com/tests/120766/c-reactive-protein-crp-high-sensitivity-cardiac-risk-assessment");
    Add(LabMetricKeys.TotalCholesterol, Both, null, 200, null, 200, Down, Watch, Medline + "cholesterollevelswhatyouneedtoknow.html");
    Add(LabMetricKeys.RemnantCholesterol, Both, null, 30, null, 30, Down, Watch, Pmc + "PMC11224847/");
    Add(LabMetricKeys.TcHdlRatio, Both, null, 5, null, 3.5, Down, Watch, "https://www.uhhospitals.org/health-information/health-and-wellness-library/tests-and-procedures/article/lab-tests-v1/lipid-panel-with-total-cholesterol-hdl-ratio");
    Add(LabMetricKeys.VitaminD, Both, 20, 50, 20, 50, Range, Watch, Mayo + "83670");
    Add(LabMetricKeys.Ferritin, Male, 31, 409, null, null, Range, Watch, Mayo + "619953");
    Add(LabMetricKeys.Ferritin, Female, 6, 175, null, null, Range, Watch, Mayo + "619953");
    Add(LabMetricKeys.Fructosamine, Both, 200, 285, null, null, Range, Clinician, Mayo + "81610");
    Add(LabMetricKeys.Albumin, Both, 3.4, 5.4, null, null, Range, Clinician, Medline + "ency/article/003480.htm");
    Add(LabMetricKeys.Triglycerides, Both, null, 150, null, 150, Down, Watch, Medline + "triglycerides.html");
    Add(LabMetricKeys.TgHdlRatio, Male, null, 2.8, null, null, Down, Watch, Pmc + "PMC11274455/");
    Add(LabMetricKeys.TgHdlRatio, Female, null, 2.5, null, null, Down, Watch, Pmc + "PMC11274455/");
    Add(LabMetricKeys.UricAcid, Male, 4.0, 8.6, null, null, Range, Watch, Medline + "ency/article/003476.htm");
    Add(LabMetricKeys.UricAcid, Female, 3.0, 7.1, null, null, Range, Watch, Medline + "ency/article/003476.htm");
    Add(LabMetricKeys.Hematocrit, Male, 37, 48, null, null, Range, Clinician, Medline + "ency/article/003646.htm");
    Add(LabMetricKeys.Hematocrit, Female, 34, 43, null, null, Range, Clinician, Medline + "ency/article/003646.htm");
    Add(LabMetricKeys.Hemoglobin, Male, 13.8, 17.2, null, null, Range, Clinician, Medline + "ency/article/003645.htm");
    Add(LabMetricKeys.Hemoglobin, Female, 12.1, 15.1, null, null, Range, Clinician, Medline + "ency/article/003645.htm");
    Add(LabMetricKeys.Creatinine, Male, 0.7, 1.3, null, null, Range, Clinician, Medline + "ency/article/003475.htm");
    Add(LabMetricKeys.Creatinine, Female, 0.5, 0.95, null, null, Range, Clinician, Medline + "ency/article/003475.htm");
    Add(LabMetricKeys.Alp, Both, 40, 129, null, null, Range, Watch, Medline + "ency/article/003470.htm");
    Add(LabMetricKeys.Ggt, Male, 8, 61, null, null, Down, Watch, Mayo + "8677");
    Add(LabMetricKeys.Ggt, Female, 5, 36, null, null, Down, Watch, Mayo + "8677");
    Add(LabMetricKeys.Egfr, Both, 90, null, 90, null, Up, Clinician, "https://www.kidney.org/kidney-topics/estimated-glomerular-filtration-rate-egfr");

    return list;
  }

  private static List<LabMarkerAlias> BuildAliases()
  {
    var list = new List<LabMarkerAlias>();
    var seen = new HashSet<(string, string)>();

    void Add(string key, params string[] aliases)
    {
      // The catalogue display name is always an alias.
      var names = new List<string> { MetricSeed.All.Single(m => m.Key == key).DisplayName };
      names.AddRange(aliases);
      foreach (var alias in names)
      {
        if (seen.Add((key, alias.ToLowerInvariant()))) list.Add(new LabMarkerAlias { MetricKey = key, Alias = alias });
      }
    }

    Add(LabMetricKeys.TotalTestosterone, "Testosterone", "Testosterone, Total", "Testosterone (Total)", "Testosterone Total", "Total T", "Total testosterone (serum)");
    Add(LabMetricKeys.FreeTestosterone, "Testosterone, Free", "Testosterone (Free)", "Testosterone Free", "Free T");
    Add(LabMetricKeys.Estradiol, "Oestradiol", "E2", "Estradiol (E2)", "Estradiol, serum");
    Add(LabMetricKeys.Progesterone, "P4", "Progesterone, serum");
    Add(LabMetricKeys.Shbg, "Sex hormone binding globulin", "Sex hormone-binding globulin", "Sex-hormone binding globulin");
    Add(LabMetricKeys.Cortisol, "Cortisol (AM)", "Cortisol, AM", "Morning cortisol", "Cortisol, serum", "AM cortisol");
    Add(LabMetricKeys.Tsh, "Thyroid stimulating hormone", "Thyroid-stimulating hormone", "Thyrotropin", "TSH, serum");
    Add(LabMetricKeys.FreeT3, "FT3", "T3, Free", "T3 Free", "Free T3 (FT3)", "Triiodothyronine, free", "Free triiodothyronine", "T3 (Free)");
    Add(LabMetricKeys.FreeT4, "FT4", "T4, Free", "T4 Free", "Free T4 (FT4)", "Thyroxine, free", "Free thyroxine", "T4 (Free)");
    Add(LabMetricKeys.ApoB, "Apolipoprotein B", "Apo B", "Apo-B", "ApoB-100", "Apolipoprotein B-100", "Apolipoprotein B (ApoB)");
    Add(LabMetricKeys.Ldl, "LDL", "LDL-C", "LDL Chol", "LDL-cholesterol", "Cholesterol, LDL", "LDL Cholesterol (calculated)", "LDL Direct", "LDL Cholesterol Calc");
    Add(LabMetricKeys.Hdl, "HDL", "HDL-C", "HDL Chol", "HDL-cholesterol", "Cholesterol, HDL");
    Add(LabMetricKeys.LdlApoBRatio, "LDL/ApoB", "LDL-C/ApoB", "LDL:ApoB", "LDL/ApoB ratio", "LDL-C to ApoB ratio");
    Add(LabMetricKeys.HsCrp, "hsCRP", "hs CRP", "High sensitivity CRP", "High-sensitivity CRP", "C-reactive protein, high sensitivity", "CRP, high sensitivity",
      "CRP (hs)", "High sensitivity C-reactive protein", "hs-CRP (cardiac)", "CRP-hs", "C-Reactive Protein (hs)", "High-sensitivity C-reactive protein");
    Add(LabMetricKeys.TotalCholesterol, "Cholesterol", "Cholesterol, Total", "Cholesterol total", "TC", "Cholesterol (Total)");
    Add(LabMetricKeys.RemnantCholesterol, "Remnant-C", "Remnant Chol", "Remnant lipoprotein cholesterol");
    Add(LabMetricKeys.TcHdlRatio, "TC/HDL", "TC:HDL", "TC/HDL ratio", "Total cholesterol/HDL", "Chol/HDL ratio", "Cholesterol/HDL ratio", "Total/HDL ratio", "Total cholesterol to HDL ratio");
    Add(LabMetricKeys.VitaminD, "25-OH Vitamin D", "25-hydroxyvitamin D", "25-Hydroxy vitamin D", "Vitamin D, 25-OH", "Vitamin D (25-OH)", "Vitamin D3", "25(OH)D", "Vit D", "25-OH-D",
      "Vitamin D, 25-Hydroxy", "Vitamin D 25-Hydroxy", "25-Hydroxyvitamin D (D2 + D3)");
    Add(LabMetricKeys.Ferritin, "Ferritin, serum");
    Add(LabMetricKeys.Fructosamine, "Fructosamine, serum");
    Add(LabMetricKeys.Albumin, "Serum albumin", "Albumin, serum", "Alb");
    Add(LabMetricKeys.Triglycerides, "Trig", "Trigs", "TG", "Triglyceride");
    Add(LabMetricKeys.TgHdlRatio, "TG/HDL", "TG:HDL", "TG/HDL ratio", "Trig/HDL ratio", "Triglycerides/HDL", "Triglyceride/HDL ratio");
    Add(LabMetricKeys.UricAcid, "Urate", "Serum urate", "Uric acid, serum");
    Add(LabMetricKeys.Hematocrit, "Hct", "Haematocrit", "PCV");
    Add(LabMetricKeys.Hemoglobin, "Hgb", "Hb", "Haemoglobin");
    Add(LabMetricKeys.Creatinine, "Creat", "Serum creatinine", "Creatinine, serum");
    Add(LabMetricKeys.Alp, "Alkaline phosphatase", "Alk Phos", "Alk. Phos.", "Alkaline phosphatase (ALP)");
    Add(LabMetricKeys.Ggt, "Gamma GT", "Gamma-GT", "Gamma-glutamyl transferase", "Gamma glutamyl transferase", "Gamma-glutamyltransferase", "GGTP", "Gamma-Glutamyl Transferase (GGT)");
    Add(LabMetricKeys.Egfr, "Estimated GFR", "Estimated glomerular filtration rate", "GFR", "eGFR (CKD-EPI)", "GFR, estimated", "eGFR CKD-EPI");

    return list;
  }

  /// <summary>Renders the idempotent SQL seed. The migration file must equal this text exactly.</summary>
  public static string ToSql()
  {
    var sb = new StringBuilder();
    sb.Append("-- 0007_lab_seed: population reference ranges (docs/lab-ranges.md) and marker aliases.\n");
    sb.Append("-- GENERATED from LabSeed.cs (LabSeedTests checks for drift). Edit the C# lists, then regenerate.\n");
    sb.Append("-- Idempotent: re-running updates the reference rows and adds missing aliases; nothing is removed.\n\n");

    sb.Append("INSERT INTO lab_reference (metric_key, sex, low, high, optimal_low, optimal_high, good_direction, tier, source_url) VALUES\n");
    for (var i = 0; i < References.Count; i++)
    {
      var r = References[i];
      sb.Append("  (")
        .Append(Lit(r.MetricKey)).Append(", ")
        .Append(Lit(r.Sex)).Append(", ")
        .Append(Num(r.Low)).Append(", ")
        .Append(Num(r.High)).Append(", ")
        .Append(Num(r.OptimalLow)).Append(", ")
        .Append(Num(r.OptimalHigh)).Append(", ")
        .Append(Lit(r.GoodDirection)).Append(", ")
        .Append(Lit(r.Tier)).Append(", ")
        .Append(Lit(r.SourceUrl))
        .Append(i == References.Count - 1 ? ")\n" : "),\n");
    }
    sb.Append("ON CONFLICT (metric_key, sex) DO UPDATE SET\n");
    sb.Append("  low = EXCLUDED.low,\n");
    sb.Append("  high = EXCLUDED.high,\n");
    sb.Append("  optimal_low = EXCLUDED.optimal_low,\n");
    sb.Append("  optimal_high = EXCLUDED.optimal_high,\n");
    sb.Append("  good_direction = EXCLUDED.good_direction,\n");
    sb.Append("  tier = EXCLUDED.tier,\n");
    sb.Append("  source_url = EXCLUDED.source_url;\n\n");

    sb.Append("INSERT INTO lab_marker_alias (metric_key, alias) VALUES\n");
    for (var i = 0; i < Aliases.Count; i++)
    {
      var a = Aliases[i];
      sb.Append("  (").Append(Lit(a.MetricKey)).Append(", ").Append(Lit(a.Alias)).Append(i == Aliases.Count - 1 ? ")\n" : "),\n");
    }
    sb.Append("ON CONFLICT (metric_key, alias) DO NOTHING;\n");
    return sb.ToString();
  }

  private static string Lit(string value) => "'" + value.Replace("'", "''") + "'";

  private static string Num(double? value) => value == null ? "NULL" : value.Value.ToString("0.###", CultureInfo.InvariantCulture);
}
