using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SelfKept.Core.Configuration;
using SelfKept.Core.Entities;
using SelfKept.Core.Formatting;

namespace SelfKept.Infrastructure.Services.Labs;

public sealed record LabSummaryTrendRow(string Label, string Unit, int Days, double Median, double Min, double Max);

public sealed record LabSummaryLabRow(string Marker, string Unit, double? Latest, string LatestFlag, double? Previous, string PreviousFlag, string UsualRange);

public sealed record LabSummaryModel(
  string PersonName,
  DateOnly From,
  DateOnly To,
  IReadOnlyList<LabSummaryTrendRow> Trends,
  DateOnly? LatestPanel,
  DateOnly? PreviousPanel,
  IReadOnlyList<LabSummaryLabRow> Labs,
  DateOnly GeneratedOn);

/// <summary>
/// The clinician summary: page one is a trends table over the date range (median and range per
/// metric), page two the latest two panels side by side with flags. Brand name from BrandOptions;
/// no value leaves this process except in the bytes returned.
/// </summary>
public sealed class LabSummaryPdfRenderer
{
  /// <summary>The daily_summary metrics on the trends page, in order, with their labels.</summary>
  public static readonly IReadOnlyList<(string MetricKey, string Label)> TrendMetrics =
  [
    (MetricKeys.SleepStage, "Sleep"),
    (MetricKeys.HrvRmssdMs, "HRV (RMSSD)"),
    (MetricKeys.HrvSdnnMs, "HRV (SDNN)"),
    (MetricKeys.RestingHrBpm, "Resting heart rate"),
    (MetricKeys.Steps, "Steps"),
    (MetricKeys.WeightKg, "Weight"),
  ];

  private readonly BrandOptions _brand;

  static LabSummaryPdfRenderer()
  {
    QuestPDF.Settings.License = LicenseType.Community;
  }

  public LabSummaryPdfRenderer(BrandOptions brand)
  {
    _brand = brand;
  }

  public byte[] Render(LabSummaryModel model)
  {
    var document = Document.Create(container =>
    {
      container.Page(page =>
      {
        Frame(page, model, "Trends");
        page.Content().PaddingVertical(12).Column(column =>
        {
          column.Spacing(8);
          column.Item().Text($"Daily values from {Iso(model.From)} to {Iso(model.To)}: median and range of the days with data.").FontSize(10);
          if (model.Trends.Count == 0)
          {
            column.Item().Text("No daily data in this range.").Italic().FontSize(10);
            return;
          }
          column.Item().Table(table =>
          {
            table.ColumnsDefinition(columns =>
            {
              columns.RelativeColumn(3);
              columns.RelativeColumn(1.4f);
              columns.RelativeColumn(1);
              columns.RelativeColumn(1.4f);
              columns.RelativeColumn(2.2f);
            });
            table.Header(header =>
            {
              HeaderCell(header.Cell(), "Metric");
              HeaderCell(header.Cell(), "Unit");
              HeaderCell(header.Cell(), "Days");
              HeaderCell(header.Cell(), "Median");
              HeaderCell(header.Cell(), "Range");
            });
            foreach (var row in model.Trends)
            {
              Cell(table.Cell(), row.Label);
              Cell(table.Cell(), row.Unit);
              Cell(table.Cell(), row.Days.ToString(CultureInfo.InvariantCulture));
              Cell(table.Cell(), Num(row.Median));
              Cell(table.Cell(), $"{Num(row.Min)} to {Num(row.Max)}");
            }
          });
        });
      });

      container.Page(page =>
      {
        Frame(page, model, "Lab results");
        page.Content().PaddingVertical(12).Column(column =>
        {
          column.Spacing(8);
          if (model.Labs.Count == 0)
          {
            column.Item().Text("No lab panels stored.").Italic().FontSize(10);
            return;
          }
          column.Item().Text("The latest two panels. H and L are the lab's flags where printed, otherwise the position against the approximate usual range shown; that range is a population range, not the lab's own.").FontSize(10);
          column.Item().Table(table =>
          {
            table.ColumnsDefinition(columns =>
            {
              columns.RelativeColumn(3);
              columns.RelativeColumn(1.6f);
              columns.RelativeColumn(1.8f);
              columns.RelativeColumn(1.8f);
              columns.RelativeColumn(2.2f);
            });
            table.Header(header =>
            {
              HeaderCell(header.Cell(), "Marker");
              HeaderCell(header.Cell(), "Unit");
              HeaderCell(header.Cell(), model.LatestPanel is { } latest ? Iso(latest) : "Latest");
              HeaderCell(header.Cell(), model.PreviousPanel is { } previous ? Iso(previous) : "Previous");
              HeaderCell(header.Cell(), "Usual range");
            });
            foreach (var row in model.Labs)
            {
              Cell(table.Cell(), row.Marker);
              Cell(table.Cell(), row.Unit);
              Cell(table.Cell(), ValueWithFlag(row.Latest, row.LatestFlag), row.LatestFlag.Length > 0);
              Cell(table.Cell(), ValueWithFlag(row.Previous, row.PreviousFlag), row.PreviousFlag.Length > 0);
              Cell(table.Cell(), row.UsualRange);
            }
          });
        });
      });
    });

    return document.GeneratePdf();
  }

  private void Frame(PageDescriptor page, LabSummaryModel model, string title)
  {
    page.Size(PageSizes.A4);
    page.Margin(2, Unit.Centimetre);
    page.DefaultTextStyle(style => style.FontSize(10));
    page.Header().Row(row =>
    {
      row.RelativeItem().Column(column =>
      {
        column.Item().Text($"{_brand.ProductName} summary for {model.PersonName}").FontSize(16).SemiBold();
        column.Item().Text(title).FontSize(12);
      });
      row.ConstantItem(150).AlignRight().Column(column =>
      {
        column.Item().Text($"{Iso(model.From)} to {Iso(model.To)}").FontSize(9);
        column.Item().Text($"Generated {Iso(model.GeneratedOn)}").FontSize(9);
      });
    });
    page.Footer().AlignCenter().Text(text =>
    {
      text.DefaultTextStyle(style => style.FontSize(8));
      text.Span("Self-recorded data from consumer devices and home lab panels, for discussion only. Page ");
      text.CurrentPageNumber();
      text.Span(" of ");
      text.TotalPages();
    });
  }

  private static void HeaderCell(IContainer cell, string text) =>
    cell.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingVertical(4).PaddingHorizontal(2).Text(text).SemiBold().FontSize(9);

  private static void Cell(IContainer cell, string text, bool emphasis = false)
  {
    var span = cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(2).Text(text).FontSize(9);
    if (emphasis) span.SemiBold();
  }

  private static string ValueWithFlag(double? value, string flag) =>
    value == null ? string.Empty : flag.Length > 0 ? $"{Num(value.Value)} {flag}" : Num(value.Value);

  private static string Num(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

  private static string Iso(DateOnly day) => day.ToIso();
}
