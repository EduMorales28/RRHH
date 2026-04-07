using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Barraca.RRHH.Infrastructure.Reports;
using Barraca.RRHH.Infrastructure.Helpers;

namespace Barraca.RRHH.Infrastructure.Services;

public class ReportService : IReportService
{
    private readonly BarracaDbContext _db;

    public ReportService(BarracaDbContext db)
    {
        _db = db;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<string> GenerarAdelantosAsync(string periodo, string carpetaDestino, string usuario)
    {
        var periodoId = await ObtenerPeriodoId(periodo);
        var pagos = await _db.PagosMensuales.Include(x => x.Funcionario)
            .Where(x => x.PeriodoId == periodoId && x.Adelanto > 0)
            .OrderBy(x => x.TipoObra).ThenBy(x => x.Funcionario!.Nombre)
            .ToListAsync();

        var path = Path.Combine(carpetaDestino, $"Adelantos_{periodo}.pdf");
        CreateAdelantosPdf(path, periodo, pagos);
        await AuditarAsync(usuario, "Reportes", "PDF Adelantos", periodo, Path.GetFileName(path));
        return path;
    }

    public async Task<string> GenerarConsolidadoGeneralAsync(string periodo, string carpetaDestino, string usuario)
    {
        var periodoId = await ObtenerPeriodoId(periodo);
        var pagos = await _db.PagosMensuales.Include(x => x.Funcionario)
            .Where(x => x.PeriodoId == periodoId)
            .OrderBy(x => x.TipoObra).ThenBy(x => x.Funcionario!.Nombre)
            .ToListAsync();

        var path = Path.Combine(carpetaDestino, $"Consolidado_General_{periodo}.pdf");
        CreateConsolidadoPdf(path, periodo, pagos);
        await AuditarAsync(usuario, "Reportes", "PDF Consolidado", periodo, Path.GetFileName(path));
        return path;
    }

    public async Task<string> GenerarDistribucionObrasAsync(string periodo, string carpetaDestino, string usuario)
    {
        var periodoId = await ObtenerPeriodoId(periodo);
        var distribuciones = await _db.DistribucionesCosto.Include(x => x.Obra)
            .Where(x => x.PeriodoId == periodoId)
            .OrderBy(x => x.TipoObra).ThenBy(x => x.Obra!.NumeroObra).ThenBy(x => x.Categoria)
            .ToListAsync();

        var path = Path.Combine(carpetaDestino, $"Distribucion_Obras_{periodo}.pdf");
        CreateDistribucionPdf(path, periodo, distribuciones);
        await AuditarAsync(usuario, "Reportes", "PDF Distribucion", periodo, Path.GetFileName(path));
        return path;
    }

    public async Task<string> GenerarNAAsync(string periodo, string carpetaDestino, string usuario)
    {
        var periodoId = await ObtenerPeriodoId(periodo);
        var rows = await _db.PagosMensuales.Include(x => x.Funcionario).ThenInclude(f => f!.CuentasPago)
            .Where(x => x.PeriodoId == periodoId && x.TipoObra == TipoObra.NA && x.Liquido > 0)
            .OrderBy(x => x.Funcionario!.Nombre)
            .ToListAsync();

        var path = Path.Combine(carpetaDestino, $"NA_Efectivo_Funcionarios_{periodo}.pdf");
        CreatePagosPdf(path, periodo, "Pagos N-A", rows);
        await AuditarAsync(usuario, "Reportes", "PDF N-A", periodo, Path.GetFileName(path));
        return path;
    }

    public async Task<string> GenerarRedPagosAsync(string periodo, string carpetaDestino, string usuario)
    {
        var periodoId = await ObtenerPeriodoId(periodo);
        var rows = await _db.PagosMensuales.Include(x => x.Funcionario).ThenInclude(f => f!.CuentasPago)
            .Where(x => x.PeriodoId == periodoId && x.TipoObra != TipoObra.NA && x.TipoPago.ToUpper().Contains("RED") && x.Liquido > 0)
            .OrderBy(x => x.TipoObra).ThenBy(x => x.Funcionario!.Nombre)
            .ToListAsync();

        var path = Path.Combine(carpetaDestino, $"RedPagos_Funcionarios_{periodo}.pdf");
        CreatePagosPdf(path, periodo, "Pagos Red Pagos", rows);
        await AuditarAsync(usuario, "Reportes", "PDF RedPagos", periodo, Path.GetFileName(path));
        return path;
    }

    public async Task<string> GenerarRetencionesAsync(string periodo, string carpetaDestino, string usuario)
    {
        var periodoId = await ObtenerPeriodoId(periodo);
        var rows = await _db.PagosMensuales.Include(x => x.Funcionario)
            .Where(x => x.PeriodoId == periodoId && x.Retencion > 0)
            .OrderBy(x => x.Funcionario!.Nombre)
            .ToListAsync();

        var path = Path.Combine(carpetaDestino, $"Retenciones_{periodo}.pdf");
        CreateRetencionesPdf(path, periodo, rows);
        await AuditarAsync(usuario, "Reportes", "PDF Retenciones", periodo, Path.GetFileName(path));
        return path;
    }

    // --- PDF creation helpers -------------------------------------------------
    private void CreateAdelantosPdf(string path, string periodo, List<Domain.Entities.PagoMensual> pagos)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var grouped = pagos.GroupBy(x => x.TipoObraOriginal).ToList();

        decimal totalAdel = 0;

        var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
        byte[]? logoBytes = File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);

                // Header
                PdfReportStyle.HeaderWithQuickSummary(
                    page.Header(),
                    logoBytes,
                    "Barraca Morales - Adelantos",
                    periodo,
                    DateTime.Now,
                    ("Tipos", grouped.Count.ToString()),
                    ("Funcionarios", pagos.Count.ToString()),
                    ("Total", pagos.Sum(x => x.Adelanto).ToString("N2")));

                // Content - single column
                page.Content().Column(col =>
                {
                    col.Item().Element(c => PdfReportStyle.CorporateCover(
                        c,
                        "Reporte de Adelantos",
                        "Detalle de adelantos por tipo de obra y total general",
                        periodo,
                        DateTime.Now));

                    col.Item().Element(c => PdfReportStyle.Subtitle(c, "Listado de adelantos por tipo de obra. Totales separados para legibilidad."));

                    var sectionIndex = 1;
                    foreach (var grp in grouped)
                    {
                        var tipoColor = PdfReportStyle.TipoObraColor(TipoObraParser.Parse(grp.Key));
                        col.Item().PaddingTop(10).Element(c =>
                            c.Border(1).BorderColor(tipoColor).Padding(8).Column(section =>
                            {
                                section.Item().Element(x => PdfReportStyle.SectionTitle(x, $"{sectionIndex}. {grp.Key}"));

                                section.Item().Element(x =>
                                {
                                    x.Table(t =>
                                    {
                                        t.ColumnsDefinition(cd => { cd.RelativeColumn(1.2f); cd.RelativeColumn(3.2f); cd.RelativeColumn(1f); });

                                        t.Header(h =>
                                        {
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Tipo"));
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Funcionario"));
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Adelanto").AlignRight());
                                        });

                                        foreach (var p in grp)
                                        {
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(AbreviarTipoObra(p.TipoObra)));
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(p.Funcionario?.Nombre ?? ""));
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(p.Adelanto.ToString("N2")));
                                            totalAdel += p.Adelanto;
                                        }
                                    });
                                });

                                section.Item().PaddingTop(6).Element(x => PdfReportStyle.HighlightedTotalRow(x, "TOTAL TIPO:", grp.Sum(y => y.Adelanto).ToString("N2")));
                            }));

            sectionIndex++;
                    }

                    // Total general
                    col.Item().PaddingTop(14).Element(x => PdfReportStyle.HighlightedTotalRow(x, "TOTAL GENERAL:", totalAdel.ToString("N2")));
                });

                PdfReportStyle.FooterWithMeta(page.Footer(), periodo);
            });
        }).GeneratePdf(path);
    }

    private void CreateConsolidadoPdf(string path, string periodo, List<Domain.Entities.PagoMensual> pagos)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var grouped = pagos.GroupBy(x => x.TipoObraOriginal).ToList();

        decimal totalAdel = 0, totalLiquido = 0, totalRet = 0, totalGeneral = 0;

        var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
        byte[]? logoBytes = File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(25);

                // Header
                PdfReportStyle.HeaderWithQuickSummary(
                    page.Header(),
                    logoBytes,
                    "CONSOLIDADO GENERAL (FUNCIONARIOS)",
                    periodo,
                    DateTime.Now,
                    ("Tipos", grouped.Count.ToString()),
                    ("Filas", pagos.Count.ToString()),
                    ("Total", pagos.Sum(x => x.TotalGenerado).ToString("N2")));

                // Content
                page.Content().Column(col =>
                {
                    col.Item().Element(c => PdfReportStyle.CorporateCover(
                        c,
                        "Consolidado General de Funcionarios",
                        "Resumen económico por tipo de obra con totales comparables",
                        periodo,
                        DateTime.Now));

                    // descriptive line
                    col.Item().Element(c => PdfReportStyle.Subtitle(c, "Adelanto + Líquido + Retención = Total generado (gasto real)"));

                    var sectionIndex = 1;
                    foreach (var grp in grouped)
                    {
                        var tipoColor = PdfReportStyle.TipoObraColor(TipoObraParser.Parse(grp.Key));
                        col.Item().PaddingTop(10).Element(c =>
                            c.Border(1).BorderColor(tipoColor).Padding(8).Column(section =>
                            {
                                section.Item().Element(x => PdfReportStyle.SectionTitle(x, $"{sectionIndex}. Tipo de obra: {grp.Key}"));

                                section.Item().Element(x =>
                                {
                                    x.Table(t =>
                                    {
                                        t.ColumnsDefinition(cd =>
                                        {
                                            cd.RelativeColumn(1.5f);
                                            cd.RelativeColumn(4f);
                                            cd.RelativeColumn(1.2f);
                                            cd.RelativeColumn(1.2f);
                                            cd.RelativeColumn(1.2f);
                                            cd.RelativeColumn(1.2f);
                                        });

                                        t.Header(h =>
                                        {
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Tipo"));
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Funcionario"));
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Adelanto").AlignRight());
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Líquido").AlignRight());
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Retención").AlignRight());
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Total").AlignRight());
                                        });

                                        foreach (var p in grp)
                                        {
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(p.TipoObraOriginal ?? grp.Key));
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(p.Funcionario?.Nombre ?? ""));
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(p.Adelanto.ToString("N2")));
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(p.Liquido.ToString("N2")));
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(p.Retencion.ToString("N2")));
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(p.TotalGenerado.ToString("N2")));

                                            totalAdel += p.Adelanto;
                                            totalLiquido += p.Liquido;
                                            totalRet += p.Retencion;
                                            totalGeneral += p.TotalGenerado;
                                        }
                                    });
                                });

                                section.Item().PaddingTop(6).Element(x => PdfReportStyle.HighlightedTotalRow(x, "TOTAL TIPO:", grp.Sum(y => y.TotalGenerado).ToString("N2")));
                            }));

            sectionIndex++;
                    }

                    // final summary
                    col.Item().PaddingTop(14).Element(c =>
                        c.BorderTop(1).BorderColor(PdfReportStyle.MediumGrey).PaddingTop(10).Column(summary =>
                        {
                            summary.Item().Element(x => PdfReportStyle.HighlightedTotalRow(x, "Total adelantos:", totalAdel.ToString("N2")));
                            summary.Item().Element(x => PdfReportStyle.HighlightedTotalRow(x, "Total líquidos:", totalLiquido.ToString("N2")));
                            summary.Item().Element(x => PdfReportStyle.HighlightedTotalRow(x, "Total retenciones:", totalRet.ToString("N2")));
                            summary.Item().Element(x => PdfReportStyle.HighlightedTotalRow(x, "Total general:", totalGeneral.ToString("N2")));
                        }));
                });

                PdfReportStyle.FooterWithMeta(page.Footer(), periodo);
            });
        }).GeneratePdf(path);
    }

    private void CreateDistribucionPdf(string path, string periodo, List<Domain.Entities.DistribucionCosto> distribuciones)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var groupedTipo = distribuciones.GroupBy(x => x.TipoObra).ToList();

        decimal totalGeneral = 0;

        var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
        byte[]? logoBytes = File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);

                // Header
                PdfReportStyle.HeaderWithQuickSummary(
                    page.Header(),
                    logoBytes,
                    "Barraca Morales - Distribucion por Obra",
                    periodo,
                    DateTime.Now,
                    ("Tipos", groupedTipo.Count.ToString()),
                    ("Lineas", distribuciones.Count.ToString()),
                    ("Total", distribuciones.Sum(x => x.MontoLinea).ToString("N2")));

                // Content
                page.Content().Column(col =>
                {
                    col.Item().Element(c => PdfReportStyle.CorporateCover(
                        c,
                        "Distribucion de Costos por Obra",
                        "Vista jerárquica por tipo de obra y detalle por obra/categoría",
                        periodo,
                        DateTime.Now));

                    col.Item().Element(c => PdfReportStyle.Subtitle(c, "Distribución de costos por obra: desglose por categoría, mostrando jornales, horas equivalentes, valor hora y monto por línea."));

                    var tipoIndex = 1;
                    foreach (var tipo in groupedTipo)
                    {
                        var groupedObra = tipo.GroupBy(x => x.ObraId).ToList();

                        var tipoColor = PdfReportStyle.TipoObraColor(tipo.Key);
                        col.Item().PaddingTop(10).Element(c =>
                            c.Border(1).BorderColor(tipoColor).Padding(10).Column(tipoCol =>
                            {
                                tipoCol.Item().Element(x => PdfReportStyle.ColorBand(x, $"{tipoIndex}. TIPO DE OBRA: {tipo.Key}", tipoColor));

                                var obraIndex = 1;
                                foreach (var obraGroup in groupedObra)
                                {
                                    var obra = obraGroup.First().Obra;

                                    tipoCol.Item().PaddingTop(8).Element(obraContainer =>
                                        obraContainer.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(obraCol =>
                                        {
                                            obraCol.Item().Element(x =>
                                                x.Background(Colors.Grey.Lighten4).Padding(6).Text($"{tipoIndex}.{obraIndex} Obra: {obra?.NumeroObra} - {obra?.Nombre}").SemiBold().FontSize(11));

                                            obraCol.Item().PaddingTop(6).Element(x =>
                                            {
                                                x.Table(t =>
                                                {
                                                    t.ColumnsDefinition(cd =>
                                                    {
                                                        cd.RelativeColumn(0.6f);
                                                        cd.RelativeColumn(2.2f);
                                                        cd.RelativeColumn(1.6f);
                                                        cd.RelativeColumn(0.9f);
                                                        cd.RelativeColumn(1f);
                                                        cd.RelativeColumn(1f);
                                                        cd.RelativeColumn(1.1f);
                                                    });

                                                    t.Header(h =>
                                                    {
                                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("N°"));
                                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Obra"));
                                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Categoría"));
                                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Jornales").AlignRight());
                                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Horas eq").AlignRight());
                                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("$/hora").AlignRight());
                                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Monto").AlignRight());
                                                    });

                                                    var index = 1;
                                                    foreach (var line in obraGroup)
                                                    {
                                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(index.ToString()));
                                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(obra?.NumeroObra ?? ""));
                                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(line.Categoria));
                                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(line.Jornales.ToString("N2")));
                                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(line.HorasLinea.ToString("N2")));
                                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(line.ValorHora.ToString("N2")));
                                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(line.MontoLinea.ToString("N2")));

                                                        totalGeneral += line.MontoLinea;
                                                        index++;
                                                    }
                                                });
                                            });

                                            obraCol.Item().PaddingTop(6).Element(x => PdfReportStyle.GreenTotalBox(x, "TOTAL OBRA:", obraGroup.Sum(y => y.MontoLinea).ToString("N2")));
                                        }));

                                    obraIndex++;
                                }

                                tipoCol.Item().PaddingTop(10).Element(x => PdfReportStyle.GreenTotalBox(x, "TOTAL TIPO DE OBRA:", tipo.Sum(y => y.MontoLinea).ToString("N2")));
                            }));

                        tipoIndex++;
                    }

                    // TOTAL GENERAL final box / CIERRE DEL REPORTE
                    col.Item().PaddingTop(14).Element(x => PdfReportStyle.GreenTotalBox(x, "CIERRE DEL REPORTE - TOTAL GENERAL:", totalGeneral.ToString("N2")));
                });

                PdfReportStyle.FooterWithMeta(page.Footer(), periodo);
            });
        }).GeneratePdf(path);
    }

    private void CreatePagosPdf(string path, string periodo, string titulo, List<Domain.Entities.PagoMensual> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var grouped = rows.GroupBy(x => x.TipoObraOriginal).ToList();
        decimal totalGeneral = 0;

        var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
        byte[]? logoBytes = File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(25);
                // Header
                PdfReportStyle.HeaderWithQuickSummary(
                    page.Header(),
                    logoBytes,
                    $"Barraca Morales - {titulo}",
                    periodo,
                    DateTime.Now,
                    ("Tipos", grouped.Count.ToString()),
                    ("Filas", rows.Count.ToString()),
                    ("Total", rows.Sum(x => x.Liquido).ToString("N2")));

                page.Content().Column(col =>
                {
                    col.Item().Element(c => PdfReportStyle.CorporateCover(
                        c,
                        titulo,
                        "Detalle de pagos por tipo de obra y medio de pago",
                        periodo,
                        DateTime.Now));

                    var sectionIndex = 1;
                    foreach (var grp in grouped)
                    {
                        var tipoColor = PdfReportStyle.TipoObraColor(TipoObraParser.Parse(grp.Key));
                        col.Item().PaddingTop(10).Element(c =>
                            c.Border(1).BorderColor(tipoColor).Padding(8).Column(section =>
                            {
                                section.Item().Element(x => PdfReportStyle.SectionTitle(x, $"{sectionIndex}. {grp.Key}"));

                                section.Item().Element(x =>
                                {
                                    x.Table(t =>
                                    {
                                        t.ColumnsDefinition(cd =>
                                        {
                                            cd.RelativeColumn(1.2f);
                                            cd.RelativeColumn(2.5f);
                                            cd.RelativeColumn(1.4f);
                                            cd.RelativeColumn(1.6f);
                                            cd.RelativeColumn(1.6f);
                                            cd.RelativeColumn(1f);
                                        });

                                        t.Header(h =>
                                        {
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Tipo de obra"));
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Funcionario"));
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Banco"));
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Cuenta nueva"));
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Cuenta vieja"));
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Monto a pagar").AlignRight());
                                        });

                                        foreach (var r in grp)
                                        {
                                            var cuenta = r.Funcionario?.CuentasPago.FirstOrDefault();
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(AbreviarTipoObra(r.TipoObra)));
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(r.Funcionario?.Nombre ?? ""));
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(cuenta?.Banco ?? r.TipoPago));
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(cuenta?.CuentaNueva ?? ""));
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(cuenta?.CuentaVieja ?? ""));
                                            t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(r.Liquido.ToString("N2")));
                                            totalGeneral += r.Liquido;
                                        }
                                    });
                                });

                                section.Item().PaddingTop(6).Element(x => PdfReportStyle.HighlightedTotalRow(x, "TOTAL TIPO:", grp.Sum(y => y.Liquido).ToString("N2")));
                            }));

            sectionIndex++;
                    }

                    col.Item().PaddingTop(10).AlignRight().Text($"TOTAL GENERAL: {totalGeneral.ToString("N2")} ").Bold();
                });

                PdfReportStyle.FooterWithMeta(page.Footer(), periodo);
            });
        }).GeneratePdf(path);
    }

    private void CreateRetencionesPdf(string path, string periodo, List<Domain.Entities.PagoMensual> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        decimal totalRet = 0;

        var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
        byte[]? logoBytes = File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(25);
                page.Header().Row(r =>
                {
                    if (logoBytes != null)
                        r.ConstantItem(80).Image(logoBytes, ImageScaling.FitHeight);

                    r.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Barraca Morales - Retenciones").Bold().FontSize(16);
                        col.Item().Text($"Periodo: {periodo}").FontSize(10);
                    });
                });

                page.Content().Column(col =>
                {
                    col.Item().Element(c => PdfReportStyle.CorporateCover(
                        c,
                        "Reporte de Retenciones",
                        "Detalle de importes retenidos por funcionario",
                        periodo,
                        DateTime.Now));

                    col.Item().Element(c =>
                        c.Border(1).BorderColor(Colors.Green.Lighten2).Padding(8).Column(section =>
                        {
                            section.Item().Element(x => PdfReportStyle.SectionTitle(x, "Detalle de retenciones"));

                            section.Item().Element(x =>
                            {
                                x.Table(t =>
                                {
                                    t.ColumnsDefinition(cd =>
                                    {
                                        cd.RelativeColumn(2.6f);
                                        cd.RelativeColumn(1f);
                                        cd.RelativeColumn(2f);
                                        cd.RelativeColumn(1.4f);
                                        cd.RelativeColumn(1.8f);
                                        cd.RelativeColumn(2f);
                                    });

                                    t.Header(h =>
                                    {
                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Funcionario"));
                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Importe").AlignRight());
                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Responsable"));
                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Banco"));
                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Cuenta"));
                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Observaciones"));
                                    });

                                    foreach (var r in rows)
                                    {
                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text($"{r.Funcionario?.NumeroFuncionario} - {r.Funcionario?.Nombre}"));
                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(r.Retencion.ToString("N2")));
                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(r.Funcionario?.ResponsableRetencion ?? ""));
                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(r.Funcionario?.BancoRetencion ?? ""));
                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(r.Funcionario?.CuentaRetencion ?? ""));
                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text(r.Observacion ?? ""));
                                        totalRet += r.Retencion;
                                    }
                                });
                            });

                            section.Item().PaddingTop(8).Element(x => PdfReportStyle.GreenTotalBox(x, "TOTAL RETENIDO:", totalRet.ToString("N2")));
                        }));
                });

                PdfReportStyle.FooterWithMeta(page.Footer(), periodo);
            });
        }).GeneratePdf(path);
    }

    private void CrearPdf(string path, string titulo, string[] headers, List<string[]> rows, bool landscape = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(25);
                page.Header().Column(col =>
                {
                    col.Item().Text("Barraca Morales - Gestión RRHH y Costos").Bold().FontSize(18);
                    col.Item().Text(titulo).FontSize(12);
                    col.Item().Text($"Generado: {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c => { for (int i = 0; i < headers.Length; i++) c.RelativeColumn(); });
                    table.Header(h =>
                    {
                        foreach (var header in headers)
                            h.Cell().Element(CellStyle).Text(header).Bold();
                    });
                    foreach (var row in rows)
                        foreach (var value in row)
                            table.Cell().Element(CellStyle).Text(value ?? string.Empty).FontSize(9);
                });
                page.Footer().AlignCenter().Text(x => { x.Span("Página "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        }).GeneratePdf(path);
    }

    private static IContainer CellStyle(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(2);

    private async Task<int> ObtenerPeriodoId(string codigo)
    {
        var periodo = await _db.Periodos.FirstAsync(x => x.Codigo == codigo);
        return periodo.Id;
    }

    private async Task AuditarAsync(string usuario, string modulo, string accion, string clave, string detalle)
    {
        _db.AuditoriaEventos.Add(new AuditoriaEvento { Usuario = usuario, Modulo = modulo, Accion = accion, Entidad = "PDF", EntidadClave = clave, Detalle = detalle });
        await _db.SaveChangesAsync();
    }

    public async Task<int> EliminarReporteAsync(string ruta, string usuario)
    {
        // No persistent report table exists; just audit the deletion attempt
        _db.AuditoriaEventos.Add(new AuditoriaEvento { Usuario = usuario, Modulo = "Reportes", Accion = "Eliminar", Entidad = "Archivo", EntidadClave = ruta, Detalle = "Eliminado desde UI" });
        await _db.SaveChangesAsync();
        return 1;
    }

    private static string AbreviarTipoObra(TipoObra tipo)
    {
        return tipo switch
        {
            TipoObra.Construccion => "CONST.",
            TipoObra.IndustriaYComercio => "IND. Y COM.",
            TipoObra.Administracion => "ADM.",
            _ => "N-A"
        };
    }
}
