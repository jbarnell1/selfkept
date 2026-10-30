using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Infrastructure.Services.Labs;
using Xunit;

namespace SelfKept.UnitTests.Labs;

public class LabRatioTests
{
  private static readonly Guid Report = Guid.NewGuid();
  private static readonly Guid Person = Guid.NewGuid();
  private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

  private static LabResult R(string key, double value) => new() { Id = Guid.NewGuid(), ReportId = Report, PersonId = Person, MetricKey = key, Value = value, Unit = "mg/dL", CreatedAt = Now };

  [Fact]
  public void DeriveRatios_AllComponentsPresent_ProducesTheThreeRatios_MarkedDerived()
  {
    var results = new[] { R("lab.ldl", 131), R("lab.apob", 128), R("lab.total_cholesterol", 212), R("lab.hdl", 48), R("lab.triglycerides", 165) };

    var derived = LabService.DeriveRatios(results, Report, Person, Now);

    derived.Should().HaveCount(3);
    derived.Should().OnlyContain(d => d.Derived && d.Unit == "ratio" && d.Flag == "none" && d.ReportId == Report && d.PersonId == Person);
    derived.Single(d => d.MetricKey == "lab.ldl_apob_ratio").Value.Should().Be(1.02);
    derived.Single(d => d.MetricKey == "lab.tc_hdl_ratio").Value.Should().Be(4.42);
    derived.Single(d => d.MetricKey == "lab.tg_hdl_ratio").Value.Should().Be(3.44);
  }

  [Fact]
  public void DeriveRatios_RatioSupplied_IsNotOverwritten()
  {
    var results = new[] { R("lab.ldl", 131), R("lab.apob", 128), R("lab.ldl_apob_ratio", 1.1) };

    LabService.DeriveRatios(results, Report, Person, Now).Should().BeEmpty();
  }

  [Fact]
  public void DeriveRatios_ComponentMissingOrZero_SkipsThatRatio()
  {
    var results = new[] { R("lab.ldl", 131), R("lab.total_cholesterol", 212), R("lab.hdl", 0), R("lab.triglycerides", 165) };

    LabService.DeriveRatios(results, Report, Person, Now).Should().BeEmpty("no ApoB, and HDL of zero cannot divide");
  }

  [Theory]
  [InlineData(null, "none")]
  [InlineData("", "none")]
  [InlineData("H", "high")]
  [InlineData("high", "high")]
  [InlineData("↑", "high")]
  [InlineData("L", "low")]
  [InlineData("Low", "low")]
  [InlineData("N", "normal")]
  [InlineData("in range", "normal")]
  [InlineData("none", "none")]
  public void NormaliseFlag_AcceptsTheUsualSpellings(string? input, string expected)
  {
    LabService.NormaliseFlag(input, "lab.apob").Should().Be(expected);
  }

  [Fact]
  public void NormaliseFlag_UnknownWord_IsRejected()
  {
    var act = () => LabService.NormaliseFlag("meh", "lab.apob");
    act.Should().Throw<DomainValidationException>().WithMessage("*lab.apob*");
  }

  [Fact]
  public void NoonLocal_IsNoonInThePersonsZone()
  {
    var zone = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    LabService.NoonLocal(new DateOnly(2026, 10, 20), zone).Should().Be(new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc));
    LabService.NoonLocal(new DateOnly(2026, 10, 20), TimeZoneInfo.Utc).Should().Be(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
  }
}
