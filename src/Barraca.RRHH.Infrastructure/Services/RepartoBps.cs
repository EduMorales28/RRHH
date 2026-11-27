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
    // CodigoSp: número de la obra en SP si no es el mismo (vacío = NumeroObra).
    public sealed record HorasObra(TipoObra Tipo, string NumeroObra, string NombreObra, bool EsSede, decimal Horas, string CodigoSp = "");

    public sealed record PersonaSede(string NumeroFuncionario, string Nombre, decimal Nominal);

    // Metalúrgico: tiene 1 hora de referencia en su obra, así que su BPS va como el de la Sede (nominal × tasa)
    // y se resta del BPS de IyC (decisión del usuario, 26/11/2026). Se carga en la factura de la Sede (27/11/2026).
    public sealed record PersonaMetalurgica(string NumeroFuncionario, string Nombre, decimal Nominal);

    private static readonly CultureInfo Uy = CultureInfo.GetCultureInfo("es-UY");
    private static string Pesos(decimal v) => v.ToString("#,##0.00", Uy);
    private static string Nombre(TipoObra t) => t == TipoObra.Construccion ? "Construcción" : "Industria y Comercio";

    public static RepartoBpsResultadoDto Calcular(string periodo, IReadOnlyList<Factura> facturas, decimal? tasaSede,
        IReadOnlyList<HorasObra> horas, IReadOnlyList<PersonaSede> sede, IReadOnlyList<PersonaMetalurgica>? metalurgicos = null)
    {
        metalurgicos ??= Array.Empty<PersonaMetalurgica>();
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

        if ((sede.Count > 0 || metalurgicos.Count > 0) && (tasaSede is null || tasaSede <= 0 || tasaSede >= 1))
            r.Problemas.Add("Falta la tasa BPS de la Sede (por ejemplo 39,656 %).");
        foreach (var p in sede.Where(p => p.Nominal <= 0))
            r.Problemas.Add($"Sede: falta el nominal de {p.NumeroFuncionario} {p.Nombre}. Cargue de nuevo la planilla de ábaco de IyC del mes y guarde los pagos.");
        foreach (var p in metalurgicos.Where(p => p.Nominal <= 0))
            r.Problemas.Add($"Metalúrgica: falta el nominal de {p.NumeroFuncionario} {p.Nombre}. Escríbalo en Pagos del mes, columna Nominal, y guarde.");

        r.HorasConstruccion = horas.Where(h => h.Tipo == TipoObra.Construccion).Sum(h => h.Horas);
        r.HorasIndustria = horas.Where(h => h.Tipo == TipoObra.IndustriaYComercio).Sum(h => h.Horas);
        if (r.Problemas.Count > 0)
            return r;

        // Sede y metalúrgicos: nominal total de cada grupo × tasa, redondeado al centésimo, y por persona según su nominal.
        r.TasaSede = tasaSede ?? 0m;
        var porTasa = new List<SedeBpsPersonaDto>();
        r.Sede = PorTasa(sede.Select(p => (p.NumeroFuncionario, p.Nombre, p.Nominal)).ToList(), r.TasaSede, false, porTasa);
        r.Metalurgica = PorTasa(metalurgicos.Select(p => (p.NumeroFuncionario, p.Nombre, p.Nominal)).ToList(), r.TasaSede, true, porTasa);
        r.PersonasSede = porTasa.OrderBy(p => p.EsMetalurgica).ThenBy(p => p.Nombre, StringComparer.Create(Uy, true)).ToList();

        r.ARepartirConstruccion = r.FacturadoConstruccion;
        r.ARepartirIndustria = r.FacturadoIndustria - r.Sede - r.Metalurgica;
        if (r.ARepartirIndustria < 0)
            r.Problemas.Add($"Industria y Comercio: el BPS de la Sede ({Pesos(r.Sede)}) y de Metalúrgica ({Pesos(r.Metalurgica)}) supera lo facturado ({Pesos(r.FacturadoIndustria)}). Revise la tasa o las facturas.");

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
                    CodigoSp = string.IsNullOrWhiteSpace(filas[i].CodigoSp) ? filas[i].NumeroObra : filas[i].CodigoSp.Trim(),
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

    // BPS de un grupo por nominal × tasa: total redondeado al centésimo y repartido entre las personas por su nominal.
    // Los dos grupos (administrativos y metalúrgicos) se cargan en la factura de la Sede.
    private static decimal PorTasa(IReadOnlyList<(string Numero, string Nombre, decimal Nominal)> personas,
        decimal tasa, bool esMetalurgica, List<SedeBpsPersonaDto> salida)
    {
        if (personas.Count == 0)
            return 0m;

        var centesimos = (long)Math.Round(personas.Sum(p => p.Nominal) * tasa * 100m, MidpointRounding.AwayFromZero);
        var porPersona = RepartirCentesimos(centesimos, personas.Select(p => p.Nominal).ToList());
        salida.AddRange(personas.Select((p, i) => new SedeBpsPersonaDto
        {
            NumeroFuncionario = p.Numero,
            Nombre = p.Nombre,
            Nominal = p.Nominal,
            Bps = porPersona[i] / 100m,
            EsMetalurgica = esMetalurgica,
            Destino = esMetalurgica ? "Sede · metalúrgico" : "Sede"
        }));
        return centesimos / 100m;
    }

    // Una factura por obra con C e IC sumados; la Sede (su BPS de administración más el reparto de sus horas)
    // va al depósito GASTOS2. Número = MMAAAA + obra, fecha = último día del mes, como en SP hasta 10/2025.
    private static List<FacturaSpDto> FacturasParaSp(string periodo, RepartoBpsResultadoDto r)
    {
        var anio = int.Parse(periodo[..4], CultureInfo.InvariantCulture);
        var mes = int.Parse(periodo[5..], CultureInfo.InvariantCulture);
        var fecha = new DateTime(anio, mes, DateTime.DaysInMonth(anio, mes));
        var prefijo = $"{mes:00}{anio}";

        // Una factura por obra del programa (C + IC). Va al número de la obra en SP; si dos obras van al mismo
        // número de SP (por ejemplo, la 079), cada una lleva su factura con letra (79A, 79B) y su nombre en la
        // observación, como en SP en 08/2025. El número de factura va sin ceros adelante, como «10202539» (obra 039).
        var porObra = new List<FacturaSpDto>();
        var obras = r.Lineas.Where(l => !l.EsSede)
            .GroupBy(l => l.NumeroObra)
            .Select(g => (Sp: g.First().CodigoSp, Nombre: g.First().NombreObra, Numero: g.Key, Importe: g.Sum(l => l.Importe)));
        foreach (var sp in obras.GroupBy(o => o.Sp))
        {
            var lista = sp.OrderBy(o => int.TryParse(o.Numero, out var n) ? n : int.MaxValue).ThenBy(o => o.Numero, StringComparer.Ordinal).ToList();
            var codigo = sp.Key.TrimStart('0') is { Length: > 0 } sinCeros ? sinCeros : "0";
            for (var i = 0; i < lista.Count; i++)
            {
                porObra.Add(new FacturaSpDto
                {
                    Numero = prefijo + codigo + (lista.Count > 1 ? ((char)('A' + i)).ToString() : string.Empty),
                    Fecha = fecha,
                    NumeroObra = sp.Key,
                    Destino = $"Obra {sp.Key}",
                    Observacion = lista[i].Nombre,
                    Importe = lista[i].Importe
                });
            }
        }

        porObra = porObra
            .OrderBy(f => int.TryParse(f.NumeroObra, out var n) ? n : int.MaxValue)
            .ThenBy(f => f.NumeroObra, StringComparer.Ordinal)
            .ThenBy(f => f.Numero, StringComparer.Ordinal)
            .ToList();

        // La Sede: BPS de los administrativos y de los metalúrgicos, más el reparto por horas de las obras SEDE.
        var sede = r.Sede + r.Metalurgica + r.Lineas.Where(l => l.EsSede).Sum(l => l.Importe);
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
