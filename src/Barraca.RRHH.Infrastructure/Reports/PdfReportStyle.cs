using System;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Infrastructure.Reports;

public static class PdfReportStyle
{
    // Palette
    public static Color Primary = Colors.Blue.Darken1; // main brand-ish
    public static Color Accent = Colors.Green.Darken1; // totals / bands
    public static Color LightGrey = Colors.Grey.Lighten4;
    public static Color MediumGrey = Colors.Grey.Lighten2;

    // Typography sizes (improved hierarchy)
    public const float TitleSize = 22f;
    public const float SectionSize = 14f;
    public const float SmallSize = 10f;
    public const string ReportVersion = "v3";

    public static void HeaderWithLogo(IContainer container, byte[]? logoBytes, string title, string periodo, string corrida, DateTime generated)
    {
        container.PaddingBottom(16).Row(row =>
        {
            if (logoBytes != null)
            {
                row.ConstantItem(80).Element(e => e.PaddingRight(8).Image(logoBytes, ImageScaling.FitHeight));
            }

            row.RelativeItem().Column(col =>
            {
                col.Item().Text(title).Bold().FontSize(TitleSize);
                col.Item().Text($"Período: {periodo}    Corrida: {corrida}    Generado: {generated:yyyy-MM-dd HH:mm}").FontSize(SmallSize).FontColor(MediumGrey);
            });
        });
    }

    public static void HeaderWithQuickSummary(IContainer container, byte[]? logoBytes, string title, string periodo, DateTime generated, params (string Label, string Value)[] summary)
    {
        container.PaddingBottom(12).Column(col =>
        {
            col.Item().Row(row =>
            {
                if (logoBytes != null)
                    row.ConstantItem(70).Element(e => e.PaddingRight(8).Image(logoBytes, ImageScaling.FitHeight));

                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(title).Bold().FontSize(18);
                    c.Item().Text($"Periodo: {periodo}    Generado: {generated:yyyy-MM-dd HH:mm}").FontSize(9).FontColor(MediumGrey);
                });
            });

            if (summary is { Length: > 0 })
            {
                col.Item().PaddingTop(6).Row(r =>
                {
                    foreach (var item in summary)
                    {
                        r.RelativeItem().Element(x =>
                            x.Background(Colors.Grey.Lighten4).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(s =>
                            {
                                s.Item().Text(item.Label).FontSize(8).FontColor(Colors.Grey.Darken1);
                                s.Item().Text(item.Value).Bold().FontSize(10);
                            }));
                    }
                });
            }
        });
    }

    public static void SectionTitle(IContainer container, string text)
    {
        container.PaddingTop(8).PaddingBottom(6).Text(text).SemiBold().FontSize(SectionSize);
    }

    public static void Subtitle(IContainer container, string text)
    {
        container.PaddingBottom(6).Text(text).FontSize(SmallSize).FontColor(MediumGrey);
    }

    public static void CorporateCover(IContainer container, string title, string subtitle, string periodo, DateTime generated)
    {
        container
            .Background(Colors.Blue.Lighten5)
            .Border(1)
            .BorderColor(Colors.Blue.Lighten3)
            .Padding(14)
            .Column(col =>
            {
                col.Item().Text(title).Bold().FontSize(18).FontColor(Primary);
                col.Item().PaddingTop(4).Text(subtitle).FontSize(11).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(8).Text($"Periodo: {periodo}    Fecha: {generated:yyyy-MM-dd HH:mm}").FontSize(10).FontColor(Colors.Grey.Darken1);
            });
    }

    public static void GreenBand(IContainer container, string text)
    {
        container.Element(c => c.Background(Colors.Green.Lighten3).Padding(8).Text(text).SemiBold().FontSize(SectionSize));
    }

    public static void ColorBand(IContainer container, string text, Color color)
    {
        container.Element(c => c.Background(color).Padding(8).Text(text).SemiBold().FontSize(SectionSize));
    }

    public static Color TipoObraColor(TipoObra tipo)
    {
        return tipo switch
        {
            TipoObra.Construccion => Colors.Blue.Lighten3,
            TipoObra.IndustriaYComercio => Colors.Orange.Lighten3,
            TipoObra.Administracion => Colors.Purple.Lighten3,
            _ => Colors.Grey.Lighten3
        };
    }

    public static void GreenTotalBox(IContainer container, string label, string value)
    {
        container.Element(c => c.Background(Colors.Green.Lighten4).Padding(10).Row(r =>
        {
            r.RelativeItem().Text(label).SemiBold().FontSize(SmallSize);
            r.ConstantItem(160).AlignRight().Text(value).Bold().FontSize(SmallSize);
        }));
    }

    public static IContainer TableHeaderCell(IContainer container)
    {
        return container.Background(LightGrey).Padding(8).BorderBottom(1).BorderColor(MediumGrey);
    }

    public static IContainer TableCell(IContainer container)
    {
        return container.PaddingVertical(6).BorderBottom(1).BorderColor(Colors.Grey.Lighten4).PaddingHorizontal(4);
    }

    public static void TotalBox(IContainer container, string label, string value)
    {
        container.Element(c => c.Background(Colors.Green.Lighten3).Padding(8).Row(r =>
        {
            r.RelativeItem().Text(label).Bold();
            r.ConstantItem(140).AlignRight().Text(value).Bold();
        }));
    }

    public static void HighlightedTotalRow(IContainer container, string label, string value)
    {
        container.PaddingTop(6).Element(c => c.Background(LightGrey).Padding(8).Row(r =>
        {
            r.RelativeItem().Text(label).Bold();
            r.ConstantItem(140).AlignRight().Text(value).Bold();
        }));
    }

    public static void FooterPageNumber(IContainer container)
    {
        container.AlignCenter().Text(x => { x.Span("Página "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
    }

    public static void FooterWithMeta(IContainer container, string periodo)
    {
        container.Row(row =>
        {
            row.RelativeItem().AlignLeft().Text($"Periodo: {periodo} | Reporte {ReportVersion}").FontSize(8).FontColor(Colors.Grey.Darken1);
            row.RelativeItem().AlignCenter().DefaultTextStyle(x => x.FontSize(8)).Text(x =>
            {
                x.Span("Pagina ");
                x.CurrentPageNumber();
                x.Span(" / ");
                x.TotalPages();
            });
            row.RelativeItem().AlignRight().Text("Barraca RRHH").FontSize(8).FontColor(Colors.Grey.Darken1);
        });
    }
}
