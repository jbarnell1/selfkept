using FluentAssertions;
using SelfKept.Infrastructure.Services.Collect;
using Xunit;

namespace SelfKept.UnitTests.Collect;

public class UnitConversionTests
{
  private static readonly SeedCatalogue Catalogue = new();

  [Theory]
  [InlineData("heart_rate_bpm", "count/min", 60, 60)]
  [InlineData("distance_m", "mi", 1, 1609.344)]
  [InlineData("distance_m", "km", 2.5, 2500)]
  [InlineData("weight_kg", "lb", 100, 45.359237)]
  [InlineData("height_cm", "in", 10, 25.4)]
  [InlineData("body_temp_c", "F", 98.6, 37)]
  [InlineData("wrist_temp_delta_c", "F", 0.9, 0.5)]
  [InlineData("blood_glucose_mmol", "mg/dL", 100, 5.55)]
  public void Convert_AllowlistedUnit_ConvertsToStoredUnit(string metric, string unit, double value, double expected)
  {
    var result = UnitConversion.Convert(Catalogue.Find(metric)!, unit, value);

    result.Should().NotBeNull().And.BeApproximately(expected, 0.0001);
  }

  [Theory]
  [InlineData("lab.total_cholesterol", "mmol/L", 5.2, 201.084)]
  [InlineData("lab.ldl", "mmol/L", 3.1, 119.877)]
  [InlineData("lab.hdl", "mmol/L", 1.4, 54.138)]
  [InlineData("lab.remnant_cholesterol", "mmol/L", 0.8, 30.936)]
  [InlineData("lab.triglycerides", "mmol/L", 1.7, 150.569)]
  [InlineData("lab.apob", "g/L", 0.92, 92)]
  [InlineData("lab.total_testosterone", "nmol/L", 18, 519.12)]
  [InlineData("lab.free_testosterone", "pmol/L", 300, 86.52)]
  [InlineData("lab.free_t3", "pmol/L", 5, 3.255)]
  [InlineData("lab.estradiol", "pmol/L", 100, 27.24)]
  [InlineData("lab.progesterone", "nmol/L", 10, 3.145)]
  [InlineData("lab.cortisol", "nmol/L", 400, 14.5)]
  [InlineData("lab.free_t4", "pmol/L", 15, 1.1655)]
  [InlineData("lab.vitamin_d", "nmol/L", 50, 20)]
  [InlineData("lab.ferritin", "ug/L", 120, 120)]
  [InlineData("lab.hscrp", "mg/dL", 0.3, 3)]
  [InlineData("lab.albumin", "g/L", 43, 4.3)]
  [InlineData("lab.hemoglobin", "g/L", 151, 15.1)]
  [InlineData("lab.creatinine", "umol/L", 88, 0.9944)]
  [InlineData("lab.uric_acid", "umol/L", 400, 6.72)]
  public void Convert_LabUnit_ConvertsToTheCatalogueUnit(string metric, string unit, double value, double expected)
  {
    var result = UnitConversion.Convert(Catalogue.Find(metric)!, unit, value);

    result.Should().NotBeNull().And.BeApproximately(expected, 0.001);
  }

  [Theory]
  [InlineData("lab.cortisol", "mcg/dL", 14.2)]
  [InlineData("lab.cortisol", "µg/dL", 14.2)]
  [InlineData("lab.fructosamine", "µmol/L", 240)]
  [InlineData("lab.tsh", "uIU/mL", 2.1)]
  [InlineData("lab.alp", "IU/L", 70)]
  [InlineData("lab.egfr", "mL/min/1.73 m²", 95)]
  [InlineData("lab.hematocrit", "%", 44)]
  [InlineData("lab.ferritin", "ng/mL", 120)]
  public void Convert_LabUnitSpelledLikeALab_IsTheCatalogueUnit(string metric, string unit, double value)
  {
    UnitConversion.Convert(Catalogue.Find(metric)!, unit, value).Should().Be(value);
    UnitConversion.Accepts(Catalogue.Find(metric)!, unit).Should().BeTrue();
  }

  [Theory]
  [InlineData("lab.tsh", "mmol/L")]
  [InlineData("lab.apob", "nmol/L")]
  [InlineData("lab.ldl", "g/L")]
  [InlineData("lab.egfr", "mg/dL")]
  public void Convert_LabUnitNotOnTheAllowlist_IsRejected(string metric, string unit)
  {
    UnitConversion.Convert(Catalogue.Find(metric)!, unit, 1).Should().BeNull();
    UnitConversion.Accepts(Catalogue.Find(metric)!, unit).Should().BeFalse();
  }

  [Theory]
  [InlineData("steps", "count", 1200)]
  [InlineData("heart_rate_bpm", "BPM", 58)]
  [InlineData("spo2_pct", "%", 97)]
  [InlineData("body_temp_c", "°C", 36.8)]
  public void Convert_StoredUnitInAnyCase_PassesThrough(string metric, string unit, double value)
  {
    UnitConversion.Convert(Catalogue.Find(metric)!, unit, value).Should().Be(value);
  }

  [Theory]
  [InlineData("steps", "km")]
  [InlineData("weight_kg", "stone")]
  [InlineData("heart_rate_bpm", "ms")]
  [InlineData("weight_kg", "mg/dL")]
  [InlineData("hrv_sdnn_ms", "mg/dL")]
  public void Convert_UnitNotOnTheAllowlist_IsRejected(string metric, string unit)
  {
    UnitConversion.Convert(Catalogue.Find(metric)!, unit, 1).Should().BeNull();
  }
}
