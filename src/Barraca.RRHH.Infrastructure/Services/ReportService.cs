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
        // Las filas sin montos (por ejemplo, copiadas del mes anterior y no completadas) no se listan.
        var pagos = await _db.PagosMensuales.Include(x => x.Funcionario)
            .Where(x => x.PeriodoId == periodoId && (x.Adelanto > 0 || x.Liquido > 0 || x.Retencion > 0))
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

    // Un pago negativo (ábaco: el adelanto superó lo que le corresponde en esa empresa) se descuenta del mayor pago
    // positivo del mismo funcionario: se le transfiere un solo neto. Las filas deben venir sin seguimiento (AsNoTracking)
    // porque se modifican solo para el listado.
    private static List<PagoMensual> NetearPagosNegativos(List<PagoMensual> filas)
    {
        foreach (var grupo in filas.GroupBy(x => x.FuncionarioId))
        {
            var deuda = -grupo.Where(x => x.Liquido < 0).Sum(x => x.Liquido);
            foreach (var positivo in grupo.Where(x => x.Liquido > 0).OrderByDescending(x => x.Liquido))
            {
                if (deuda <= 0)
                    break;

                var descuento = Math.Min(deuda, positivo.Liquido);
                positivo.Liquido -= descuento;
                deuda -= descuento;
            }
        }

        return filas.Where(x => x.Liquido > 0).ToList();
    }

    public async Task<string> GenerarRedPagosAsync(string periodo, string carpetaDestino, string usuario)
    {
        var periodoId = await ObtenerPeriodoId(periodo);
        var rows = NetearPagosNegativos(await _db.PagosMensuales.AsNoTracking().Include(x => x.Funcionario).ThenInclude(f => f!.CuentasPago)
            .Where(x => x.PeriodoId == periodoId && x.TipoObra != TipoObra.NA && x.TipoPago.ToUpper().Contains("RED") && x.Liquido != 0)
            .OrderBy(x => x.TipoObra).ThenBy(x => x.Funcionario!.Nombre)
            .ToListAsync());

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

        byte[]? logoBytes = PdfReportStyle.ObtenerLogo();

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
                    "Morales Construcciones - Adelantos",
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
                        var subtotalAdel = grp.Sum(y => y.Adelanto);
                        var subtotalLiquido = grp.Sum(y => y.Liquido);
                        var subtotalRet = grp.Sum(y => y.Retencion);
                        var subtotalTipo = grp.Sum(y => y.TotalGenerado);

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

        byte[]? logoBytes = PdfReportStyle.ObtenerLogo();

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
                        var subtotalAdel = grp.Sum(y => y.Adelanto);
                        var subtotalLiquido = grp.Sum(y => y.Liquido);
                        var subtotalRet = grp.Sum(y => y.Retencion);
                        var subtotalTipo = grp.Sum(y => y.TotalGenerado);

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

                                section.Item().PaddingTop(6).Element(x =>
                                {
                                    x.Table(t =>
                                    {
                                        t.ColumnsDefinition(cd =>
                                        {
                                            cd.RelativeColumn(2.4f);
                                            cd.RelativeColumn(1f);
                                            cd.RelativeColumn(1f);
                                            cd.RelativeColumn(1f);
                                            cd.RelativeColumn(1f);
                                        });

                                        t.Header(h =>
                                        {
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Subtotal (tipo de obra)"));
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Adelantos").AlignRight());
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Líquidos").AlignRight());
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Retenciones").AlignRight());
                                            h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Total").AlignRight());
                                        });

                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text($"Subtotal ({grp.Key})").SemiBold());
                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(subtotalAdel.ToString("N2")).SemiBold());
                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(subtotalLiquido.ToString("N2")).SemiBold());
                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(subtotalRet.ToString("N2")).SemiBold());
                                        t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(subtotalTipo.ToString("N2")).SemiBold());
                                    });
                                });
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

        byte[]? logoBytes = PdfReportStyle.ObtenerLogo();
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
                    "Morales Construcciones - Distribucion por Obra",
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
                                tipoCol.Item().Element(x => PdfReportStyle.ColorBand(x, $"TIPO: {tipo.Key.ToString().ToUpperInvariant()}", tipoColor));

                                var obraIndex = 1;
                                foreach (var obraGroup in groupedObra)
                                {
                                    var obra = obraGroup.First().Obra;

                                    tipoCol.Item().PaddingTop(8).EnsureSpace(180).Element(obraContainer =>
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

                                                    var totalObraJornales = obraGroup.Sum(y => y.Jornales);
                                                    var totalObraHoras = obraGroup.Sum(y => y.HorasLinea);
                                                    var totalObraMonto = obraGroup.Sum(y => y.MontoLinea);
                                                    var etiquetaObra = $"TOTAL OBRA: {obra?.NumeroObra} - {obra?.Nombre}";

                                                    t.Cell().ColumnSpan(3).Element(cell =>
                                                        PdfReportStyle.TableCell(cell)
                                                            .Background(Colors.Green.Lighten4)
                                                            .Text(etiquetaObra)
                                                            .Bold());
                                                    t.Cell().Element(cell =>
                                                        PdfReportStyle.TableCell(cell)
                                                            .Background(Colors.Green.Lighten4)
                                                            .AlignRight()
                                                            .Text(totalObraJornales.ToString("N2"))
                                                            .Bold());
                                                    t.Cell().Element(cell =>
                                                        PdfReportStyle.TableCell(cell)
                                                            .Background(Colors.Green.Lighten4)
                                                            .AlignRight()
                                                            .Text(totalObraHoras.ToString("N2"))
                                                            .Bold());
                                                    t.Cell().Element(cell =>
                                                        PdfReportStyle.TableCell(cell)
                                                            .Background(Colors.Green.Lighten4)
                                                            .AlignRight()
                                                            .Text("-")
                                                            .Bold());
                                                    t.Cell().Element(cell =>
                                                        PdfReportStyle.TableCell(cell)
                                                            .Background(Colors.Green.Lighten4)
                                                            .AlignRight()
                                                            .Text(totalObraMonto.ToString("N2"))
                                                            .Bold());
                                                });
                                            });
                                        }));

                                    obraIndex++;
                                }

                                tipoCol.Item().PaddingTop(10).Element(x => PdfReportStyle.GreenTotalBox(x, "TOTAL TIPO DE OBRA:", tipo.Sum(y => y.MontoLinea).ToString("N2")));
                            }));

                        tipoIndex++;
                    }

                    var totalHorasGeneral = distribuciones.Sum(x => x.HorasLinea);
                    var totalJornalesGeneral = distribuciones.Sum(x => x.Jornales);
                    var valorHoraGeneral = totalHorasGeneral <= 0m
                        ? 0m
                        : Math.Round(totalGeneral / totalHorasGeneral, 2, MidpointRounding.AwayFromZero);

                    col.Item().PaddingTop(10).EnsureSpace(90).Element(c =>
                        c.Border(1).BorderColor(PdfReportStyle.BorderGreen).Padding(8).Column(summary =>
                        {
                            summary.Item().Element(x => PdfReportStyle.SectionTitle(x, "RESUMEN GENERAL DE DISTRIBUCION"));
                            summary.Item().Element(x =>
                            {
                                x.Table(t =>
                                {
                                    t.ColumnsDefinition(cd =>
                                    {
                                        cd.RelativeColumn(1.5f);
                                        cd.RelativeColumn(1f);
                                        cd.RelativeColumn(1f);
                                        cd.RelativeColumn(1f);
                                    });

                                    t.Header(h =>
                                    {
                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Concepto"));
                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Horas eq").AlignRight());
                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("Jornales").AlignRight());
                                        h.Cell().Element(cell => PdfReportStyle.TableHeaderCell(cell).Text("$/hora").AlignRight());
                                    });

                                    t.Cell().Element(cell => PdfReportStyle.TableCell(cell).Text("Totales generales"));
                                    t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(totalHorasGeneral.ToString("N2")));
                                    t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(totalJornalesGeneral.ToString("N2")));
                                    t.Cell().Element(cell => PdfReportStyle.TableCell(cell).AlignRight().Text(valorHoraGeneral.ToString("N2")));
                                });
                            });
                        }));

                    // TOTAL GENERAL final box / CIERRE DEL REPORTE
                    col.Item().PaddingTop(14).EnsureSpace(40).Element(x => PdfReportStyle.GreenTotalBox(x, "CIERRE DEL REPORTE - TOTAL GENERAL:", totalGeneral.ToString("N2")));
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

        byte[]? logoBytes = PdfReportStyle.ObtenerLogo();

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
                    $"Morales Construcciones - {titulo}",
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
                                            var cuentas = r.Funcionario?.CuentasPago ?? [];
                                            var cuenta = CuentaVigente.Seleccionar(cuentas, r.TipoPago);
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

        byte[]? logoBytes = PdfReportStyle.ObtenerLogo();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(25);
                page.Header().Row(r =>
                {
                    if (logoBytes != null)
                        r.ConstantItem(95).PaddingRight(10).Height(62).AlignLeft().Image(logoBytes).FitArea();

                    r.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Morales Construcciones - Retenciones").Bold().FontSize(16);
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
                    col.Item().Text("Morales Construcciones - Gestión RRHH y Costos").Bold().FontSize(18);
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
            TipoObra.Metalurgica => "METAL.",
            _ => "N-A"
        };
    }

    public async Task<string> GenerarPlanillaPagosExcelAsync(string periodo, string carpetaDestino, string usuario)
    {
        var periodoId = await ObtenerPeriodoId(periodo);

        var redPagos = NetearPagosNegativos(await _db.PagosMensuales.AsNoTracking()
            .Include(x => x.Funcionario).ThenInclude(f => f!.CuentasPago)
            .Where(x => x.PeriodoId == periodoId && x.TipoObra != TipoObra.NA
                        && x.TipoPago.ToUpper().Contains("RED") && x.Liquido != 0)
            .OrderBy(x => x.TipoObra).ThenBy(x => x.Funcionario!.Nombre)
            .ToListAsync());

        var naEfectivo = await _db.PagosMensuales
            .Include(x => x.Funcionario).ThenInclude(f => f!.CuentasPago)
            .Where(x => x.PeriodoId == periodoId && x.TipoObra == TipoObra.NA && x.Liquido > 0)
            .OrderBy(x => x.Funcionario!.Nombre)
            .ToListAsync();

        Directory.CreateDirectory(carpetaDestino);
        var path = Path.Combine(carpetaDestino, $"Planilla_Pagos_{periodo}.xlsx");

        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add("Pagos");

        // --- Estilos ---
        var headerFill = ClosedXML.Excel.XLColor.FromHtml("#16324F");
        var sectionFill = ClosedXML.Excel.XLColor.FromHtml("#D9E5F3");
        var altFill = ClosedXML.Excel.XLColor.FromHtml("#F3F4F6");
        var totalFill = ClosedXML.Excel.XLColor.FromHtml("#E5F0E0");

        string[] cols = ["Tipo Obra", "Funcionario", "Tipo Pago", "Banco", "Cuenta Nueva", "Cuenta Vieja", "Monto"];
        int colCount = cols.Length;

        // Título principal
        ws.Cell(1, 1).Value = $"Planilla de Pagos - Período {periodo}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, colCount).Merge();

        ws.Cell(2, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Range(2, 1, 2, colCount).Merge();

        int row = 4;

        void EscribirSeccion(string titulo, List<PagoMensual> filas)
        {
            if (filas.Count == 0) return;

            // Encabezado de sección
            var sectionRange = ws.Range(row, 1, row, colCount);
            sectionRange.Merge();
            sectionRange.Value = titulo;
            sectionRange.Style.Fill.BackgroundColor = sectionFill;
            sectionRange.Style.Font.Bold = true;
            sectionRange.Style.Font.FontSize = 11;
            row++;

            // Cabecera de columnas
            for (int c = 0; c < colCount; c++)
            {
                var cell = ws.Cell(row, c + 1);
                cell.Value = cols[c];
                cell.Style.Fill.BackgroundColor = headerFill;
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            }
            row++;

            var grupos = filas.GroupBy(x => x.TipoObraOriginal).ToList();
            foreach (var grp in grupos)
            {
                bool par = false;
                decimal subtotal = 0;
                foreach (var pago in grp)
                {
                    var cuentas = pago.Funcionario?.CuentasPago ?? [];
                    var cuenta = CuentaVigente.Seleccionar(cuentas, pago.TipoPago);

                    ws.Cell(row, 1).Value = grp.Key;
                    ws.Cell(row, 2).Value = pago.Funcionario?.Nombre ?? "";
                    ws.Cell(row, 3).Value = pago.TipoPago;
                    ws.Cell(row, 4).Value = cuenta?.Banco ?? "";
                    ws.Cell(row, 5).Value = cuenta?.CuentaNueva ?? "";
                    ws.Cell(row, 6).Value = cuenta?.CuentaVieja ?? "";
                    ws.Cell(row, 7).Value = pago.Liquido;
                    ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00";

                    if (par)
                        ws.Range(row, 1, row, colCount).Style.Fill.BackgroundColor = altFill;

                    par = !par;
                    subtotal += pago.Liquido;
                    row++;
                }

                // Subtotal por tipo obra
                var subtotalRange = ws.Range(row, 1, row, colCount - 1);
                subtotalRange.Merge();
                subtotalRange.Value = $"Subtotal {grp.Key}";
                subtotalRange.Style.Fill.BackgroundColor = totalFill;
                subtotalRange.Style.Font.Bold = true;
                subtotalRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Right;
                ws.Cell(row, colCount).Value = subtotal;
                ws.Cell(row, colCount).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, colCount).Style.Fill.BackgroundColor = totalFill;
                ws.Cell(row, colCount).Style.Font.Bold = true;
                row++;
            }

            // Total sección
            var totalRange = ws.Range(row, 1, row, colCount - 1);
            totalRange.Merge();
            totalRange.Value = $"TOTAL {titulo.ToUpper()}";
            totalRange.Style.Fill.BackgroundColor = headerFill;
            totalRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Right;
            ws.Cell(row, colCount).Value = filas.Sum(x => x.Liquido);
            ws.Cell(row, colCount).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, colCount).Style.Fill.BackgroundColor = headerFill;
            ws.Cell(row, colCount).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            ws.Cell(row, colCount).Style.Font.Bold = true;
            row += 2;
        }

        EscribirSeccion("Red Pagos", redPagos);
        EscribirSeccion("N-A Efectivo", naEfectivo);

        // Total general final
        decimal totalGeneral = redPagos.Sum(x => x.Liquido) + naEfectivo.Sum(x => x.Liquido);
        var totalGeneralRange = ws.Range(row, 1, row, colCount - 1);
        totalGeneralRange.Merge();
        totalGeneralRange.Value = "TOTAL GENERAL";
        totalGeneralRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#16324F");
        totalGeneralRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
        totalGeneralRange.Style.Font.Bold = true;
        totalGeneralRange.Style.Font.FontSize = 12;
        totalGeneralRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Right;
        ws.Cell(row, colCount).Value = totalGeneral;
        ws.Cell(row, colCount).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(row, colCount).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#16324F");
        ws.Cell(row, colCount).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
        ws.Cell(row, colCount).Style.Font.Bold = true;
        ws.Cell(row, colCount).Style.Font.FontSize = 12;

        // Ajustar ancho de columnas
        ws.Column(1).Width = 18;
        ws.Column(2).Width = 32;
        ws.Column(3).Width = 20;
        ws.Column(4).Width = 20;
        ws.Column(5).Width = 22;
        ws.Column(6).Width = 22;
        ws.Column(7).Width = 16;

        workbook.SaveAs(path);

        await AuditarAsync(usuario, "Reportes", "Excel Planilla Pagos", periodo, Path.GetFileName(path));
        return path;
    }

    public async Task<string> GenerarPorcentajeHorasExcelAsync(string periodo, string carpetaDestino, string usuario)
    {
        var periodoId = await ObtenerPeriodoId(periodo);

        var horas = await _db.HorasMensuales
            .AsNoTracking()
            .Include(x => x.Funcionario)
            .Include(x => x.Obra)
            .Where(x => x.PeriodoId == periodoId && x.HorasEquivalentes > 0)
            .ToListAsync();

        Directory.CreateDirectory(carpetaDestino);
        var path = Path.Combine(carpetaDestino, $"Porcentaje_Horas_{periodo}.xlsx");

        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add("Porcentaje Horas");

        var azulOscuro  = ClosedXML.Excel.XLColor.FromHtml("#16324F");
        var verdeClaro  = ClosedXML.Excel.XLColor.FromHtml("#E5F0E0");
        var grisClaro   = ClosedXML.Excel.XLColor.FromHtml("#F3F4F6");
        var grisTexto   = ClosedXML.Excel.XLColor.FromHtml("#6B7280");

        string[] cols = ["N° Func.", "Funcionario", "N° Obra", "Obra", "Tipo", "Hs. Comunes", "Hs. Extras", "Hs. Lluvia", "Hs. Equiv.", "% en su tipo"];
        int colCount = cols.Length;
        int colEquiv = colCount - 1;
        int colPct = colCount;

        // Título
        ws.Cell(1, 1).Value = $"Porcentaje de Horas por Funcionario - Período {periodo}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, colCount).Merge();

        ws.Cell(2, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}  |  Funcionarios con horas: {horas.Select(x => x.FuncionarioId).Distinct().Count()}  |  "
            + "El % se calcula por separado dentro de cada tipo (Construcción e Industria y Comercio no se suman entre sí). N-A va en 0 y no se suma.";
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Range(2, 1, 2, colCount).Merge();

        // Cabecera de columnas
        int row = 4;
        for (int c = 0; c < colCount; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = cols[c];
            cell.Style.Fill.BackgroundColor = azulOscuro;
            cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
        }
        row++;

        static TipoObra TipoDeHora(HoraMensual h) => h.Obra?.TipoObra ?? TipoObra.NA;
        static int OrdenTipo(TipoObra t) => t switch
        {
            TipoObra.Construccion => 1,
            TipoObra.IndustriaYComercio => 2,
            TipoObra.Metalurgica => 3,
            TipoObra.Administracion => 4,
            _ => 9
        };

        var porFuncionario = horas
            .GroupBy(x => x.FuncionarioId)
            .OrderBy(g => g.First().Funcionario?.NumeroFuncionario)
            .ToList();

        var totalesPorTipo = new Dictionary<TipoObra, decimal>();

        foreach (var grp in porFuncionario)
        {
            var func = grp.First().Funcionario;

            // Cada tipo se reparte por separado: el % de una obra es sobre las horas de la persona en ese mismo tipo.
            foreach (var tipoGrp in grp.GroupBy(TipoDeHora).OrderBy(g => OrdenTipo(g.Key)))
            {
                var tipo = tipoGrp.Key;
                bool esNa = tipo == TipoObra.NA;
                decimal totalTipo = tipoGrp.Sum(x => x.HorasEquivalentes);
                totalesPorTipo[tipo] = totalesPorTipo.GetValueOrDefault(tipo) + totalTipo;
                bool par = false;

                foreach (var h in tipoGrp.OrderBy(x => x.Obra?.NumeroObra))
                {
                    double pct = !esNa && totalTipo > 0 ? (double)(h.HorasEquivalentes / totalTipo) : 0;

                    ws.Cell(row, 1).Value = func?.NumeroFuncionario ?? "";
                    ws.Cell(row, 2).Value = func?.Nombre ?? "";
                    ws.Cell(row, 3).Value = h.Obra?.NumeroObra ?? "";
                    ws.Cell(row, 4).Value = h.Obra?.Nombre ?? h.NombreObraExcel;
                    ws.Cell(row, 5).Value = TipoObraParser.Texto(tipo);
                    ws.Cell(row, 6).Value = h.HorasComunes;
                    ws.Cell(row, 7).Value = h.HorasExtras;
                    ws.Cell(row, 8).Value = h.HorasLluvia;
                    ws.Cell(row, colEquiv).Value = h.HorasEquivalentes;
                    for (int c = 6; c <= colEquiv; c++)
                        ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, colPct).Value = pct;
                    ws.Cell(row, colPct).Style.NumberFormat.Format = "0.00%";

                    if (par)
                        ws.Range(row, 1, row, colCount).Style.Fill.BackgroundColor = grisClaro;
                    if (esNa)
                    {
                        ws.Range(row, 1, row, colCount).Style.Font.FontColor = grisTexto;
                        ws.Range(row, 1, row, colCount).Style.Font.Italic = true;
                    }

                    par = !par;
                    row++;
                }

                // N-A no lleva subtotal: va en 0 y no se suma.
                if (esNa)
                    continue;

                var subtotalRange = ws.Range(row, 1, row, colEquiv - 1);
                subtotalRange.Merge();
                subtotalRange.Value = $"Total {TipoObraParser.Texto(tipo)} - {func?.Nombre ?? grp.Key.ToString()}";
                subtotalRange.Style.Fill.BackgroundColor = verdeClaro;
                subtotalRange.Style.Font.Bold = true;
                subtotalRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Right;

                ws.Cell(row, colEquiv).Value = totalTipo;
                ws.Cell(row, colEquiv).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, colPct).Value = totalTipo > 0 ? 1.0 : 0.0;
                ws.Cell(row, colPct).Style.NumberFormat.Format = "0.00%";
                ws.Cell(row, colPct).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                foreach (var c in new[] { colEquiv, colPct })
                {
                    ws.Cell(row, c).Style.Fill.BackgroundColor = verdeClaro;
                    ws.Cell(row, c).Style.Font.Bold = true;
                }
                row++;
            }
        }

        // Totales por tipo (cada tipo por separado; N-A se informa pero no se suma)
        row++;
        foreach (var (tipo, total) in totalesPorTipo.OrderBy(x => OrdenTipo(x.Key)))
        {
            bool esNa = tipo == TipoObra.NA;
            var rango = ws.Range(row, 1, row, colEquiv - 1);
            rango.Merge();
            rango.Value = esNa ? "HORAS N-A (no se suman, van en 0)" : $"TOTAL HORAS {TipoObraParser.Texto(tipo)}";
            rango.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Right;

            ws.Cell(row, colEquiv).Value = total;
            ws.Cell(row, colEquiv).Style.NumberFormat.Format = "#,##0.00";

            var fila = ws.Range(row, 1, row, colCount);
            fila.Style.Fill.BackgroundColor = esNa ? grisClaro : azulOscuro;
            fila.Style.Font.FontColor = esNa ? grisTexto : ClosedXML.Excel.XLColor.White;
            fila.Style.Font.Bold = true;
            row++;
        }

        // Anchos de columna
        ws.Column(1).Width = 12;
        ws.Column(2).Width = 30;
        ws.Column(3).Width = 10;
        ws.Column(4).Width = 30;
        ws.Column(5).Width = 22;
        for (int c = 6; c <= colEquiv; c++)
            ws.Column(c).Width = 12;
        ws.Column(colPct).Width = 13;

        // Congelar fila de cabecera
        ws.SheetView.FreezeRows(4);

        workbook.SaveAs(path);

        await AuditarAsync(usuario, "Reportes", "Excel Porcentaje Horas", periodo, Path.GetFileName(path));
        return path;
    }
}
