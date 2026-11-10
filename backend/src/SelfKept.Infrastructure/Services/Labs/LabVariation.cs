using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Services.Labs;

/// <summary>One marker's within-person biological variation (CVi, percent) and where the figure comes from.</summary>
public sealed record MarkerVariation(string MetricKey, double CviPercent, string Source, string SourceUrl, string? Sex = null, string? Caveat = null);

/// <summary>
/// How much a marker varies between two tests in the same healthy person with nothing changed
/// (docs/cross-reference.md, "Test-to-test variation"). Only figures read at a published source
/// are here; a marker without one is judged "cannot tell", never against a guess.
///
/// The reference change value is RCV = 2.77 x sqrt(CVa^2 + CVi^2), where 2.77 is 1.96 x sqrt(2)
/// (two tests, 95 percent). CVa is the laboratory's analytical variation. No analytical figure has
/// been checked for the labs our users see, so CVa is taken as 0 and the RCV here is a lower
/// bound: the lab's own imprecision would widen it.
/// </summary>
public static class LabVariation
{
  /// <summary>1.96 x sqrt(2), rounded as published.</summary>
  public const double RcvFactor = 2.77;

  /// <summary>No analytical variation is verified, so none is added (see the summary).</summary>
  public const double AnalyticalCvPercent = 0;

  private const string Carobene2017Enzymes = "Carobene 2017, Clin Chem 63(6):1141-1150 (EuBIVAS)";
  private const string Carobene2017EnzymesUrl = "https://pubmed.ncbi.nlm.nih.gov/28428356/";
  private const string Bottani = "Bottani 2022, Clin Chem Lab Med 60(4):523-532 (EuBIVAS)";
  private const string BottaniUrl = "https://pubmed.ncbi.nlm.nih.gov/33561908/";
  private const string Ricos = "Ricos 2020, Adv Lab Med 1(3):20200029";
  private const string RicosUrl = "https://pubmed.ncbi.nlm.nih.gov/37361503/";

  public static readonly IReadOnlyList<MarkerVariation> All =
  [
    new(LabMetricKeys.TotalTestosterone, 10.0, "Itkonen 2024, Clin Chim Acta 555:117806", "https://pubmed.ncbi.nlm.nih.gov/38341016/",
      Sex: SexApplicabilities.Male, Caveat: "Measured in men; no figure for women is held."),
    new(LabMetricKeys.VitaminD, 6.3, "Cavalier 2021, Nutrients 13(2):431", "https://pmc.ncbi.nlm.nih.gov/articles/PMC7911223/",
      Caveat: "After outlier removal. The authors note people were not in a steady state and judge the usual model a poor fit for vitamin D."),
    new(LabMetricKeys.Ferritin, 16.9, "Ozkanay 2023, Scand J Clin Lab Invest", "https://pubmed.ncbi.nlm.nih.gov/37961767/",
      Caveat: "Small single-centre study, 22 volunteers."),
    new(LabMetricKeys.Tsh, 17.7, Bottani, BottaniUrl),
    new(LabMetricKeys.FreeT3, 5.0, Bottani, BottaniUrl),
    new(LabMetricKeys.FreeT4, 4.8, Bottani, BottaniUrl),
    new(LabMetricKeys.Alt, 9.3, Carobene2017Enzymes, Carobene2017EnzymesUrl),
    new(LabMetricKeys.Ast, 9.5, Carobene2017Enzymes, Carobene2017EnzymesUrl),
    new(LabMetricKeys.Ggt, 8.9, Carobene2017Enzymes, Carobene2017EnzymesUrl),
    new(LabMetricKeys.Alp, 5.3, Carobene2017Enzymes, Carobene2017EnzymesUrl),
    new(LabMetricKeys.Creatinine, 4.4, "Carobene 2017, Clin Chem 63(9):1527-1536 (EuBIVAS, enzymatic method)", "https://pubmed.ncbi.nlm.nih.gov/28720681/"),
    new(LabMetricKeys.HbA1c, 1.2, Ricos, RicosUrl),
    new(LabMetricKeys.FastingGlucose, 5.0, Ricos, RicosUrl),
  ];

  private static readonly Dictionary<string, MarkerVariation> ByKey = All.ToDictionary(v => v.MetricKey, StringComparer.Ordinal);

  /// <summary>The figure that applies to this person, or null when none is verified (or it was measured in the other sex).</summary>
  public static MarkerVariation? For(string metricKey, Core.Enums.SexAtBirth sex)
  {
    if (!ByKey.TryGetValue(metricKey, out var v)) return null;
    if (v.Sex == SexApplicabilities.Male && sex != Core.Enums.SexAtBirth.Male) return null;
    if (v.Sex == SexApplicabilities.Female && sex != Core.Enums.SexAtBirth.Female) return null;
    return v;
  }

  /// <summary>The reference change value in percent: 2.77 x sqrt(CVa^2 + CVi^2).</summary>
  public static double Rcv(double cviPercent, double cvaPercent = AnalyticalCvPercent) =>
    RcvFactor * Math.Sqrt(cvaPercent * cvaPercent + cviPercent * cviPercent);

  /// <summary>The change from previous to latest as a percent of the previous value, or null when the previous is zero.</summary>
  public static double? ChangePercent(double previous, double latest) =>
    previous > 0 ? (latest - previous) / previous * 100 : null;
}
