using SelfKept.Core.Enums;

namespace SelfKept.Core.Entities;

/// <summary>
/// One row per catalogued metric (docs/ingest-contract.md). The key is the stable identifier the
/// clients post; unit is the stored unit; category decides which share grant a viewer needs.
/// Seeded by SQL migration and cached in memory by IMetricCatalogue.
/// </summary>
public class Metric
{
  public string Key { get; set; } = string.Empty;
  public string Unit { get; set; } = string.Empty;

  /// <summary>instant, interval or daily. See <see cref="MetricKinds"/>.</summary>
  public string Kind { get; set; } = MetricKinds.Instant;

  public ShareCategory Category { get; set; }
  public string DisplayName { get; set; } = string.Empty;
  public string Blurb { get; set; } = string.Empty;

  /// <summary>up, down, neutral or range. See <see cref="GoodDirections"/>.</summary>
  public string GoodDirection { get; set; } = GoodDirections.Neutral;

  public string? Loinc { get; set; }

  /// <summary>JSON object mapping categorical values to labels, for example {"0":"in_bed","1":"awake"}.</summary>
  public string? ValueLabels { get; set; }

  /// <summary>both, female or male. See <see cref="SexApplicabilities"/>.</summary>
  public string SexApplicability { get; set; } = SexApplicabilities.Both;

  public bool IsLab => Key.StartsWith(MetricKeys.LabPrefix, StringComparison.Ordinal);
}

public static class MetricKinds
{
  public const string Instant = "instant";
  public const string Interval = "interval";
  public const string Daily = "daily";
  public static readonly string[] All = [Instant, Interval, Daily];
}

public static class GoodDirections
{
  public const string Up = "up";
  public const string Down = "down";
  public const string Neutral = "neutral";
  public const string Range = "range";
  public static readonly string[] All = [Up, Down, Neutral, Range];
}

public static class SexApplicabilities
{
  public const string Both = "both";
  public const string Female = "female";
  public const string Male = "male";
  public static readonly string[] All = [Both, Female, Male];
}

/// <summary>Metric keys the server itself refers to.</summary>
public static class MetricKeys
{
  public const string LabPrefix = "lab.";
  public const string Steps = "steps";
  public const string DistanceM = "distance_m";
  public const string ActiveEnergyKcal = "active_energy_kcal";
  public const string ExerciseMin = "exercise_min";
  public const string Flights = "flights";
  public const string HeartRateBpm = "heart_rate_bpm";
  public const string RestingHrBpm = "resting_hr_bpm";
  public const string HrvSdnnMs = "hrv_sdnn_ms";
  public const string HrvRmssdMs = "hrv_rmssd_ms";
  public const string Spo2Pct = "spo2_pct";
  public const string RespiratoryRateBpm = "respiratory_rate_bpm";
  public const string WristTempDeltaC = "wrist_temp_delta_c";
  public const string WeightKg = "weight_kg";
  public const string SleepStage = "sleep_stage";
  public const string BloodGlucoseMmol = "blood_glucose_mmol";
  public const string CheckinMood = "checkin_mood";
  public const string OuraReadiness = "oura_readiness";
  public const string OuraSleepScore = "oura_sleep_score";
}

/// <summary>Sleep stage values carried in sample.value for sleep_stage (docs/ingest-contract.md).</summary>
public static class SleepStages
{
  public const int InBed = 0;
  public const int Awake = 1;
  public const int Light = 2;
  public const int Deep = 3;
  public const int Rem = 4;
  public const int Unspecified = 5;

  public static bool IsAsleep(double value) => value is Light or Deep or Rem or Unspecified;
}

/// <summary>Where a person's samples come from: one row per device install (or one system row for check-ins).</summary>
public class Source
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }

  /// <summary>healthkit, health_connect, oura, google_health, manual or lab_scan. See <see cref="SourceKinds"/>.</summary>
  public string Kind { get; set; } = SourceKinds.Manual;

  public string DeviceName { get; set; } = string.Empty;
  public string Platform { get; set; } = string.Empty;
  public string? AppVersion { get; set; }
  public bool Enabled { get; set; } = true;

  /// <summary>Opaque client resume token (HealthKit anchor, Health Connect changes token, or a timestamp).</summary>
  public string? Cursor { get; set; }

  public DateTime? LastSyncAt { get; set; }
  public DateTime? LastSampleAt { get; set; }
  public long SampleCount { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime? DeletedAt { get; set; }

  public bool IsDeleted => DeletedAt != null;
}

public static class SourceKinds
{
  public const string HealthKit = "healthkit";
  public const string HealthConnect = "health_connect";
  public const string Oura = "oura";
  public const string GoogleHealth = "google_health";
  public const string Manual = "manual";
  public const string LabScan = "lab_scan";
  public static readonly string[] All = [HealthKit, HealthConnect, Oura, GoogleHealth, Manual, LabScan];

  /// <summary>The per-person system source that check-ins are written under.</summary>
  public const string CheckinDeviceName = "Check-ins";
  public const string CheckinPlatform = "server";
}

/// <summary>
/// One reading of one metric. The table is range-partitioned by month on start_at, so the
/// database-level unique key is (source_id, source_uid, start_at); a source_uid names exactly one
/// reading with a fixed start, which keeps the contract's (sourceId, sourceUid) idempotency.
/// </summary>
public class Sample
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }
  public Guid SourceId { get; set; }
  public string SourceUid { get; set; } = string.Empty;
  public string MetricKey { get; set; } = string.Empty;
  public DateTime StartAt { get; set; }
  public DateTime? EndAt { get; set; }
  public double Value { get; set; }
  public string Unit { get; set; } = string.Empty;

  /// <summary>Small JSON object (jsonb). Null when the client sent none.</summary>
  public string? Meta { get; set; }

  public DateTime CreatedAt { get; set; }
}

public class Workout
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }
  public Guid SourceId { get; set; }
  public string SourceUid { get; set; } = string.Empty;
  public string Type { get; set; } = string.Empty;
  public DateTime StartAt { get; set; }
  public DateTime EndAt { get; set; }
  public double? DistanceM { get; set; }
  public double? EnergyKcal { get; set; }
  public double? AvgHr { get; set; }
  public double? MaxHr { get; set; }

  /// <summary>The GeoJSON route, encrypted with IEncryptionService (a route is a home address). Null when none.</summary>
  public byte[]? RouteCipher { get; set; }

  public string? Meta { get; set; }
  public DateTime CreatedAt { get; set; }
}

public class Checkin
{
  public Guid Id { get; set; }
  public Guid PersonId { get; set; }
  public DateTime At { get; set; }

  /// <summary>1 (low) to 6 (great).</summary>
  public short Mood { get; set; }

  /// <summary>The free-text note, encrypted with IEncryptionService. Null when none.</summary>
  public byte[]? NoteCipher { get; set; }

  public string[] Tags { get; set; } = [];
  public DateTime CreatedAt { get; set; }

  public const short MinMood = 1;
  public const short MaxMood = 6;
}

/// <summary>
/// One row per (person, metric, calendar day in the person's time zone). Derived by the server
/// from samples after every batch; never posted by clients.
/// </summary>
public class DailySummary
{
  public Guid PersonId { get; set; }
  public string MetricKey { get; set; } = string.Empty;
  public DateOnly Day { get; set; }
  public double Value { get; set; }
  public double? Min { get; set; }
  public double? Max { get; set; }
  public int Count { get; set; }
  public DateTime ComputedAt { get; set; }
}
