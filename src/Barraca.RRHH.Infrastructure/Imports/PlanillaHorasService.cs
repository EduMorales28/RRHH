using System.Text.RegularExpressions;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Data;
using Barraca.RRHH.Infrastructure.Helpers;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Imports;

// Archivo de horas del mes ("DATOS SUELDO <MES>.xlsm"). Columnas: A Funcionario "(Codigo:032) Nombre(Categoría)",
// E Obra "Nombre [BPS:...]", F Tipo de obra, G Hs Comunes, I Hs Extra, J Hs Lluvia, M Tck Alimentacion (cantidad de tickets).
// Hay una hoja por tipo (CONSTRUCCION, IYC, N-A) y BASE con todo junto: se usa BASE solo si faltan las otras.
public class PlanillaHorasService : IPlanillaHorasService
{
    private static readonly HashSet<string> PalabrasGenericasObra = ["IYC", "OBRA", "OBRAS", "BPS"];

    private readonly BarracaDbContext _db;

    public PlanillaHorasService(BarracaDbContext db)
    {
        _db = db;
    }

    // ---------------------------------------------------------------- Lectura

    public PlanillaHorasDto Leer(string ruta)
    {
        var planilla = new PlanillaHorasDto { Archivo = Path.GetFileName(ruta) };
        using var stream = ExcelImportService.AbrirArchivoExcelParaLectura(ruta);
        using var libro = new XLWorkbook(stream);

        var hojas = libro.Worksheets.Where(h => !h.Name.Equals("BASE", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!hojas.Any(TieneFilasDeHoras) && libro.TryGetWorksheet("BASE", out var hojaBase))
            hojas = [hojaBase];

        foreach (var hoja in hojas)
        {
            foreach (var fila in hoja.RowsUsed())
            {
                var funcionario = Regex.Match(fila.Cell(1).GetString().Trim(), @"^\(Codigo:\s*([^)]+)\)\s*(.+)$");
                var obra = fila.Cell(5).GetString().Trim();
                if (!funcionario.Success || obra.Length == 0 || obra is "Obra" or "Obras" or "GLOBAL"
                    || obra.StartsWith("Total", StringComparison.OrdinalIgnoreCase))
                    continue;

                var comunes = ExcelImportService.LeerDecimal(fila.Cell(7));
                var extras = ExcelImportService.LeerDecimal(fila.Cell(9));
                var lluvia = ExcelImportService.LeerDecimal(fila.Cell(10));
                var tickets = ExcelImportService.LeerDecimal(fila.Cell(13));
                if (comunes == 0 && extras == 0 && lluvia == 0 && tickets == 0)
                {
                    planilla.FilasSinHoras++;
                    continue;
                }

                var tipoTexto = fila.Cell(6).GetString().Trim();
                if (!TipoObraParser.EsReconocido(tipoTexto))
                    throw new InvalidOperationException($"Hoja {hoja.Name}, fila {fila.RowNumber()}: tipo de obra '{tipoTexto}' no reconocido.");

                var tipo = TipoObraParser.Parse(tipoTexto);
                var codigo = funcionario.Groups[1].Value.Trim();
                var nombre = funcionario.Groups[2].Value.Trim();

                if (tickets != 0)
                    planilla.Tickets.Add(new TicketsHorasDto { TipoObra = tipo, Codigo = codigo, Nombre = nombre, Cantidad = tickets });

                if (comunes == 0 && extras == 0 && lluvia == 0)
                {
                    planilla.FilasSinHoras++;
                    continue;
                }

                planilla.Filas.Add(new FilaHorasDto
                {
                    TipoObra = tipo,
                    Codigo = codigo,
                    Nombre = nombre,
                    Obra = Regex.Replace(obra, @"\s*\[BPS:[^\]]*\]", string.Empty).Trim(),
                    Comunes = comunes,
                    Extras = extras,
                    Lluvia = lluvia
                });
            }
        }

        if (planilla.Filas.Count == 0)
            throw new InvalidOperationException($"'{planilla.Archivo}' no tiene horas reconocibles. ¿Es el archivo DATOS SUELDO del mes?");

        // Varias obras de una misma persona: se suman los tickets por persona y tipo.
        planilla.Tickets = planilla.Tickets
            .GroupBy(t => (t.TipoObra, t.Codigo, t.Nombre))
            .Select(g => new TicketsHorasDto { TipoObra = g.Key.TipoObra, Codigo = g.Key.Codigo, Nombre = g.Key.Nombre, Cantidad = g.Sum(t => t.Cantidad) })
            .ToList();

        return planilla;
    }

    private static bool TieneFilasDeHoras(IXLWorksheet hoja) =>
        hoja.RowsUsed().Take(5).Any(f => f.Cell(1).GetString().TrimStart().StartsWith("(Codigo:", StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- Emparejamiento

    public async Task<EmparejamientoHorasDto> EmparejarAsync(PlanillaHorasDto planilla, string periodo)
    {
        var funcionarios = await _db.Funcionarios.AsNoTracking().ToListAsync();
        var porId = funcionarios.ToDictionary(f => f.Id);
        var frecuencia = NombresHelper.Frecuencias(funcionarios.Select(f => f.Nombre));
        var codigosRecordados = await _db.CodigosHoras.AsNoTracking().ToListAsync();
        var obrasRecordadas = await _db.ObrasHoras.AsNoTracking().ToListAsync();
        var obras = await _db.Obras.AsNoTracking().ToListAsync();

        var codigo = (periodo ?? string.Empty).Trim();
        var conPago = (await _db.PagosMensuales.AsNoTracking()
                .Where(p => p.Periodo!.Codigo == codigo)
                .Select(p => new { p.FuncionarioId, p.TipoObra, p.Adelanto, p.Liquido, p.Retencion })
                .ToListAsync())
            .Where(p => p.Adelanto + p.Liquido + p.Retencion > 0)
            .Select(p => (p.FuncionarioId, p.TipoObra))
            .ToHashSet();

        // Obras usadas en el mes anterior: desempatan entre obras de nombre parecido.
        var usadasAntes = (await _db.HorasMensuales.AsNoTracking()
                .Select(h => new { h.ObraId, h.Periodo!.Codigo })
                .ToListAsync())
            .Where(h => string.CompareOrdinal(h.Codigo, codigo) < 0)
            .GroupBy(h => h.ObraId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Codigo));

        var resultado = new EmparejamientoHorasDto();

        // Personas con horas o con tickets (alguien puede tener solo tickets).
        var personas = planilla.Filas.Select(f => (f.TipoObra, f.Codigo, f.Nombre))
            .Concat(planilla.Tickets.Select(t => (t.TipoObra, t.Codigo, t.Nombre)))
            .Distinct();
        foreach (var persona in personas)
            resultado.Personas.Add(EmparejarPersona(persona.TipoObra, persona.Codigo, persona.Nombre, funcionarios, porId, frecuencia, codigosRecordados, conPago));

        foreach (var obra in planilla.Filas.GroupBy(f => (f.TipoObra, f.Obra)).Select(g => g.Key))
            resultado.Obras.Add(EmparejarObra(obra.TipoObra, obra.Obra, obras, obrasRecordadas, usadasAntes));

        return resultado;
    }

    private static EmparejamientoPersonaHorasDto EmparejarPersona(
        TipoObra tipo, string codigo, string nombre, List<Funcionario> funcionarios, Dictionary<int, Funcionario> porId,
        Dictionary<string, int> frecuencia, List<CodigoHoras> recordados, HashSet<(int, TipoObra)> conPago)
    {
        var item = new EmparejamientoPersonaHorasDto { TipoObra = tipo, Codigo = codigo, Nombre = nombre };

        var recordado = recordados.FirstOrDefault(r => r.TipoObra == tipo && r.Codigo == codigo);
        if (recordado is not null && porId.TryGetValue(recordado.FuncionarioId, out var f0))
        {
            item.NumeroFuncionario = f0.NumeroFuncionario;
            item.Estado = EstadosHoras.Recordado;
            return item;
        }

        var tokens = NombresHelper.Tokens(nombre);
        var categoria = NombresHelper.Categoria(nombre);
        var numeroCodigo = int.TryParse(codigo.Split('-')[0], out var n) ? n.ToString() : codigo;

        var candidatos = funcionarios
            .Where(f =>
            {
                var t = NombresHelper.Tokens(f.Nombre);
                var puntaje = NombresHelper.Coincidencias(tokens, t);
                return puntaje >= Math.Min(2, Math.Max(1, t.Count))
                    || NombresHelper.CoincideTokenUnico(tokens, t, frecuencia)
                    || (f.NumeroFuncionario == numeroCodigo && puntaje >= 1);
            })
            .ToList();

        if (candidatos.Count == 0)
        {
            item.Estado = EstadosHoras.NoExiste;
            item.RequiereRevision = true;
            return item;
        }

        // Entre los números de la persona: primero los que cobran de este tipo en el mes; después la misma categoría y el mismo número.
        var ordenados = candidatos
            .OrderByDescending(f => conPago.Contains((f.Id, tipo)))
            .ThenByDescending(f => MismaCategoria(categoria, f))
            .ThenByDescending(f => f.NumeroFuncionario == numeroCodigo)
            .ToList();
        var elegido = ordenados[0];
        item.NumeroFuncionario = elegido.NumeroFuncionario;

        var cobran = candidatos.Count(f => conPago.Contains((f.Id, tipo)));
        if (cobran == 1 && conPago.Contains((elegido.Id, tipo)))
        {
            item.Estado = EstadosHoras.ConPago;
        }
        else if (cobran == 0)
        {
            item.Estado = EstadosHoras.SinPago;
            item.RequiereRevision = true;
        }
        else if (ordenados.Count(f => conPago.Contains((f.Id, tipo)) && MismaCategoria(categoria, f)) == 1 && MismaCategoria(categoria, elegido))
        {
            item.Estado = EstadosHoras.MismoNombre;
        }
        else
        {
            item.Estado = EstadosHoras.VariosNumeros;
            item.RequiereRevision = true;
        }

        return item;
    }

    // La categoría del archivo ("XI-Maquinista") coincide con la del nombre o la categoría del funcionario.
    private static bool MismaCategoria(string categoriaArchivo, Funcionario f)
    {
        if (categoriaArchivo.Length == 0)
            return false;
        var delNombre = NombresHelper.Categoria(f.Nombre);
        var deFicha = NombresHelper.Normalizar(f.Categoria);
        return categoriaArchivo == delNombre || categoriaArchivo == deFicha
            || (delNombre.Length > 0 && categoriaArchivo.Contains(delNombre)) || (deFicha.Length > 0 && categoriaArchivo.Contains(deFicha));
    }

    private static EmparejamientoObraHorasDto EmparejarObra(
        TipoObra tipo, string nombre, List<Obra> obras, List<ObraHoras> recordadas, Dictionary<int, string> usadasAntes)
    {
        var item = new EmparejamientoObraHorasDto { TipoObra = tipo, Nombre = nombre };

        var recordada = recordadas.FirstOrDefault(r => r.TipoObra == tipo && string.Equals(r.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
        var obraRecordada = recordada is null ? null : obras.FirstOrDefault(o => o.Id == recordada.ObraId);
        if (obraRecordada is not null)
        {
            item.NumeroObra = obraRecordada.NumeroObra;
            item.Estado = EstadosHoras.ObraRecordada;
            return item;
        }

        var tokens = TokensObra(nombre);
        var candidatas = obras
            .Where(o => o.TipoObra == tipo)
            .Select(o => (Obra: o, Puntaje: NombresHelper.Coincidencias(tokens, TokensObra(o.Nombre))))
            .Where(c => c.Puntaje >= 1)
            .OrderByDescending(c => c.Puntaje)
            .ThenByDescending(c => c.Obra.Activa)
            .ThenByDescending(c => usadasAntes.TryGetValue(c.Obra.Id, out var p) ? p : string.Empty)
            .ToList();

        if (candidatas.Count == 0)
        {
            item.CrearNueva = true;
            item.Estado = EstadosHoras.ObraNueva;
            item.RequiereRevision = true;
            return item;
        }

        item.NumeroObra = candidatas[0].Obra.NumeroObra;
        var empatadas = candidatas.Count(c => c.Puntaje == candidatas[0].Puntaje && c.Obra.Activa);
        if (empatadas > 1 || !candidatas[0].Obra.Activa)
        {
            item.Estado = EstadosHoras.ObraDudosa;
            item.RequiereRevision = true;
        }
        else
        {
            item.Estado = EstadosHoras.ObraPorNombre;
        }

        return item;
    }

    private static List<string> TokensObra(string nombre) =>
        NombresHelper.Normalizar(nombre).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && !t.All(char.IsDigit) && !PalabrasGenericasObra.Contains(t))
            .ToList();

    // ---------------------------------------------------------------- Equivalencias recordadas

    public async Task GuardarEquivalenciasAsync(
        IEnumerable<(TipoObra TipoObra, string Codigo, string NumeroFuncionario)> personas,
        IEnumerable<(TipoObra TipoObra, string Nombre, string NumeroObra)> obras,
        string usuario)
    {
        var funcionarios = (await _db.Funcionarios.ToListAsync())
            .GroupBy(f => f.NumeroFuncionario.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var obrasPorNumero = (await _db.Obras.ToListAsync())
            .GroupBy(o => o.NumeroObra.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var codigos = await _db.CodigosHoras.ToListAsync();
        var nombresObra = await _db.ObrasHoras.ToListAsync();

        foreach (var (tipo, codigo, numero) in personas)
        {
            if (!funcionarios.TryGetValue(numero.Trim(), out var funcionarioId))
                throw new InvalidOperationException($"No existe el funcionario {numero} (código {codigo} del archivo de horas).");

            var registro = codigos.FirstOrDefault(c => c.TipoObra == tipo && c.Codigo == codigo);
            if (registro is null)
            {
                registro = new CodigoHoras { TipoObra = tipo, Codigo = codigo };
                _db.CodigosHoras.Add(registro);
                codigos.Add(registro);
            }
            registro.FuncionarioId = funcionarioId;
        }

        foreach (var (tipo, nombre, numero) in obras)
        {
            if (!obrasPorNumero.TryGetValue(numero.Trim(), out var obraId))
                throw new InvalidOperationException($"No existe la obra {numero} (obra '{nombre}' del archivo de horas).");

            var registro = nombresObra.FirstOrDefault(o => o.TipoObra == tipo && string.Equals(o.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
            if (registro is null)
            {
                registro = new ObraHoras { TipoObra = tipo, Nombre = nombre };
                _db.ObrasHoras.Add(registro);
                nombresObra.Add(registro);
            }
            registro.ObraId = obraId;
        }

        _db.AuditoriaEventos.Add(new AuditoriaEvento
        {
            Usuario = usuario,
            Modulo = "Carga mensual",
            Accion = "Equivalencias horas",
            Entidad = "CodigoHoras",
            Detalle = "Códigos de funcionario y obras del archivo de horas confirmados."
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
}
