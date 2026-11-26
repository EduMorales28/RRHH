using System.Globalization;
using System.Text.RegularExpressions;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Infrastructure.Services;

// Reparto del BPS del mes por obra, como la planilla "Distribución BPS por Obras – C y IC" de ALMIRTAUN:
// - Construcción: el total de las facturas BPS de Construcción se reparte entre las obras por sus horas.
// - Industria y Comercio: primero se separa la Sede (nominal de cada administrativo × tasa) y el resto
//   se reparte entre las obras por sus horas.
// Los importes van al centésimo: lo que falta por redondeo va a los restos mayores, así cada tipo suma
// exactamente lo facturado. Con un dato pendiente no se reparte nada (pendiente no es cero).
// Es un cálculo sin base de datos: DistribucionBpsService le pasa los datos del mes.
public static class RepartoBps
{
    public sealed record Factura(TipoObra Tipo, string Descripcion, decimal Importe);

    // Horas de una obra en un tipo (Construcción o IndustriaYComercio). EsSede: se carga en la factura de la Sede.
    public sealed record HorasObra(TipoObra Tipo, string NumeroObra, string NombreObra, bool EsSede, decimal Horas);

    public sealed record PersonaSede(string NumeroFuncionario, string Nombre, decimal Nominal);

    private static readonly CultureInfo Uy = CultureInfo.GetCultureInfo("es-UY");
    private static string Pesos(decimal v) => v.ToString("#,##0.00", Uy);
    private static string Nombre(TipoObra t) => t == TipoObra.Construccion ? "Construcción" : "Industria y Comercio";

    public static RepartoBpsResultadoDto Calcular(string periodo, IReadOnlyList<Factura> facturas, decimal? tasaSede,
        IReadOnlyList<HorasObra> horas, IReadOnlyList<PersonaSede> sede)
    {
        if (!Regex.IsMatch(periodo ?? string.Empty, "^\\d{4}-(0[1-9]|1[0-2])$"))
            throw new ArgumentException("Período inválido: usar AAAA-MM.", nameof(periodo));

        var r = new RepartoBpsResultadoDto { Periodo = periodo! };
        var tipos = new[] { TipoObra.Construccion, TipoObra.IndustriaYComercio };

        foreach (var f in facturas.Where(f => !tipos.Contains(f.Tipo)))
            r.Problemas.Add($"La factura «{f.Descripcion}» no es de Construcción ni de Industria y Comercio.");
        foreach (var f in facturas.Where(f => tipos.Contains(f.Tipo) && f.Importe <= 0))
            r.Problemas.Add($"{Nombre(f.Tipo)}: la factura «{Texto(f.Descripcion)}» no tiene importe.");

        r.FacturadoConstruccion = facturas.Where(f => f.Tipo == TipoObra.Construccion && f.Importe > 0).Sum(f => f.Importe);
        r.FacturadoIndustria = facturas.Where(f => f.Tipo == TipoObra.IndustriaYComercio && f.Importe > 0).Sum(f => f.Importe);

        if (sede.Count > 0 && (tasaSede is null || tasaSede <= 0 || tasaSede >= 1))
            r.Problemas.Add("Falta la tasa BPS de la Sede (por ejemplo 39,656 %).");
        foreach (var p in sede.Where(p => p.Nominal <= 0))
            r.Problemas.Add($"Sede: falta el nominal de {p.NumeroFuncionario} {p.Nombre}. Cargue de nuevo la planilla de ábaco de IyC del mes y guarde los pagos.");

        r.HorasConstruccion = horas.Where(h => h.Tipo == TipoObra.Construccion).Sum(h => h.Horas);
        r.HorasIndustria = horas.Where(h => h.Tipo == TipoObra.IndustriaYComercio).Sum(h => h.Horas);
        if (r.Problemas.Count > 0)
            return r;

        // Sede: nominal total × tasa, redondeado al centésimo, y por persona según su nominal.
        r.TasaSede = tasaSede ?? 0m;
        if (sede.Count > 0)
        {
            var nominal = sede.Sum(p => p.Nominal);
            var sedeCent = (long)Math.Round(nominal * r.TasaSede * 100m, MidpointRounding.AwayFromZero);
            var porPersona = RepartirCentesimos(sedeCent, sede.Select(p => p.Nominal).ToList());
            r.Sede = sedeCent / 100m;
            r.PersonasSede = sede.Select((p, i) => new SedeBpsPersonaDto
            {
                NumeroFuncionario = p.NumeroFuncionario,
                Nombre = p.Nombre,
                Nominal = p.Nominal,
                Bps = porPersona[i] / 100m
            }).OrderBy(p => p.Nombre, StringComparer.Create(Uy, true)).ToList();
        }

        r.ARepartirConstruccion = r.FacturadoConstruccion;
        r.ARepartirIndustria = r.FacturadoIndustria - r.Sede;
        if (r.ARepartirIndustria < 0)
            r.Problemas.Add($"Industria y Comercio: el BPS de la Sede ({Pesos(r.Sede)}) supera lo facturado ({Pesos(r.FacturadoIndustria)}). Revise la tasa o las facturas.");

        foreach (var tipo in tipos)
        {
            var aRepartir = tipo == TipoObra.Construccion ? r.ARepartirConstruccion : r.ARepartirIndustria;
            var horasTipo = tipo == TipoObra.Construccion ? r.HorasConstruccion : r.HorasIndustria;
            if (aRepartir > 0 && horasTipo <= 0)
                r.Problemas.Add($"{Nombre(tipo)}: hay {Pesos(aRepartir)} para repartir y ninguna obra con horas.");
            if (horasTipo > 0 && !facturas.Any(f => f.Tipo == tipo))
                r.Problemas.Add($"{Nombre(tipo)}: hay obras con horas y faltan las facturas BPS.");
        }

        if (r.Problemas.Count > 0)
            return r;

        foreach (var tipo in tipos)
        {
            var filas = horas.Where(h => h.Tipo == tipo && h.Horas > 0).ToList();
            if (filas.Count == 0)
                continue;
            var aRepartir = tipo == TipoObra.Construccion ? r.ARepartirConstruccion : r.ARepartirIndustria;
            var total = filas.Sum(h => h.Horas);
            var centesimos = RepartirCentesimos((long)Math.Round(aRepartir * 100m), filas.Select(h => h.Horas).ToList());
            for (var i = 0; i < filas.Count; i++)
            {
                r.Lineas.Add(new RepartoBpsLineaDto
                {
                    TipoObra = tipo,
                    NumeroObra = filas[i].NumeroObra,
                    NombreObra = filas[i].NombreObra,
                    EsSede = filas[i].EsSede,
                    Horas = filas[i].Horas,
                    Porcentaje = Math.Round(filas[i].Horas / total, 6),
                    Importe = centesimos[i] / 100m
                });
            }
        }

        r.FacturasSp = FacturasParaSp(periodo!, r);
        return r;
    }

    // Una factura por obra con C e IC sumados; la Sede (su BPS de administración más el reparto de sus horas)
    // va al depósito GASTOS2. Número = MMAAAA + obra, fecha = último día del mes, como en SP hasta 10/2025.
    private static List<FacturaSpDto> FacturasParaSp(string periodo, RepartoBpsResultadoDto r)
    {
        var anio = int.Parse(periodo[..4], CultureInfo.InvariantCulture);
        var mes = int.Parse(periodo[5..], CultureInfo.InvariantCulture);
        var fecha = new DateTime(anio, mes, DateTime.DaysInMonth(anio, mes));
        var prefijo = $"{mes:00}{anio}";

        var porObra = r.Lineas.Where(l => !l.EsSede)
            .GroupBy(l => l.NumeroObra)
            .Select(g => new FacturaSpDto
            {
                Numero = prefijo + g.Key,
                Fecha = fecha,
                NumeroObra = g.Key,
                Destino = $"Obra {g.Key}",
                Observacion = g.Select(l => l.NombreObra).First(),
                Importe = g.Sum(l => l.Importe)
            })
            .OrderBy(f => int.TryParse(f.NumeroObra, out var n) ? n : int.MaxValue)
            .ThenBy(f => f.NumeroObra, StringComparer.Ordinal)
            .ToList();

        var sede = r.Sede + r.Lineas.Where(l => l.EsSede).Sum(l => l.Importe);
        if (sede > 0)
        {
            porObra.Add(new FacturaSpDto
            {
                Numero = prefijo + "SEDE",
                Fecha = fecha,
                NumeroObra = "SEDE",
                Destino = "Depósito GASTOS2 (Sede)",
                Observacion = "SEDE",
                Importe = sede
            });
        }

        return porObra;
    }

    // Reparte un total en centésimos según los pesos: redondea hacia abajo y da los centésimos que faltan
    // a los restos mayores (a igual resto, al primero).
    public static long[] RepartirCentesimos(long total, IReadOnlyList<decimal> pesos)
    {
        var suma = pesos.Sum();
        if (suma <= 0)
            throw new InvalidOperationException("No hay pesos para repartir.");

        var exactos = pesos.Select(p => total * p / suma).ToArray();
        var resultado = exactos.Select(e => (long)Math.Floor(e)).ToArray();
        var faltan = total - resultado.Sum();
        foreach (var i in exactos.Select((e, i) => (i, resto: e - resultado[i]))
                     .OrderByDescending(x => x.resto).ThenBy(x => x.i).Select(x => x.i))
        {
            if (faltan <= 0)
                break;
            resultado[i]++;
            faltan--;
        }

        return resultado;
    }

    private static string Texto(string s) => string.IsNullOrWhiteSpace(s) ? "sin nombre" : s.Trim();
}
