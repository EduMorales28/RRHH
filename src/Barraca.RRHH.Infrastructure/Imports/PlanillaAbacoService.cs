using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Data;
using Barraca.RRHH.Infrastructure.Helpers;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Barraca.RRHH.Infrastructure.Imports;

// Lectura de la "Planilla de Sueldos a Pagar" de ábaco y emparejamiento de sus códigos con los funcionarios.
// Columnas: Código, Empleado, A.L., $, Nominal, Aportes, Adelantos, Ret. Jud., Pago.
public class PlanillaAbacoService : IPlanillaAbacoService
{
    private static readonly string[] Meses =
        ["ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SETIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];

    private readonly BarracaDbContext _db;

    public PlanillaAbacoService(BarracaDbContext db)
    {
        _db = db;
    }

    // ---------------------------------------------------------------- Lectura del PDF

    public PlanillaAbacoDto Leer(string rutaPdf)
    {
        List<string> lineas;
        try
        {
            using var documento = PdfDocument.Open(rutaPdf);
            lineas = documento.GetPages().SelectMany(LineasDePagina).ToList();
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"No se pudo leer el PDF '{Path.GetFileName(rutaPdf)}'. Verifique que sea la planilla de ábaco y que no esté abierto en otro programa.", ex);
        }

        var planilla = new PlanillaAbacoDto { Archivo = Path.GetFileName(rutaPdf) };
        LeerEncabezado(lineas, planilla);

        foreach (var linea in lineas)
        {
            if (linea.Contains("Totales", StringComparison.OrdinalIgnoreCase))
            {
                planilla.TotalesPie = Regex.Matches(linea, @"-?[\d,]*\d\.\d\d").Select(m => Numero(m.Value)).ToList();
                continue;
            }

            if (LeerFila(linea) is { } fila)
                planilla.Filas.Add(fila);
        }

        if (planilla.Filas.Count == 0)
            throw new InvalidOperationException($"'{planilla.Archivo}' no tiene filas de empleados reconocibles. ¿Es la 'Planilla de Sueldos a Pagar' de ábaco?");

        var calculados = new[]
        {
            planilla.Filas.Sum(f => f.Nominal), planilla.Filas.Sum(f => f.Aportes), planilla.Filas.Sum(f => f.Adelantos),
            planilla.Filas.Sum(f => f.RetencionJudicial), planilla.Filas.Sum(f => f.Pago)
        };
        planilla.TotalesVerificados = planilla.TotalesPie.Count == calculados.Length
            && planilla.TotalesPie.OrderBy(x => x).Zip(calculados.OrderBy(x => x)).All(p => Math.Abs(p.First - p.Second) < 0.01m);

        return planilla;
    }

    // Reconstruye las líneas de texto agrupando las palabras por altura y ordenándolas de izquierda a derecha.
    private static IEnumerable<string> LineasDePagina(Page pagina)
    {
        var palabras = pagina.GetWords().OrderByDescending(p => p.BoundingBox.Bottom).ThenBy(p => p.BoundingBox.Left).ToList();
        var lineas = new List<List<Word>>();
        foreach (var palabra in palabras)
        {
            var linea = lineas.LastOrDefault();
            if (linea is not null && Math.Abs(linea[0].BoundingBox.Bottom - palabra.BoundingBox.Bottom) <= 3)
                linea.Add(palabra);
            else
                lineas.Add([palabra]);
        }

        return lineas.Select(l => string.Join(' ', l.OrderBy(p => p.BoundingBox.Left).Select(p => p.Text)));
    }

    private static void LeerEncabezado(List<string> lineas, PlanillaAbacoDto planilla)
    {
        var texto = string.Join('\n', lineas);

        // Los valores junto a "EMPRESA:" y "MES:" no vienen como texto en el PDF de ábaco: la empresa se toma del título.
        var titulo = lineas.FirstOrDefault(l => l.Contains("ALMIRTAUN", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        var corte = titulo.IndexOf("Emisi", StringComparison.OrdinalIgnoreCase);
        planilla.Empresa = (corte > 0 ? titulo[..corte] : titulo).Trim();
        var referencia = NombresHelper.Normalizar(planilla.Empresa);
        if (Regex.IsMatch(referencia, @"\bIND\b|INDUSTRIA"))
            planilla.TipoObra = TipoObra.IndustriaYComercio;
        else if (referencia.Contains("CONSTRUC"))
            planilla.TipoObra = TipoObra.Construccion;
        else
            throw new InvalidOperationException($"En '{planilla.Archivo}' no se reconoce la empresa (se esperaba Construcción o Industria y Comercio).");

        var mes = Regex.Match(texto, @"MES:\s*([A-Za-zÁÉÍÓÚáéíóú]+)\s+(\d{4})");
        if (mes.Success)
        {
            planilla.Mes = $"{mes.Groups[1].Value} {mes.Groups[2].Value}";
            var nombreMes = NombresHelper.Normalizar(mes.Groups[1].Value).Replace("SEPTIEMBRE", "SETIEMBRE");
            var indice = Array.IndexOf(Meses, nombreMes);
            if (indice >= 0)
                planilla.Periodo = $"{mes.Groups[2].Value}-{indice + 1:00}";
        }
    }

    private static FilaAbacoDto? LeerFila(string linea)
    {
        var partes = linea.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (partes.Count < 8 || !Regex.IsMatch(partes[0], @"^\d+$"))
            return null;

        // Los 5 montos están al final de la línea.
        var montos = new List<decimal>();
        while (montos.Count < 5 && partes.Count > 1 && Regex.IsMatch(partes[^1].TrimStart('$'), @"^-?[\d,]*\d\.\d\d$"))
        {
            montos.Insert(0, Numero(partes[^1].TrimStart('$')));
            partes.RemoveAt(partes.Count - 1);
        }

        if (montos.Count != 5)
            return null;

        // Antes de los montos: "$" y la columna A.L.
        if (partes.Count > 1 && partes[^1] == "$")
            partes.RemoveAt(partes.Count - 1);
        if (partes.Count > 2 && Regex.IsMatch(partes[^1], @"^\d$"))
            partes.RemoveAt(partes.Count - 1);

        var nombre = string.Join(' ', partes.Skip(1)).Trim();
        if (nombre.Length == 0)
            return null;

        return new FilaAbacoDto
        {
            Codigo = partes[0],
            Nombre = nombre,
            Nominal = montos[0],
            Aportes = montos[1],
            Adelantos = montos[2],
            RetencionJudicial = montos[3],
            Pago = montos[4]
        };
    }

    // ábaco usa coma para miles y punto para decimales: "44,261.00", "38153.00", "-396.00".
    private static decimal Numero(string texto) =>
        decimal.Parse(texto.Replace(",", string.Empty), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- Emparejamiento con funcionarios

    public async Task<List<EmparejamientoAbacoDto>> EmparejarAsync(IEnumerable<PlanillaAbacoDto> planillas)
    {
        var funcionarios = await _db.Funcionarios.AsNoTracking().ToListAsync();
        var porNumero = funcionarios
            .GroupBy(f => f.NumeroFuncionario.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var porId = funcionarios.ToDictionary(f => f.Id);
        var recordados = await _db.CodigosAbaco.AsNoTracking().ToListAsync();
        // En cuántos funcionarios aparece cada palabra del nombre (para saber si un apellido es único).
        var frecuencia = NombresHelper.Frecuencias(funcionarios.Select(f => f.Nombre));

        var resultado = new List<EmparejamientoAbacoDto>();
        foreach (var planilla in planillas)
        {
            foreach (var fila in planilla.Filas)
            {
                var item = new EmparejamientoAbacoDto { TipoObra = planilla.TipoObra, Codigo = fila.Codigo };
                resultado.Add(item);

                var recordado = recordados.FirstOrDefault(r => r.TipoObra == planilla.TipoObra && r.Codigo == fila.Codigo);
                if (recordado is not null && porId.TryGetValue(recordado.FuncionarioId, out var f0))
                {
                    item.NumeroFuncionario = f0.NumeroFuncionario;
                    item.Estado = EstadosAbaco.Recordado;
                    continue;
                }

                var tokensAbaco = NombresHelper.Tokens(fila.Nombre);
                if (porNumero.TryGetValue(fila.Codigo, out var mismoNumero)
                    && NombresHelper.Coincidencias(tokensAbaco, NombresHelper.Tokens(mismoNumero.Nombre)) >= Math.Min(2, Math.Max(1, NombresHelper.Tokens(mismoNumero.Nombre).Count)))
                {
                    item.NumeroFuncionario = mismoNumero.NumeroFuncionario;
                    item.Estado = EstadosAbaco.MismoCodigo;
                    continue;
                }

                item.RequiereRevision = true;

                // Mismo número con el nombre parecido a medias (ej. "Pedro Salaberry" / "SALABERRY FONTES, PABLO"): se sugiere igual.
                if (mismoNumero is not null && NombresHelper.Coincidencias(tokensAbaco, NombresHelper.Tokens(mismoNumero.Nombre)) >= 1)
                {
                    item.NumeroFuncionario = mismoNumero.NumeroFuncionario;
                    item.Estado = EstadosAbaco.MismoCodigoNombreParecido;
                    continue;
                }

                // Por nombre: al menos dos palabras en común, o un apellido que nadie más tiene (ej. "Ferrizo").
                // Ante empate, en códigos 10xx se prefiere el número 1000xx? del programa.
                var candidato = funcionarios
                    .Select(f => (Funcionario: f, Puntaje: NombresHelper.Coincidencias(tokensAbaco, NombresHelper.Tokens(f.Nombre)), Unica: NombresHelper.CoincideTokenUnico(tokensAbaco, NombresHelper.Tokens(f.Nombre), frecuencia)))
                    .Where(c => c.Puntaje >= 2 || c.Unica)
                    .OrderByDescending(c => c.Puntaje)
                    .ThenByDescending(c => EsSegundoNumero(fila.Codigo, c.Funcionario.NumeroFuncionario))
                    .ThenBy(c => c.Funcionario.NumeroFuncionario.Length)
                    .Select(c => c.Funcionario)
                    .FirstOrDefault();

                if (candidato is not null)
                {
                    item.NumeroFuncionario = candidato.NumeroFuncionario;
                    item.Estado = mismoNumero is not null ? EstadosAbaco.CodigoOtraPersona : EstadosAbaco.SugeridoPorNombre;
                }
                else
                {
                    item.Estado = mismoNumero is not null ? EstadosAbaco.CodigoOtraPersona : EstadosAbaco.NoExiste;
                }
            }
        }

        return resultado;
    }

    public async Task GuardarCodigosAsync(IEnumerable<(TipoObra TipoObra, string Codigo, string NumeroFuncionario)> codigos, string usuario)
    {
        var funcionarios = (await _db.Funcionarios.ToListAsync())
            .GroupBy(f => f.NumeroFuncionario.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var existentes = await _db.CodigosAbaco.ToListAsync();

        var cambios = 0;
        foreach (var (tipo, codigo, numero) in codigos)
        {
            if (!funcionarios.TryGetValue(numero.Trim(), out var funcionarioId))
                throw new InvalidOperationException($"No existe el funcionario {numero} para el código {codigo} de ábaco.");

            var registro = existentes.FirstOrDefault(x => x.TipoObra == tipo && x.Codigo == codigo);
            if (registro is null)
            {
                registro = new CodigoAbaco { TipoObra = tipo, Codigo = codigo };
                _db.CodigosAbaco.Add(registro);
                existentes.Add(registro);
            }

            if (registro.FuncionarioId != funcionarioId)
            {
                registro.FuncionarioId = funcionarioId;
                cambios++;
            }
        }

        _db.AuditoriaEventos.Add(new AuditoriaEvento
        {
            Usuario = usuario,
            Modulo = "Carga mensual",
            Accion = "Códigos ábaco",
            Entidad = "CodigoAbaco",
            Detalle = $"Códigos guardados o cambiados: {cambios}."
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    // Códigos 10xx de ábaco suelen ser el "segundo número" 1000xx? del programa (1022 -> 1000222).
    private static bool EsSegundoNumero(string codigoAbaco, string numeroPrograma) =>
        codigoAbaco.Length == 4 && codigoAbaco.StartsWith("10", StringComparison.Ordinal)
        && int.TryParse(codigoAbaco[2..], out var resto)
        && numeroPrograma.StartsWith($"1000{resto}", StringComparison.Ordinal)
        && numeroPrograma.Length == $"1000{resto}".Length + 1;
}
