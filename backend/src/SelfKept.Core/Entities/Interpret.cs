namespace SelfKept.Core.Entities;

/// <summary>
/// One person's normal for one metric as of one day, computed nightly from daily_summary
/// (docs/insights-design.md). One row per day so history is replayable. Percentile fields are
/// null until there is at least one value; n7 and n28 count the days that had data.
/// </summary>
public class Baseline
{
  public Guid PersonId { get; set; }
  public string MetricKey { get; set; } = string.Empty;

  /// <summary>The local day the 28-day window ends on (inclusive).</summary>
  public DateOnly ComputedFor { get; set; }

  public double? Median7 { get; set; }
  public double? Median28 { get; set; }
  public double? P10_28 { get; set; }
  public double? P25_28 { get; set; }
  public double? P75_28 { get; set; }
  public double? P90_28 { get; set; }

  /// <summary>Least-squares slope per day over the 28 days, in metric units. Null with fewer than two points.</summary>
  public double? Slope28 { get; set; }

  public double? Sd28 { get; set; }
  public int N7 { get; set; }
  public int N28 { get; set; }
  public DateTime ComputedAt { get; set; }

  /// <summary>Days of data a metric needs before any rule may fire on it.</summary>
  public const int MinimumDays = 14;

  /// <summary>Days of data a metric needs before the Trends screen offers it.</summary>
  public const int TrendDays = 7;

  public bool IsEstablished => N28 >= MinimumDays;
}

/// <summary>Metrics that get a baseline (docs/insights-design.md, "Metrics with baselines in v0.1").</summary>
public static class BaselineMetrics
{
  public static readonly string[] Keys =
  [
    MetricKeys.SleepStage,
    MetricKeys.HrvSdnnMs,
    MetricKeys.HrvRmssdMs,
    MetricKeys.RestingHrBpm,
    MetricKeys.RespiratoryRateBpm,
    MetricKeys.Spo2Pct,
    MetricKeys.WristTempDeltaC,
    MetricKeys.Steps,
    MetricKeys.ActiveEnergyKcal,
    MetricKeys.ExerciseMin,
    MetricKeys.WeightKg,
    MetricKeys.CheckinMood,
    MetricKeys.OuraReadiness,
    MetricKeys.OuraSleepScore,
  ];

  public static bool Has(string metricKey) => Array.IndexOf(Keys, metricKey) >= 0;
}

/// <summary>
/// A card: one rule's finding for one person on one day. Created once per (person, kind, day)
/// and re-ranked, never duplicated, when the rule keeps firing. generated is the model's
/// rewording when a model is configured and its output passed validation; the templated text
/// always exists.
/// </summary>
public class Insight
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }
  public DateOnly Day { get; set; }

  /// <summary>See <see cref="InsightKinds"/>.</summary>
  public string Kind { get; set; } = string.Empty;

  /// <summary>notice, watch or clinician. See <see cref="InsightSeverities"/>.</summary>
  public string Severity { get; set; } = InsightSeverities.Notice;

  public string[] Metrics { get; set; } = [];

  /// <summary>JSON object with the numbers the rule used, including bandDistance for ranking.</summary>
  public string Evidence { get; set; } = "{}";

  public string Templated { get; set; } = string.Empty;
  public string? Generated { get; set; }
  public string? Model { get; set; }
  public DateTime CreatedAt { get; set; }

  /// <summary>First time the card appeared on Today. Null for cards that only live in the archive.</summary>
  public DateTime? ShownAt { get; set; }

  public DateTime? DismissedAt { get; set; }

  /// <summary>helpful, knew or wrong. See <see cref="InsightReactions"/>.</summary>
  public string? Reaction { get; set; }

  public string Text => Generated ?? Templated;
}

public static class InsightKinds
{
  public const string HrvLowRun = "hrv_low_run";
  public const string HrvRecovered = "hrv_recovered";
  public const string RhrElevated = "rhr_elevated";
  public const string IllnessPattern = "illness_pattern";
  public const string SleepDebt = "sleep_debt";
  public const string SleepGoodWeek = "sleep_good_week";
  public const string ActivityDip = "activity_dip";
  public const string TrainingSpike = "training_spike";
  public const string WeightTrend = "weight_trend";
  public const string MoodLowRun = "mood_low_run";
  public const string MoodSleepLink = "mood_sleep_link";
  public const string Spo2Low = "spo2_low";
  public const string LabOutOfRange = "lab_out_of_range";
  public const string LabTrend = "lab_trend";
  public const string Learning = "learning";
}

public static class InsightSeverities
{
  public const string Notice = "notice";
  public const string Watch = "watch";
  public const string Clinician = "clinician";
  public static readonly string[] All = [Notice, Watch, Clinician];

  /// <summary>Higher ranks first: clinician, then watch, then notice.</summary>
  public static int Rank(string severity) => severity switch
  {
    Clinician => 2,
    Watch => 1,
    _ => 0,
  };
}

public static class InsightReactions
{
  public const string Helpful = "helpful";
  public const string Knew = "knew";
  public const string Wrong = "wrong";
  public static readonly string[] All = [Helpful, Knew, Wrong];

  /// <summary>A kind marked wrong this many times in <see cref="DemotionWindowDays"/> stays off Today.</summary>
  public const int DemotionCount = 2;
  public const int DemotionWindowDays = 30;
}

/// <summary>
/// One model call: that it happened, for whom, how long it took and whether it worked. Never
/// the prompt or the completion text; prompt_hash lets identical prompts be spotted.
/// </summary>
public class AiCall
{
  public Guid Id { get; set; }
  public Guid? PersonId { get; set; }

  /// <summary>insight or today. See <see cref="AiPurposes"/>.</summary>
  public string Purpose { get; set; } = string.Empty;

  public string PromptHash { get; set; } = string.Empty;
  public int? PromptTokens { get; set; }
  public int? CompletionTokens { get; set; }
  public int LatencyMs { get; set; }
  public bool Ok { get; set; }

  /// <summary>Short failure reason (exception type, HTTP status or the validator's verdict). Null when ok.</summary>
  public string? Error { get; set; }

  public DateTime CreatedAt { get; set; }
}

public static class AiPurposes
{
  public const string Insight = "insight";
  public const string Today = "today";
}

/// <summary>The Today sentence, composed on request and kept for an hour unless new data arrives.</summary>
public class TodayCache
{
  public Guid PersonId { get; set; }
  public DateTime ComputedAt { get; set; }
  public string Sentence { get; set; } = string.Empty;

  /// <summary>JSON: the facts the sentence was built from and whether a model wrote it.</summary>
  public string Payload { get; set; } = "{}";

  public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(60);
}
