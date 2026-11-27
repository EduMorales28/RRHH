using System.Globalization;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Data;
using Barraca.RRHH.Infrastructure.Helpers;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Services;

// Pestaña "BPS por obra": guarda las facturas BPS del mes y la tasa de la Sede, arma el reparto
// (RepartoBps) con las horas y los pagos del mes y genera la planilla para cargar en SP.
// Decisiones del usuario (26/11/2026):
// - Se reparte el total de cada factura, tal cual se paga.
// - Horas equivalentes (comunes + 2 × extras + lluvia), las mismas de la distribución de costos.
// - Sede = nominal de ábaco de cada administrativo × tasa BPS de la factura de IyC.
// - Metalúrgicos = nominal × la misma tasa (tienen 1 hora de referencia, no horas reales), en la factura de la Sede.
// - El programa arma la planilla y no escribe en SP.
public class DistribucionBpsService : IDistribucionBpsService
{
    private static readonly CultureInfo Uy = CultureInfo.GetCultureInfo("es-UY");
    private readonly BarracaDbContext _db;

    public DistribucionBpsService(BarracaDbContext db)
    {
        _db = db;
    }

    public async Task<BpsMesDto> ObtenerAsync(string periodo)
    {
        var codigo = (periodo ?? string.Empty).Trim();
        var p = await _db.Periodos.AsNoTracking().FirstOrDefaultAsync(x => x.Codigo == codigo);
        var dto = new BpsMesDto { Periodo = codigo, Cerrado = p is null || p.Estado == EstadoPeriodo.Cerrado };
        if (p is null)
            return dto;

        dto.Facturas = await _db.FacturasBps.AsNoTracking()
            .Where(x => x.PeriodoId == p.Id)
            .OrderBy(x => x.TipoObra).ThenBy(x => x.Id)
            .Select(x => new FacturaBpsDto { Id = x.Id, TipoObra = x.TipoObra, Descripcion = x.Descripcion, Importe = x.Importe })
            .ToListAsync();

        var (tasa, de) = await TasaSedeAsync(p);
        dto.TasaSede = tasa;
        dto.TasaSedeDe = de;
        return dto;
    }

    public async Task<string> GuardarAsync(string periodo, decimal? tasaSede, IReadOnlyList<FacturaBpsDto> facturas, string usuario)
    {
        var codigo = (periodo ?? string.Empty).Trim();
        await PeriodoGuard.VerificarAbiertoAsync(_db, codigo);
        var p = await _db.Periodos.FirstOrDefaultAsync(x => x.Codigo == codigo)
            ?? throw new InvalidOperationException($"No existe el período {codigo}.");

        var errores = new List<string>();
        for (var i = 0; i < facturas.Count; i++)
        {
            var f = facturas[i];
            if (f.TipoObra is not (TipoObra.Construccion or TipoObra.IndustriaYComercio))
                errores.Add($"Fila {i + 1}: elija la empresa (Construcción o Industria y Comercio).");
            if (f.Importe < 0)
                errores.Add($"Fila {i + 1}: el importe no puede ser negativo.");
        }
        if (tasaSede is not null && (tasaSede < 0 || tasaSede >= 1))
            errores.Add("La tasa de la Sede tiene que estar entre 0 % y 100 %.");
        if (errores.Count > 0)
            throw new InvalidOperationException("No se guardaron las facturas BPS:\n" + string.Join("\n", errores));

        await using var tx = await _db.Database.BeginTransactionAsync();
        _db.FacturasBps.RemoveRange(await _db.FacturasBps.Where(x => x.PeriodoId == p.Id).ToListAsync());
        foreach (var f in facturas)
        {
            _db.FacturasBps.Add(new FacturaBps
            {
                PeriodoId = p.Id,
                TipoObra = f.TipoObra,
                Descripcion = (f.Descripcion ?? string.Empty).Trim(),
                Importe = f.Importe
            });
        }

        var propia = await _db.BpsPeriodos.FirstOrDefaultAsync(x => x.PeriodoId == p.Id);
        if (tasaSede is null)
        {
            if (propia is not null)
                _db.BpsPeriodos.Remove(propia);
        }
        else if (propia is null)
        {
            _db.BpsPeriodos.Add(new BpsPeriodo { PeriodoId = p.Id, TasaSede = tasaSede.Value });
        }
        else
        {
            propia.TasaSede = tasaSede.Value;
        }

        var totalC = facturas.Where(f => f.TipoObra == TipoObra.Construccion).Sum(f => f.Importe);
        var totalIc = facturas.Where(f => f.TipoObra == TipoObra.IndustriaYComercio).Sum(f => f.Importe);
        Auditar(usuario, "Guardar facturas", codigo,
            $"Facturas: {facturas.Count}. Construcción {totalC:N2}. IyC {totalIc:N2}. Tasa Sede: {(tasaSede is null ? "sin cargar" : tasaSede.Value.ToString("P3", Uy))}.");

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return $"BPS de {codigo} guardado: {facturas.Count} facturas, Construcción {totalC.ToString("N2", Uy)} e IyC {totalIc.ToString("N2", Uy)}.";
    }

    public async Task<RepartoBpsResultadoDto> CalcularAsync(string periodo)
    {
        var codigo = (periodo ?? string.Empty).Trim();
        var p = await _db.Periodos.AsNoTracking().FirstOrDefaultAsync(x => x.Codigo == codigo)
            ?? throw new InvalidOperationException($"No existe el período {codigo}.");

        var facturas = await _db.FacturasBps.AsNoTracking().Where(x => x.PeriodoId == p.Id)
            .Select(x => new RepartoBps.Factura(x.TipoObra, x.Descripcion, x.Importe)).ToListAsync();
        var (tasa, tasaDe) = await TasaSedeAsync(p);

        var horas = await _db.HorasMensuales.AsNoTracking()
            .Include(x => x.Obra).Include(x => x.Funcionario)
            .Where(x => x.PeriodoId == p.Id && x.HorasEquivalentes > 0)
            .ToListAsync();

        // Quien solo tiene horas de IyC en la obra SEDE de IyC tiene la hora de referencia que el programa pone a
        // quien cobra en IyC sin horas: esa hora no se reparte. De ellos, la Sede son solo los administrativos
        // (categoría «Administrativo»): su BPS va por nominal × tasa. El de los demás queda en el reparto de IyC
        // por horas (decisión del usuario, 26/11/2026).
        var horasIc = horas.Where(h => h.Obra!.TipoObra == TipoObra.IndustriaYComercio).ToList();
        var soloEnSede = horasIc.GroupBy(h => h.FuncionarioId)
            .Where(g => g.All(h => EsSede(h.Obra!)))
            .ToDictionary(g => g.Key, g => g.First().Funcionario);
        var idsSede = soloEnSede.Where(x => EsAdministrativo(x.Value)).Select(x => x.Key).ToHashSet();
        var sinObra = soloEnSede.Where(x => !idsSede.Contains(x.Key))
            .Select(x => $"{x.Value?.NumeroFuncionario} {x.Value?.Nombre}").OrderBy(x => x, StringComparer.Ordinal).ToList();

        // Los metalúrgicos tienen 1 hora de referencia en su obra (Metalúrgica): su BPS va como el de la Sede,
        // nominal × tasa, y esas horas no se reparten (decisión del 26/11/2026). Se carga en la factura de la
        // Sede (aclaración del usuario del 27/11/2026: «metalúrgica se carga en sede»).
        var horasMet = horas.Where(h => h.Obra!.TipoObra == TipoObra.Metalurgica).ToList();
        var idsMet = horasMet.Select(h => h.FuncionarioId)
            .Where(id => horas.Where(h => h.FuncionarioId == id && h.Obra!.TipoObra is TipoObra.IndustriaYComercio or TipoObra.Metalurgica)
                .All(h => h.Obra!.TipoObra == TipoObra.Metalurgica))
            .ToHashSet();

        var todosLosPagos = await _db.PagosMensuales.AsNoTracking().Include(x => x.Funcionario)
            .Where(x => x.PeriodoId == p.Id && (x.TipoObra == TipoObra.IndustriaYComercio || x.TipoObra == TipoObra.Metalurgica))
            .ToListAsync();
        todosLosPagos = todosLosPagos.Where(x => !x.Observacion.Contains("TICKET", StringComparison.OrdinalIgnoreCase)).ToList();
        var pagosIc = todosLosPagos.Where(x => x.TipoObra == TipoObra.IndustriaYComercio).ToList();

        var metalurgicos = horasMet.Where(h => idsMet.Contains(h.FuncionarioId))
            .GroupBy(h => h.FuncionarioId)
            .Select(g => new RepartoBps.PersonaMetalurgica(
                g.First().Funcionario?.NumeroFuncionario ?? string.Empty,
                g.First().Funcionario?.Nombre ?? g.First().NombreFuncionarioExcel,
                todosLosPagos.Where(x => x.FuncionarioId == g.Key).Sum(x => x.Nominal)))
            .ToList();

        var sede = horasIc.Where(h => idsSede.Contains(h.FuncionarioId))
            .GroupBy(h => h.FuncionarioId)
            .Select(g => new RepartoBps.PersonaSede(
                g.First().Funcionario?.NumeroFuncionario ?? string.Empty,
                g.First().Funcionario?.Nombre ?? g.First().NombreFuncionarioExcel,
                pagosIc.Where(x => x.FuncionarioId == g.Key).Sum(x => x.Nominal)))
            .ToList();

        var horasReparto = horas
            .Where(h => !(h.Obra!.TipoObra == TipoObra.IndustriaYComercio && soloEnSede.ContainsKey(h.FuncionarioId)))
            .Where(h => !(h.Obra!.TipoObra == TipoObra.Metalurgica && idsMet.Contains(h.FuncionarioId)))
            .Select(h => (Tipo: TipoReparto(h.Obra!.TipoObra), h.Obra!, h.HorasEquivalentes))
            .Where(x => x.Tipo is not null)
            .GroupBy(x => (x.Tipo!.Value, x.Item2.Id))
            .Select(g => new RepartoBps.HorasObra(g.Key.Value, g.First().Item2.NumeroObra, g.First().Item2.Nombre,
                EsSede(g.First().Item2), g.Sum(x => x.HorasEquivalentes)))
            .OrderBy(x => x.Tipo).ThenBy(x => int.TryParse(x.NumeroObra, out var n) ? n : int.MaxValue).ThenBy(x => x.NumeroObra)
            .ToList();

        var r = RepartoBps.Calcular(codigo, facturas, tasa, horasReparto, sede, metalurgicos);

        if (tasaDe.Length > 0 && (sede.Count > 0 || metalurgicos.Count > 0))
            r.Avisos.Add($"La tasa de la Sede es la de {tasaDe}: confírmela con la factura de IyC de este mes y guarde.");
        if (sinObra.Count > 0)
            r.Avisos.Add($"Cobran en IyC sin horas en obras y no son administrativos: su BPS se reparte entre las obras de IyC por horas ({sinObra.Count}): {string.Join(", ", sinObra)}.");
        var fuera = horas.Where(h => h.Obra!.TipoObra is TipoObra.NA or TipoObra.Administracion)
            .GroupBy(h => h.Obra!.TipoObra)
            .Select(g => $"{(g.Key == TipoObra.NA ? "N-A" : "Administración")} {g.Sum(h => h.HorasEquivalentes).ToString("#,##0.##", Uy)} h")
            .ToList();
        if (fuera.Count > 0)
            r.Avisos.Add($"No entran en el reparto: {string.Join(" · ", fuera)}.");

        var conHorasIc = horas.Where(h => h.Obra!.TipoObra is TipoObra.IndustriaYComercio or TipoObra.Metalurgica)
            .Select(h => h.FuncionarioId).ToHashSet();
        var sinHoras = pagosIc.Where(x => x.TotalGenerado != 0 && !conHorasIc.Contains(x.FuncionarioId))
            .Select(x => $"{x.Funcionario?.NumeroFuncionario} {x.Funcionario?.Nombre}").Distinct().ToList();
        if (sinHoras.Count > 0)
            r.Avisos.Add($"Cobran en IyC sin horas de IyC (no están en la Sede; su BPS queda en el reparto por horas): {string.Join(", ", sinHoras)}.");

        return r;
    }

    public async Task<string> GenerarPlanillaSpAsync(string periodo, string carpetaDestino, string usuario)
    {
        var r = await CalcularAsync(periodo);
        if (!r.Listo)
            throw new InvalidOperationException("Todavía no se puede armar la planilla para SP:\n- " + string.Join("\n- ", r.Problemas));

        Directory.CreateDirectory(carpetaDestino);
        var ruta = Path.Combine(carpetaDestino, $"BPS_por_obra_{r.Periodo}.xlsx");
        using (var libro = new XLWorkbook())
        {
            HojaSp(libro.Worksheets.Add("Para SP"), r);
            HojaReparto(libro.Worksheets.Add("Reparto"), r);
            HojaSede(libro.Worksheets.Add("Sede y metalúrgicos"), r);
            var facturas = await ObtenerAsync(r.Periodo);
            HojaFacturas(libro.Worksheets.Add("Facturas"), facturas, r);
            libro.SaveAs(ruta);
        }

        Auditar(usuario, "Planilla para SP", r.Periodo,
            $"{r.FacturasSp.Count} facturas por {r.FacturasSp.Sum(f => f.Importe):N2}. Archivo: {Path.GetFileName(ruta)}.");
        await _db.SaveChangesAsync();
        return ruta;
    }

    // ---------------------------------------------------------------- Hojas del Excel

    private const string Numero = "#,##0.00";
    private static readonly XLColor Azul = XLColor.FromHtml("#16324F");
    private static readonly XLColor Gris = XLColor.FromHtml("#F3F4F6");

    private static void Titulo(IXLWorksheet ws, string texto, string bajada, int columnas)
    {
        ws.Cell(1, 1).Value = texto;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, columnas).Merge();
        ws.Cell(2, 1).Value = bajada;
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Range(2, 1, 2, columnas).Merge();
    }

    private static void Encabezado(IXLWorksheet ws, int fila, params string[] columnas)
    {
        for (var c = 0; c < columnas.Length; c++)
        {
            var celda = ws.Cell(fila, c + 1);
            celda.Value = columnas[c];
            celda.Style.Fill.BackgroundColor = Azul;
            celda.Style.Font.FontColor = XLColor.White;
            celda.Style.Font.Bold = true;
        }
    }

    private static void HojaSp(IXLWorksheet ws, RepartoBpsResultadoDto r)
    {
        Titulo(ws, $"BPS por obra {r.Periodo} · facturas para cargar en SP",
            "Proveedor BPS, factura a crédito sin IVA, una por obra con Construcción e IyC sumados, un renglón con el insumo BPS, imputada a la obra sin remito. La Sede va al depósito GASTOS2.", 9);
        Encabezado(ws, 4, "Número", "Fecha", "Proveedor", "Comprobante", "Imputada a", "Insumo", "Cantidad", "Importe", "Observación");
        var fila = 5;
        foreach (var f in r.FacturasSp)
        {
            ws.Cell(fila, 1).Value = f.Numero;
            ws.Cell(fila, 2).Value = f.Fecha;
            ws.Cell(fila, 2).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Cell(fila, 3).Value = "BPS (BANCO DE PREVISION SOCIAL)";
            ws.Cell(fila, 4).Value = "Crédito";
            ws.Cell(fila, 5).Value = f.Destino;
            ws.Cell(fila, 6).Value = "BPS";
            ws.Cell(fila, 7).Value = 1;
            ws.Cell(fila, 8).Value = f.Importe;
            ws.Cell(fila, 8).Style.NumberFormat.Format = Numero;
            ws.Cell(fila, 9).Value = f.Observacion;
            if (fila % 2 == 0)
                ws.Range(fila, 1, fila, 9).Style.Fill.BackgroundColor = Gris;
            fila++;
        }

        ws.Cell(fila, 1).Value = $"Total ({r.FacturasSp.Count} facturas)";
        ws.Cell(fila, 8).Value = r.FacturasSp.Sum(f => f.Importe);
        ws.Cell(fila, 8).Style.NumberFormat.Format = Numero;
        ws.Range(fila, 1, fila, 9).Style.Font.Bold = true;
        ws.Cell(fila + 2, 1).Value = "Antes de cargar: revise en SP que ese mes no tenga ya facturas BPS por obra o un total al depósito GASTOS2, para no duplicar el gasto.";
        ws.Cell(fila + 2, 1).Style.Font.Italic = true;
        ws.Columns().AdjustToContents();
    }

    private static void HojaReparto(IXLWorksheet ws, RepartoBpsResultadoDto r)
    {
        Titulo(ws, $"Reparto del BPS {r.Periodo} por obra",
            "Construcción por las horas equivalentes de Construcción. De Industria y Comercio primero salen la Sede y los metalúrgicos, y el resto va por las horas de IyC.", 6);
        var resumen = new (string, decimal, string)[]
        {
            ("Facturado Construcción", r.FacturadoConstruccion, Numero),
            ("Facturado Industria y Comercio", r.FacturadoIndustria, Numero),
            ("Tasa BPS de la Sede", r.TasaSede, "0.000%"),
            ("Sede (nominal × tasa)", r.Sede, Numero),
            ("Metalúrgica (nominal × tasa, a la Sede)", r.Metalurgica, Numero),
            ("A repartir Construcción", r.ARepartirConstruccion, Numero),
            ("A repartir Industria y Comercio", r.ARepartirIndustria, Numero)
        };
        var fila = 4;
        foreach (var (texto, valor, formato) in resumen)
        {
            ws.Cell(fila, 1).Value = texto;
            ws.Cell(fila, 2).Value = valor;
            ws.Cell(fila, 2).Style.NumberFormat.Format = formato;
            fila++;
        }

        fila++;
        Encabezado(ws, fila, "Tipo", "N° obra", "Obra", "Horas equiv.", "% de su tipo", "Importe");
        fila++;
        foreach (var grupo in r.Lineas.GroupBy(l => l.TipoObra))
        {
            foreach (var l in grupo)
            {
                ws.Cell(fila, 1).Value = l.Tipo;
                ws.Cell(fila, 2).Value = l.NumeroObra;
                ws.Cell(fila, 3).Value = l.NombreObra;
                ws.Cell(fila, 4).Value = l.Horas;
                ws.Cell(fila, 4).Style.NumberFormat.Format = Numero;
                ws.Cell(fila, 5).Value = l.Porcentaje;
                ws.Cell(fila, 5).Style.NumberFormat.Format = "0.00%";
                ws.Cell(fila, 6).Value = l.Importe;
                ws.Cell(fila, 6).Style.NumberFormat.Format = Numero;
                fila++;
            }

            ws.Cell(fila, 1).Value = $"Total {grupo.First().Tipo}";
            ws.Cell(fila, 4).Value = grupo.Sum(l => l.Horas);
            ws.Cell(fila, 4).Style.NumberFormat.Format = Numero;
            ws.Cell(fila, 6).Value = grupo.Sum(l => l.Importe);
            ws.Cell(fila, 6).Style.NumberFormat.Format = Numero;
            ws.Range(fila, 1, fila, 6).Style.Font.Bold = true;
            fila += 2;
        }

        if (r.Avisos.Count > 0)
        {
            ws.Cell(fila, 1).Value = "Avisos";
            ws.Cell(fila, 1).Style.Font.Bold = true;
            foreach (var aviso in r.Avisos)
                ws.Cell(++fila, 1).Value = aviso;
        }

        ws.Columns().AdjustToContents();
    }

    private static void HojaSede(IXLWorksheet ws, RepartoBpsResultadoDto r)
    {
        Titulo(ws, $"BPS por nominal × tasa {r.Periodo}",
            $"Nominal × tasa {r.TasaSede.ToString("P3", Uy)}. Los administrativos de IyC y los metalúrgicos; todo se carga en la factura de la Sede.", 5);
        Encabezado(ws, 4, "N° Func.", "Funcionario", "Va a", "Nominal", "BPS");
        var fila = 5;
        foreach (var p in r.PersonasSede)
        {
            ws.Cell(fila, 1).Value = p.NumeroFuncionario;
            ws.Cell(fila, 2).Value = p.Nombre;
            ws.Cell(fila, 3).Value = p.Destino;
            ws.Cell(fila, 4).Value = p.Nominal;
            ws.Cell(fila, 5).Value = p.Bps;
            ws.Range(fila, 4, fila, 5).Style.NumberFormat.Format = Numero;
            fila++;
        }

        ws.Cell(fila, 1).Value = "Total";
        ws.Cell(fila, 4).Value = r.PersonasSede.Sum(p => p.Nominal);
        ws.Cell(fila, 5).Value = r.Sede + r.Metalurgica;
        ws.Range(fila, 4, fila, 5).Style.NumberFormat.Format = Numero;
        ws.Range(fila, 1, fila, 5).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
    }

    private static void HojaFacturas(IXLWorksheet ws, BpsMesDto mes, RepartoBpsResultadoDto r)
    {
        Titulo(ws, $"Facturas BPS {r.Periodo}", "Lo cargado en la pestaña BPS por obra.", 3);
        Encabezado(ws, 4, "Empresa", "Factura", "Importe");
        var fila = 5;
        foreach (var f in mes.Facturas)
        {
            ws.Cell(fila, 1).Value = f.TipoObra == TipoObra.Construccion ? "Construcción" : "Industria y Comercio";
            ws.Cell(fila, 2).Value = f.Descripcion;
            ws.Cell(fila, 3).Value = f.Importe;
            ws.Cell(fila, 3).Style.NumberFormat.Format = Numero;
            fila++;
        }

        ws.Cell(fila, 1).Value = "Total";
        ws.Cell(fila, 3).Value = mes.Facturas.Sum(f => f.Importe);
        ws.Cell(fila, 3).Style.NumberFormat.Format = Numero;
        ws.Range(fila, 1, fila, 3).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
    }

    // ---------------------------------------------------------------- Auxiliares

    // La tasa propia del mes o, si no tiene, la del último mes anterior que la tenga.
    private async Task<(decimal? Tasa, string De)> TasaSedeAsync(Periodo p)
    {
        var propia = await _db.BpsPeriodos.AsNoTracking().Where(x => x.PeriodoId == p.Id).Select(x => (decimal?)x.TasaSede).FirstOrDefaultAsync();
        if (propia is not null)
            return (propia, string.Empty);

        var tasas = await _db.BpsPeriodos.AsNoTracking()
            .Select(x => new { x.TasaSede, x.Periodo!.Codigo })
            .ToListAsync();
        var anterior = tasas
            .Where(x => string.CompareOrdinal(x.Codigo, p.Codigo) < 0)
            .OrderByDescending(x => x.Codigo, StringComparer.Ordinal)
            .FirstOrDefault();
        return anterior is null ? (null, string.Empty) : (anterior.TasaSede, anterior.Codigo);
    }

    private static bool EsSede(Obra obra) => obra.Nombre.Contains("SEDE", StringComparison.OrdinalIgnoreCase);

    private static bool EsAdministrativo(Funcionario? f) => (f?.Categoria ?? string.Empty).Contains("ADMIN", StringComparison.OrdinalIgnoreCase);

    // Construcción va con Construcción; IyC con IyC. Metalúrgica también va con IyC (la paga la empresa de IyC), pero solo
    // las horas de quien además tiene horas en obras de IyC: los metalúrgicos van por nominal × tasa. N-A y Administración no entran.
    private static TipoObra? TipoReparto(TipoObra tipo) => tipo switch
    {
        TipoObra.Construccion => TipoObra.Construccion,
        TipoObra.IndustriaYComercio or TipoObra.Metalurgica => TipoObra.IndustriaYComercio,
        _ => null
    };

    private void Auditar(string usuario, string accion, string periodo, string detalle)
    {
        _db.AuditoriaEventos.Add(new AuditoriaEvento
        {
            Usuario = usuario,
            Modulo = "BPS por obra",
            Accion = accion,
            Entidad = "Periodo",
            EntidadClave = periodo,
            Detalle = detalle.Length > 500 ? detalle[..500] : detalle
        });
    }
}
