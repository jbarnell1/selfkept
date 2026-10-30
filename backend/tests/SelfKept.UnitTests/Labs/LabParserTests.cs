using FluentAssertions;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Services.Labs;
using SelfKept.UnitTests.Collect;
using Xunit;

namespace SelfKept.UnitTests.Labs;

/// <summary>The five parsing steps of docs/labs-design.md over text in the shape of the Rythm results screen.</summary>
public class LabParserTests
{
  private static readonly LabParser Parser = new(new SeedCatalogue(), LabSeed.Aliases);

  [Fact]
  public void Parse_FullRythmPanel_MatchesAllThirtyMarkers_WithUnitsAndFlags()
  {
    var result = Parser.Parse(RythmText.FullPanel);

    result.Candidates.Should().HaveCount(30);
    result.Candidates.Select(c => c.MetricKey).Should().OnlyHaveUniqueItems();
    result.Candidates.Select(c => c.MetricKey).Should().BeEquivalentTo(MetricSeed.All.Where(m => m.IsLab).Select(m => m.Key));
    result.Candidates.Should().OnlyContain(c => c.Confidence == LabParser.ConfidenceFull, "every line carries a unit (ratios imply theirs)");

    var byKey = result.Candidates.ToDictionary(c => c.MetricKey);
    byKey["lab.apob"].Value.Should().Be(128);
    byKey["lab.apob"].Unit.Should().Be("mg/dL");
    byKey["lab.apob"].Flag.Should().Be("high");
    byKey["lab.apob"].Line.Should().Be(15);
    byKey["lab.vitamin_d"].Flag.Should().Be("low");
    byKey["lab.hdl"].Flag.Should().BeNull();
    byKey["lab.progesterone"].Value.Should().Be(0.2, "a '<' bound keeps the number");
    byKey["lab.shbg"].Value.Should().Be(42.5);
    byKey["lab.hscrp"].Unit.Should().Be("mg/L");
    byKey["lab.hematocrit"].Unit.Should().Be("%");
    byKey["lab.egfr"].Unit.Should().Be("mL/min/1.73m2");
    byKey["lab.ldl_apob_ratio"].Value.Should().Be(1.02);
    byKey["lab.ldl_apob_ratio"].Unit.Should().Be("ratio");
    byKey["lab.tc_hdl_ratio"].Value.Should().Be(4.4);
    byKey["lab.tg_hdl_ratio"].Value.Should().Be(3.4);
    byKey["lab.total_testosterone"].Value.Should().Be(540);
    byKey["lab.free_testosterone"].Value.Should().Be(98);
    byKey["lab.total_cholesterol"].Value.Should().Be(212);
    byKey["lab.remnant_cholesterol"].Value.Should().Be(33);
    byKey["lab.free_t3"].Value.Should().Be(3.1);
    byKey["lab.free_t4"].Value.Should().Be(1.2);
    byKey["lab.apob"].DisplayName.Should().Be("ApoB");

    result.UnmatchedLines.Should().BeEquivalentTo(new[] { "Rythm Health", "Monthly panel", "Collected 20 Oct 2026", "Hormones", "Thyroid", "Heart", "Nutrients", "Blood", "Kidney", "Liver" });
  }

  [Fact]
  public void Parse_ValueOnTheNextLine_IsReadWithLowConfidence_AndAUnitLineAfterThat()
  {
    var text = "ApoB\n92 mg/dL\nLDL cholesterol\n101\nmg/dL\nHDL cholesterol\n\n55 mg/dL";

    var result = Parser.Parse(text);

    result.Candidates.Should().HaveCount(3);
    var apob = result.Candidates.Single(c => c.MetricKey == "lab.apob");
    apob.Value.Should().Be(92);
    apob.Unit.Should().Be("mg/dL");
    apob.Confidence.Should().Be(LabParser.ConfidenceNextLine);
    apob.Line.Should().Be(0);
    apob.RawText.Should().Be("ApoB | 92 mg/dL");
    var ldl = result.Candidates.Single(c => c.MetricKey == "lab.ldl");
    ldl.Value.Should().Be(101);
    ldl.Unit.Should().Be("mg/dL", "a unit-only line after the value is picked up");
    ldl.Confidence.Should().Be(LabParser.ConfidenceNextLine);
    result.Candidates.Single(c => c.MetricKey == "lab.hdl").Value.Should().Be(55, "blank lines are skipped");
    result.UnmatchedLines.Should().BeEmpty();
  }

  [Fact]
  public void Parse_ThousandsSeparatorsAndDecimalCommas_AreNormalised()
  {
    var text = "Ferritin 1,250 ng/mL H\nTSH 2,1 mIU/L\nTotal testosterone   1,005 ng/dL  H\nSteps today 12,345";

    var result = Parser.Parse(text);

    result.Candidates.Single(c => c.MetricKey == "lab.ferritin").Value.Should().Be(1250);
    result.Candidates.Single(c => c.MetricKey == "lab.ferritin").Flag.Should().Be("high");
    result.Candidates.Single(c => c.MetricKey == "lab.tsh").Value.Should().Be(2.1);
    result.Candidates.Single(c => c.MetricKey == "lab.total_testosterone").Value.Should().Be(1005);
    result.Candidates.Single(c => c.MetricKey == "lab.total_testosterone").RawText.Should().Be("Total testosterone 1005 ng/dL H", "spaces collapse and separators go");
    result.UnmatchedLines.Should().Equal("Steps today 12345");
  }

  [Fact]
  public void Parse_MmolCholesterolPanel_KeepsTheConvertibleUnit_ForTheReportToConvert()
  {
    var text = "Total cholesterol 5.2 mmol/L\nLDL cholesterol 3.1 mmol/L H\nHDL cholesterol 1.4 mmol/L\nTriglycerides 1.7 mmol/L";

    var result = Parser.Parse(text);

    result.Candidates.Should().HaveCount(4);
    result.Candidates.Should().OnlyContain(c => c.Unit == "mmol/L" && c.Confidence == LabParser.ConfidenceFull);
    result.Candidates.Single(c => c.MetricKey == "lab.ldl").Value.Should().Be(3.1);
    result.Candidates.Single(c => c.MetricKey == "lab.ldl").Flag.Should().Be("high");
  }

  [Fact]
  public void Parse_UnitThatIsNotAcceptedForTheMarker_LeavesUnitNull_AndLowersConfidence()
  {
    var result = Parser.Parse("TSH 2.1 mmol/L\nApoB 128");

    result.Candidates.Single(c => c.MetricKey == "lab.tsh").Unit.Should().BeNull();
    result.Candidates.Single(c => c.MetricKey == "lab.tsh").Confidence.Should().Be(LabParser.ConfidenceNoUnit);
    result.Candidates.Single(c => c.MetricKey == "lab.apob").Confidence.Should().Be(LabParser.ConfidenceNoUnit);
  }

  [Theory]
  [InlineData("Apolipoprotein B 92 mg/dL", "lab.apob", 92)]
  [InlineData("APO B: 92 mg/dL", "lab.apob", 92)]
  [InlineData("Cholesterol, LDL 112 mg/dL", "lab.ldl", 112)]
  [InlineData("Cholesterol, Total 180 mg/dL", "lab.total_cholesterol", 180)]
  [InlineData("Cholesterol 180 mg/dL", "lab.total_cholesterol", 180)]
  [InlineData("Testosterone 540 ng/dL", "lab.total_testosterone", 540)]
  [InlineData("Testosterone, Free 98 pg/mL", "lab.free_testosterone", 98)]
  [InlineData("25-OH Vitamin D 42 ng/mL", "lab.vitamin_d", 42)]
  [InlineData("Vitamin D (25-OH) 42 ng/mL", "lab.vitamin_d", 42)]
  [InlineData("25-Hydroxyvitamin D 42 ng/mL", "lab.vitamin_d", 42)]
  [InlineData("High sensitivity CRP 1.3 mg/L", "lab.hscrp", 1.3)]
  [InlineData("C-reactive protein, high sensitivity 1.3 mg/L", "lab.hscrp", 1.3)]
  [InlineData("hsCRP 1.3 mg/L", "lab.hscrp", 1.3)]
  [InlineData("Estimated GFR >90 mL/min/1.73m2", "lab.egfr", 90)]
  [InlineData("Thyroid stimulating hormone 2.1 uIU/mL", "lab.tsh", 2.1)]
  [InlineData("FT3 3.1 pg/mL", "lab.free_t3", 3.1)]
  [InlineData("T4, Free 1.2 ng/dL", "lab.free_t4", 1.2)]
  [InlineData("Hct 44 %", "lab.hematocrit", 44)]
  [InlineData("Hgb 15.1 g/dL", "lab.hemoglobin", 15.1)]
  [InlineData("Gamma GT 44 IU/L", "lab.ggt", 44)]
  [InlineData("Alkaline phosphatase 70 U/L", "lab.alp", 70)]
  [InlineData("Sex hormone binding globulin 42.5 nmol/L", "lab.shbg", 42.5)]
  [InlineData("Your ApoB came back at 92 mg/dL", "lab.apob", 92)]
  [InlineData("ApoB H 128 mg/dL", "lab.apob", 128)]
  public void Parse_Synonym_MapsToTheCatalogueKey(string line, string expectedKey, double expectedValue)
  {
    var result = Parser.Parse(line);

    result.Candidates.Should().ContainSingle();
    result.Candidates[0].MetricKey.Should().Be(expectedKey);
    result.Candidates[0].Value.Should().Be(expectedValue);
  }

  [Fact]
  public void Parse_FlagBeforeTheValue_IsKept()
  {
    Parser.Parse("ApoB H 128 mg/dL").Candidates[0].Flag.Should().Be("high");
  }

  [Fact]
  public void Parse_SameMarkerTwice_KeepsTheFirst_AndLeavesTheSecondUnmatched()
  {
    var result = Parser.Parse("ApoB 92 mg/dL\nApoB 95 mg/dL");

    result.Candidates.Should().ContainSingle().Which.Value.Should().Be(92);
    result.UnmatchedLines.Should().Equal("ApoB 95 mg/dL");
  }

  [Fact]
  public void Parse_EmptyOrNoiseOnly_ReturnsNoCandidates()
  {
    Parser.Parse(string.Empty).Candidates.Should().BeEmpty();
    Parser.Parse("   \n\n").Candidates.Should().BeEmpty();
    var noise = Parser.Parse("Dear patient,\nYour results are below.\nALP");
    noise.Candidates.Should().BeEmpty("ALP alone, with no number anywhere after it, is not a value");
    noise.UnmatchedLines.Should().HaveCount(3);
  }
}
