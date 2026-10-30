using System.Text;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;

namespace SelfKept.Infrastructure.Data;

/// <summary>
/// The metric catalogue as code: the one list that generates the SQL seed migration
/// (0004_metric_seed.up.sql, checked for drift by MetricSeedTests) and seeds the InMemory test
/// database, which runs no SQL. On Postgres the metric table is authoritative and the catalogue
/// loads from it.
/// </summary>
public static class MetricSeed
{
  public const string SeedScriptName = "0004_metric_seed.up.sql";

  private const string Instant = MetricKinds.Instant;
  private const string Interval = MetricKinds.Interval;
  private const string Daily = MetricKinds.Daily;
  private const string Up = GoodDirections.Up;
  private const string Down = GoodDirections.Down;
  private const string Neutral = GoodDirections.Neutral;
  private const string Range = GoodDirections.Range;

  private const string SleepStageLabels = """{"0":"in_bed","1":"awake","2":"light","3":"deep","4":"rem","5":"unspecified"}""";
  private const string MenstrualFlowLabels = """{"0":"none","1":"light","2":"medium","3":"heavy","4":"unspecified"}""";
  private const string MoodLabels = """{"1":"very_low","2":"low","3":"meh","4":"okay","5":"good","6":"great"}""";
  private const string ResilienceLabels = """{"1":"limited","2":"adequate","3":"solid","4":"strong","5":"exceptional"}""";

  public static IReadOnlyList<Metric> All { get; } = Build();

  private static List<Metric> Build()
  {
    var list = new List<Metric>();

    void Add(string key, string unit, string kind, ShareCategory category, string name, string blurb, string direction,
      string? loinc = null, string? labels = null, string sex = SexApplicabilities.Both) =>
      list.Add(new Metric
      {
        Key = key, Unit = unit, Kind = kind, Category = category, DisplayName = name, Blurb = blurb,
        GoodDirection = direction, Loinc = loinc, ValueLabels = labels, SexApplicability = sex,
      });

    // Activity
    Add("steps", "count", Interval, ShareCategory.Activity, "Steps", "Steps counted by your phone or watch, summed per day.", Up);
    Add("distance_m", "m", Interval, ShareCategory.Activity, "Distance", "Distance walked, run or cycled, in metres.", Up);
    Add("active_energy_kcal", "kcal", Interval, ShareCategory.Activity, "Active energy", "Energy burned through movement, on top of what your body uses at rest.", Up);
    Add("basal_energy_kcal", "kcal", Interval, ShareCategory.Activity, "Resting energy", "Energy your body uses just to keep going, as estimated by your device.", Neutral);
    Add("exercise_min", "min", Interval, ShareCategory.Activity, "Exercise minutes", "Minutes of movement brisk enough to count as exercise.", Up);
    Add("flights", "count", Interval, ShareCategory.Activity, "Flights climbed", "Flights of stairs climbed, as estimated by your device.", Up);

    // Vitals
    Add("heart_rate_bpm", "bpm", Instant, ShareCategory.Vitals, "Heart rate", "Beats per minute at the moment of the reading.", Range, "8867-4");
    Add("resting_hr_bpm", "bpm", Daily, ShareCategory.Vitals, "Resting heart rate", "Your heart rate when you are still and relaxed; lower usually means fitter.", Down, "40443-4");
    Add("hrv_sdnn_ms", "ms", Instant, ShareCategory.Vitals, "HRV (SDNN)", "Variation between heartbeats as measured by Apple Watch; higher tends to mean better recovery.", Up);
    Add("hrv_rmssd_ms", "ms", Instant, ShareCategory.Vitals, "HRV (RMSSD)", "Variation between heartbeats as measured by Oura and Android devices; higher tends to mean better recovery.", Up);
    Add("spo2_pct", "%", Instant, ShareCategory.Vitals, "Blood oxygen", "Percentage of oxygen carried in your blood.", Up, "59408-5");
    Add("respiratory_rate_bpm", "breaths/min", Instant, ShareCategory.Vitals, "Respiratory rate", "Breaths per minute, usually measured during sleep.", Range, "9279-1");
    Add("wrist_temp_delta_c", "C", Daily, ShareCategory.Vitals, "Wrist temperature change", "How much your overnight wrist temperature differed from your personal baseline.", Neutral);
    Add("body_temp_c", "C", Instant, ShareCategory.Vitals, "Body temperature", "Body temperature from a thermometer or device.", Range, "8310-5");
    Add("blood_glucose_mmol", "mmol/L", Instant, ShareCategory.Vitals, "Blood glucose", "Glucose in your blood, in mmol/L.", Range, "14749-6");
    Add("blood_pressure_sys", "mmHg", Instant, ShareCategory.Vitals, "Blood pressure (systolic)", "The upper blood pressure number, when the heart beats.", Range, "8480-6");
    Add("blood_pressure_dia", "mmHg", Instant, ShareCategory.Vitals, "Blood pressure (diastolic)", "The lower blood pressure number, between beats.", Range, "8462-4");
    Add("oura_readiness", "score", Daily, ShareCategory.Vitals, "Oura readiness", "Oura's daily readiness score, 0 to 100.", Up);
    Add("oura_sleep_score", "score", Daily, ShareCategory.Vitals, "Oura sleep score", "Oura's daily sleep score, 0 to 100.", Up);
    Add("oura_activity_score", "score", Daily, ShareCategory.Vitals, "Oura activity score", "Oura's daily activity score, 0 to 100.", Up);
    Add("oura_resilience", "level", Daily, ShareCategory.Vitals, "Oura resilience", "Oura's resilience level, from limited to exceptional.", Up, labels: ResilienceLabels);
    Add("oura_stress_high_s", "s", Daily, ShareCategory.Vitals, "Oura high stress time", "Seconds per day Oura judged you to be under high stress.", Down);

    // Sleep
    Add("sleep_stage", "stage", Interval, ShareCategory.Sleep, "Sleep stages", "Periods of in bed, awake, light, deep and REM sleep, summed into time asleep per night.", Neutral, labels: SleepStageLabels);

    // Body
    Add("vo2max", "mL/kg/min", Instant, ShareCategory.Body, "VO2 max", "An estimate of how much oxygen your body can use during hard exercise.", Up, "60842-2");
    Add("weight_kg", "kg", Instant, ShareCategory.Body, "Weight", "Body weight in kilograms.", Neutral, "29463-7");
    Add("body_fat_pct", "%", Instant, ShareCategory.Body, "Body fat", "Body fat as a percentage of weight, from a scale or device.", Neutral, "41982-0");
    Add("height_cm", "cm", Instant, ShareCategory.Body, "Height", "Height in centimetres.", Neutral, "8302-2");

    // Cycle
    Add("menstrual_flow", "level", Interval, ShareCategory.Cycle, "Menstrual flow", "Logged flow, from none to heavy.", Neutral, labels: MenstrualFlowLabels, sex: SexApplicabilities.Female);
    Add("basal_body_temp_c", "C", Instant, ShareCategory.Cycle, "Basal body temperature", "Temperature taken on waking, used to follow the cycle.", Neutral, "8310-5", sex: SexApplicabilities.Female);

    // Check-ins
    Add("checkin_mood", "level", Instant, ShareCategory.Checkins, "Mood", "How you said you felt at a check-in, from 1 to 6.", Up, labels: MoodLabels);

    // Labs (Package 4 writes these; listed here so the catalogue and the sharing rules are complete).
    Add("lab.total_testosterone", "ng/dL", Instant, ShareCategory.Labs, "Total testosterone", "All testosterone in the blood, bound and free.", Range, "2986-8");
    Add("lab.free_testosterone", "pg/mL", Instant, ShareCategory.Labs, "Free testosterone", "The small share of testosterone not bound to proteins and available to tissues.", Range, "2991-8");
    Add("lab.estradiol", "pg/mL", Instant, ShareCategory.Labs, "Estradiol", "The main form of oestrogen.", Range, "2243-4");
    Add("lab.progesterone", "ng/mL", Instant, ShareCategory.Labs, "Progesterone", "A hormone that rises after ovulation.", Range, "2839-9", sex: SexApplicabilities.Female);
    Add("lab.shbg", "nmol/L", Instant, ShareCategory.Labs, "SHBG", "The protein that carries sex hormones in the blood.", Range, "13967-5");
    Add("lab.cortisol", "ug/dL", Instant, ShareCategory.Labs, "Cortisol", "The main stress hormone; it is naturally highest in the morning.", Range, "2143-6");
    Add("lab.tsh", "mIU/L", Instant, ShareCategory.Labs, "TSH", "The signal from the pituitary that tells the thyroid how hard to work.", Range, "3016-3");
    Add("lab.free_t3", "pg/mL", Instant, ShareCategory.Labs, "Free T3", "The active thyroid hormone available to tissues.", Range, "3051-0");
    Add("lab.free_t4", "ng/dL", Instant, ShareCategory.Labs, "Free T4", "The thyroid hormone the body converts into T3.", Range, "3024-7");
    Add("lab.apob", "mg/dL", Instant, ShareCategory.Labs, "ApoB", "A count of the particles that carry cholesterol into artery walls.", Down, "1884-6");
    Add("lab.ldl", "mg/dL", Instant, ShareCategory.Labs, "LDL cholesterol", "Cholesterol carried in LDL particles.", Down, "2089-1");
    Add("lab.hdl", "mg/dL", Instant, ShareCategory.Labs, "HDL cholesterol", "Cholesterol carried in HDL particles.", Up, "2085-9");
    Add("lab.ldl_apob_ratio", "ratio", Instant, ShareCategory.Labs, "LDL to ApoB ratio", "LDL cholesterol divided by ApoB; a rough read on particle size.", Range);
    Add("lab.hscrp", "mg/L", Instant, ShareCategory.Labs, "hs-CRP", "A sensitive marker of inflammation in the body.", Down, "30522-7");
    Add("lab.total_cholesterol", "mg/dL", Instant, ShareCategory.Labs, "Total cholesterol", "All cholesterol in the blood added together.", Down, "2093-3");
    Add("lab.remnant_cholesterol", "mg/dL", Instant, ShareCategory.Labs, "Remnant cholesterol", "Total cholesterol minus LDL and HDL: what triglyceride-rich particles carry.", Down);
    Add("lab.tc_hdl_ratio", "ratio", Instant, ShareCategory.Labs, "Total to HDL ratio", "Total cholesterol divided by HDL.", Down, "9830-1");
    Add("lab.vitamin_d", "ng/mL", Instant, ShareCategory.Labs, "Vitamin D", "25-hydroxy vitamin D, the storage form measured in blood.", Range, "1989-3");
    Add("lab.ferritin", "ng/mL", Instant, ShareCategory.Labs, "Ferritin", "A measure of the iron your body has stored.", Range, "2276-4");
    Add("lab.fructosamine", "umol/L", Instant, ShareCategory.Labs, "Fructosamine", "Average blood sugar over the past two to three weeks.", Down, "15069-8");
    Add("lab.albumin", "g/dL", Instant, ShareCategory.Labs, "Albumin", "The main protein in blood, made by the liver.", Range, "1751-7");
    Add("lab.triglycerides", "mg/dL", Instant, ShareCategory.Labs, "Triglycerides", "Fat carried in the blood, often high after a meal.", Down, "2571-8");
    Add("lab.tg_hdl_ratio", "ratio", Instant, ShareCategory.Labs, "Triglyceride to HDL ratio", "Triglycerides divided by HDL; a rough read on insulin sensitivity.", Down);
    Add("lab.uric_acid", "mg/dL", Instant, ShareCategory.Labs, "Uric acid", "A waste product that can build up in joints when high.", Down, "3084-1");
    Add("lab.hematocrit", "%", Instant, ShareCategory.Labs, "Hematocrit", "The share of your blood made up of red cells.", Range, "4544-3");
    Add("lab.hemoglobin", "g/dL", Instant, ShareCategory.Labs, "Hemoglobin", "The protein in red cells that carries oxygen.", Range, "718-7");
    Add("lab.creatinine", "mg/dL", Instant, ShareCategory.Labs, "Creatinine", "A muscle waste product the kidneys clear; used to judge kidney function.", Range, "2160-0");
    Add("lab.alp", "U/L", Instant, ShareCategory.Labs, "ALP", "Alkaline phosphatase, an enzyme from liver and bone.", Range, "6768-6");
    Add("lab.ggt", "U/L", Instant, ShareCategory.Labs, "GGT", "A liver enzyme that rises with alcohol and some medicines.", Down, "2324-2");
    Add("lab.egfr", "mL/min/1.73m2", Instant, ShareCategory.Labs, "eGFR", "An estimate of how well the kidneys filter, from creatinine, age and sex.", Up, "62238-1");

    return list;
  }

  /// <summary>Renders the idempotent SQL seed. The migration file must equal this text exactly.</summary>
  public static string ToSql()
  {
    var sb = new StringBuilder();
    sb.Append("-- 0004_metric_seed: the metric catalogue from docs/ingest-contract.md plus the lab markers.\n");
    sb.Append("-- GENERATED from MetricSeed.cs (MetricSeedTests checks for drift). Edit the C# list, then regenerate.\n");
    sb.Append("-- Idempotent: re-running updates display text and classification but never removes a key.\n\n");
    sb.Append("INSERT INTO metric (key, unit, kind, category, display_name, blurb, good_direction, loinc, value_labels, sex_applicability) VALUES\n");
    for (var i = 0; i < All.Count; i++)
    {
      var m = All[i];
      sb.Append("  (")
        .Append(Lit(m.Key)).Append(", ")
        .Append(Lit(m.Unit)).Append(", ")
        .Append(Lit(m.Kind)).Append(", ")
        .Append(Lit(m.Category.ToString().ToLowerInvariant())).Append(", ")
        .Append(Lit(m.DisplayName)).Append(", ")
        .Append(Lit(m.Blurb)).Append(", ")
        .Append(Lit(m.GoodDirection)).Append(", ")
        .Append(m.Loinc == null ? "NULL" : Lit(m.Loinc)).Append(", ")
        .Append(m.ValueLabels == null ? "NULL" : Lit(m.ValueLabels) + "::jsonb").Append(", ")
        .Append(Lit(m.SexApplicability))
        .Append(i == All.Count - 1 ? ")\n" : "),\n");
    }
    sb.Append("ON CONFLICT (key) DO UPDATE SET\n");
    sb.Append("  unit = EXCLUDED.unit,\n");
    sb.Append("  kind = EXCLUDED.kind,\n");
    sb.Append("  category = EXCLUDED.category,\n");
    sb.Append("  display_name = EXCLUDED.display_name,\n");
    sb.Append("  blurb = EXCLUDED.blurb,\n");
    sb.Append("  good_direction = EXCLUDED.good_direction,\n");
    sb.Append("  loinc = EXCLUDED.loinc,\n");
    sb.Append("  value_labels = EXCLUDED.value_labels,\n");
    sb.Append("  sex_applicability = EXCLUDED.sex_applicability;\n");
    return sb.ToString();
  }

  private static string Lit(string value) => "'" + value.Replace("'", "''") + "'";
}
