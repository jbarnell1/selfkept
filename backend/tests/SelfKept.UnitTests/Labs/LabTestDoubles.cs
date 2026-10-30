using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;

namespace SelfKept.UnitTests.Labs;

/// <summary>The reference table over the seed list, so tests need no database.</summary>
public sealed class SeedLabReferences : ILabReferenceRepository
{
  private readonly Dictionary<string, List<LabReference>> _byKey =
    LabSeed.References.GroupBy(r => r.MetricKey).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

  public IReadOnlyList<LabReference> All => LabSeed.References;

  public IReadOnlyList<LabReference> RowsFor(string metricKey) => _byKey.TryGetValue(metricKey, out var rows) ? rows : [];

  public LabReference? Find(Metric metric, SexAtBirth sex) => LabReferenceLookup.Resolve(RowsFor(metric.Key), metric, sex);

  public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class SeedAliases : IMarkerAliasRepository
{
  public IReadOnlyList<LabMarkerAlias> All => LabSeed.Aliases;

  public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>A realistic text block in the shape of the Rythm results screen, one line per row, with units and flags.</summary>
public static class RythmText
{
  public const string FullPanel = """
    Rythm Health
    Monthly panel
    Collected 20 Oct 2026
    Hormones
    Total testosterone 540 ng/dL
    Free testosterone 98 pg/mL
    Estradiol 28 pg/mL
    Progesterone <0.2 ng/mL
    SHBG 42.5 nmol/L
    Cortisol 14.2 ug/dL
    Thyroid
    TSH 2.1 mIU/L
    Free T3 3.1 pg/mL
    Free T4 1.2 ng/dL
    Heart
    ApoB 128 mg/dL H
    LDL cholesterol 131 mg/dL H
    HDL cholesterol 48 mg/dL
    LDL/ApoB ratio 1.02
    hs-CRP 4.1 mg/L H
    Total cholesterol 212 mg/dL H
    Remnant cholesterol 33 mg/dL
    TC/HDL ratio 4.4
    Triglycerides 165 mg/dL H
    TG/HDL ratio 3.4
    Nutrients
    Vitamin D 18 ng/mL L
    Ferritin 95 ng/mL
    Fructosamine 240 umol/L
    Albumin 4.3 g/dL
    Uric acid 6.9 mg/dL
    Blood
    Hematocrit 44 %
    Hemoglobin 15.1 g/dL
    Kidney
    Creatinine 1.0 mg/dL
    eGFR 95 mL/min/1.73m2
    Liver
    ALP 70 U/L
    GGT 44 U/L
    """;
}
