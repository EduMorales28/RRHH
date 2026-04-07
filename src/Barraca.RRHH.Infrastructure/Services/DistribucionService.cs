using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Barraca.RRHH.Infrastructure.Services;

public class DistribucionService : IDistribucionService
{
    private readonly BarracaDbContext _db;
    private const decimal ToleranciaIntegridad = 0.000001m;

    public DistribucionService(BarracaDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DistribucionLineaDto>> CalcularDistribucionAsync(string periodo, string usuario = "sistema", bool persistir = true)
    {
        if (string.IsNullOrWhiteSpace(periodo))
            throw new ArgumentException("El periodo es requerido.", nameof(periodo));

        var periodoCodigo = periodo.Trim();
        var periodoEntity = await _db.Periodos.FirstOrDefaultAsync(x => x.Codigo == periodoCodigo);
        if (periodoEntity is null)
            throw new InvalidOperationException($"No existe el periodo '{periodoCodigo}'.");

        var horas = await _db.HorasMensuales
            .Include(x => x.Obra)
            .Where(x => x.PeriodoId == periodoEntity.Id)
            .ToListAsync();

        var obrasSedePorTipo = await ObtenerObrasSedePorTipoAsync();

        var pagos = await _db.PagosMensuales
            .Where(x => x.PeriodoId == periodoEntity.Id)
            .ToListAsync();

        var funcionarios = await _db.Funcionarios
            .Select(x => new { x.Id, x.NumeroFuncionario, x.Nombre })
            .ToListAsync();
        var funcionariosMap = funcionarios.ToDictionary(x => x.Id, x => x);

        // Base contable por funcionario y tipo de obra: evita mezclar montos entre tipos.
        var totalPorFuncionarioTipo = pagos
            .GroupBy(x => (x.FuncionarioId, x.TipoObra))
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.Adelanto + x.Liquido + x.Retencion));

        // Solo horas distribuibles: con obra asociada y horas positivas.
        var horasDistribuibles = horas
            .Where(x => x.Obra is not null && x.HorasEquivalentes > 0m)
            .ToList();

        ValidarSedesParaAdministrativos(horasDistribuibles, obrasSedePorTipo);

        var registrosDistribuibles = horasDistribuibles
            .Select(x => new RegistroDistribuible
            {
                Hora = x,
                ObraDestino = ObtenerObraDestinoParaDistribucion(x, obrasSedePorTipo)
            })
            .ToList();

        var horasTotalesPorFuncionarioTipo = registrosDistribuibles
            .GroupBy(x => (x.Hora.FuncionarioId, x.ObraDestino.TipoObra))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Hora.HorasEquivalentes));

        var funcionariosTipoConPagoSinHoras = totalPorFuncionarioTipo
            .Where(x => x.Value > 0m
                && (!horasTotalesPorFuncionarioTipo.TryGetValue(x.Key, out var horasFuncTipo) || horasFuncTipo <= 0m))
            .ToList();

        if (funcionariosTipoConPagoSinHoras.Count > 0)
        {
            var detalle = string.Join("; ", funcionariosTipoConPagoSinHoras.Select(x =>
            {
                if (funcionariosMap.TryGetValue(x.Key.FuncionarioId, out var f))
                    return $"{f.NumeroFuncionario} - {f.Nombre} ({x.Key.TipoObra}): total={x.Value:N2}, horas=0";

                return $"FuncionarioId={x.Key.FuncionarioId} ({x.Key.TipoObra}): total={x.Value:N2}, horas=0";
            }));

            throw new InvalidOperationException(
                "No se puede distribuir: hay funcionario/tipo con pagos y sin horas. " +
                "Corrige estos casos o asígnalos a un centro de costo especial. " +
                $"Detalle: {detalle}");
        }

        var resultado = new List<DistribucionLineaDto>();
        CorridaProceso? corrida = null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;

        if (persistir)
        {
            tx = await _db.Database.BeginTransactionAsync();

            corrida = new CorridaProceso
            {
                PeriodoId = periodoEntity.Id,
                TipoProceso = "Distribucion",
                CodigoCorrida = DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                Usuario = usuario
            };

            _db.CorridasProceso.Add(corrida);

            var distribucionesPrevias = _db.DistribucionesCosto.Where(x => x.PeriodoId == periodoEntity.Id);
            _db.DistribucionesCosto.RemoveRange(distribucionesPrevias);
        }

        var lineasBase = new List<LineaDistribucionBase>();
        var controlPorFuncionario = new List<ControlFuncionario>();

        foreach (var item in totalPorFuncionarioTipo
            .Where(x => x.Value > 0m)
            .OrderBy(x => x.Key.FuncionarioId)
            .ThenBy(x => x.Key.TipoObra))
        {
            var funcionarioId = item.Key.FuncionarioId;
            var tipoObra = item.Key.TipoObra;
            var totalFuncionarioTipo = item.Value;

            if (!horasTotalesPorFuncionarioTipo.TryGetValue((funcionarioId, tipoObra), out var horasFuncionarioTipo) || horasFuncionarioTipo <= 0m)
                continue;

            var registrosFuncionarioTipo = registrosDistribuibles
                .Where(x => x.Hora.FuncionarioId == funcionarioId && x.ObraDestino.TipoObra == tipoObra)
                .OrderBy(x => x.Hora.Id)
                .ToList();

            var valorHoraFuncionarioTipo = totalFuncionarioTipo / horasFuncionarioTipo;
            decimal sumaFuncionarioTipoDistribuida = 0m;

            for (var i = 0; i < registrosFuncionarioTipo.Count; i++)
            {
                var registro = registrosFuncionarioTipo[i];
                var reg = registro.Hora;
                var montoRegistro = reg.HorasEquivalentes * valorHoraFuncionarioTipo;

                if (i == registrosFuncionarioTipo.Count - 1)
                {
                    var ajuste = totalFuncionarioTipo - (sumaFuncionarioTipoDistribuida + montoRegistro);
                    montoRegistro += ajuste;
                }

                sumaFuncionarioTipoDistribuida += montoRegistro;

                var obra = registro.ObraDestino;
                lineasBase.Add(new LineaDistribucionBase
                {
                    TipoObra = obra.TipoObra,
                    ObraId = obra.Id,
                    NumeroObra = obra.NumeroObra,
                    NombreObra = obra.Nombre,
                    Categoria = NormalizarCategoria(reg.Categoria),
                    Horas = reg.HorasEquivalentes,
                    Monto = montoRegistro
                });
            }

            controlPorFuncionario.Add(new ControlFuncionario
            {
                FuncionarioId = funcionarioId,
                TipoObra = tipoObra,
                TotalFuncionario = totalFuncionarioTipo,
                TotalDistribuido = sumaFuncionarioTipoDistribuida
            });
        }

        var diferenciasFuncionario = controlPorFuncionario
            .Select(x => new
            {
                x.FuncionarioId,
                x.TipoObra,
                x.TotalFuncionario,
                x.TotalDistribuido,
                Diferencia = x.TotalFuncionario - x.TotalDistribuido
            })
            .Where(x => Math.Abs(x.Diferencia) > ToleranciaIntegridad)
            .ToList();

        if (diferenciasFuncionario.Count > 0)
        {
            var detalle = string.Join("; ", diferenciasFuncionario.Select(x =>
            {
                if (funcionariosMap.TryGetValue(x.FuncionarioId, out var f))
                    return $"{f.NumeroFuncionario} - {f.Nombre} ({x.TipoObra}): esperado={x.TotalFuncionario:N2}, distribuido={x.TotalDistribuido:N2}, dif={x.Diferencia:N2}";

                return $"FuncionarioId={x.FuncionarioId} ({x.TipoObra}): esperado={x.TotalFuncionario:N2}, distribuido={x.TotalDistribuido:N2}, dif={x.Diferencia:N2}";
            }));

            throw new InvalidOperationException("Error de integridad por funcionario en distribución. " + detalle);
        }

        var lineasConsolidadas = lineasBase
            .GroupBy(x => new { x.TipoObra, x.ObraId, x.NumeroObra, x.NombreObra, x.Categoria })
            .Select(g => new LineaConsolidada
            {
                TipoObra = g.Key.TipoObra,
                ObraId = g.Key.ObraId,
                NumeroObra = g.Key.NumeroObra,
                NombreObra = g.Key.NombreObra,
                Categoria = g.Key.Categoria,
                Horas = g.Sum(x => x.Horas),
                Monto = g.Sum(x => x.Monto)
            })
            .OrderBy(x => x.TipoObra)
            .ThenBy(x => x.NumeroObra)
            .ThenBy(x => x.Categoria, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var totalConsolidado = controlPorFuncionario.Sum(x => x.TotalFuncionario);
        var totalDistribucionRaw = lineasConsolidadas.Sum(x => x.Monto);

        var diferenciaRaw = totalConsolidado - totalDistribucionRaw;
        if (Math.Abs(diferenciaRaw) > ToleranciaIntegridad)
        {
            throw new InvalidOperationException(
            $"Error de integridad: total consolidado ({totalConsolidado:N6}) distinto a total distribuido crudo ({totalDistribucionRaw:N6}). " +
            $"Diferencia técnica={diferenciaRaw:G29}.");
        }

        var lineasFinales = lineasConsolidadas
            .Select(x => new LineaConsolidada
            {
                TipoObra = x.TipoObra,
                ObraId = x.ObraId,
                NumeroObra = x.NumeroObra,
                NombreObra = x.NombreObra,
                Categoria = x.Categoria,
                Horas = x.Horas,
                Monto = Math.Round(x.Monto, 2, MidpointRounding.AwayFromZero)
            })
            .ToList();

        var totalDistribucionFinal = lineasFinales.Sum(x => x.Monto);
        var diferenciaFinal = Math.Round(totalConsolidado - totalDistribucionFinal, 2, MidpointRounding.AwayFromZero);
        if (diferenciaFinal != 0m && lineasFinales.Count > 0)
            lineasFinales[^1].Monto += diferenciaFinal;

        totalDistribucionFinal = lineasFinales.Sum(x => x.Monto);
        if (totalDistribucionFinal != totalConsolidado)
        {
            var detalle = string.Join("; ", controlPorFuncionario.Select(x =>
            {
                var diff = x.TotalFuncionario - x.TotalDistribuido;
                if (funcionariosMap.TryGetValue(x.FuncionarioId, out var f))
                    return $"{f.NumeroFuncionario} - {f.Nombre} ({x.TipoObra}): dif={diff:N2}";

                return $"FuncionarioId={x.FuncionarioId} ({x.TipoObra}): dif={diff:N2}";
            }));

            throw new InvalidOperationException(
                "Error de integridad: SUMA_TOTAL_DISTRIBUIDA != SUMA_TOTAL_CONSOLIDADO. " +
                $"Consolidado={totalConsolidado:N2}, Distribuido={totalDistribucionFinal:N2}. " +
                $"Detalle por funcionario: {detalle}");
        }

        var resumenTipo = lineasFinales
            .GroupBy(x => x.TipoObra)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Horas = g.Sum(x => x.Horas),
                    Monto = g.Sum(x => x.Monto)
                });

        foreach (var linea in lineasFinales)
        {
            var resumen = resumenTipo[linea.TipoObra];
            var porcentaje = resumen.Horas <= 0m ? 0m : linea.Horas / resumen.Horas;
            var valorHora = linea.Horas <= 0m ? 0m : Math.Round(linea.Monto / linea.Horas, 2, MidpointRounding.AwayFromZero);
            var jornales = Math.Round(linea.Horas / 8.8m, 2, MidpointRounding.AwayFromZero);

            var dto = new DistribucionLineaDto
            {
                TipoObra = linea.TipoObra,
                ObraId = linea.ObraId,
                NumeroObra = linea.NumeroObra,
                NombreObra = linea.NombreObra,
                Categoria = linea.Categoria,
                HorasLinea = linea.Horas,
                HorasTotalesTipoObra = resumen.Horas,
                CostoTotalTipoObra = resumen.Monto,
                PorcentajeParticipacion = porcentaje,
                MontoLinea = linea.Monto,
                ValorHora = valorHora,
                Jornales = jornales
            };

            resultado.Add(dto);

            if (persistir && corrida is not null)
            {
                _db.DistribucionesCosto.Add(new DistribucionCosto
                {
                    PeriodoId = periodoEntity.Id,
                    CorridaProceso = corrida,
                    TipoObra = dto.TipoObra,
                    ObraId = dto.ObraId,
                    Categoria = dto.Categoria,
                    HorasLinea = dto.HorasLinea,
                    HorasTotalesTipoObra = dto.HorasTotalesTipoObra,
                    CostoTotalTipoObra = dto.CostoTotalTipoObra,
                    PorcentajeParticipacion = dto.PorcentajeParticipacion,
                    MontoLinea = dto.MontoLinea,
                    ValorHora = dto.ValorHora,
                    Jornales = dto.Jornales
                });
            }
        }

        if (persistir)
        {
            _db.AuditoriaEventos.Add(new AuditoriaEvento
            {
                Usuario = usuario,
                Modulo = "Distribucion",
                Accion = "Calcular Distribucion",
                Entidad = "Periodo",
                EntidadClave = periodoCodigo,
                Detalle = $"Líneas generadas: {resultado.Count}"
            });

            try
            {
                await _db.SaveChangesAsync();
                if (tx is not null)
                    await tx.CommitAsync();
            }
            catch
            {
                if (tx is not null)
                    await tx.RollbackAsync();
                throw;
            }
        }

        return resultado;
    }

    private sealed class LineaDistribucionBase
    {
        public TipoObra TipoObra { get; set; }
        public int ObraId { get; set; }
        public string NumeroObra { get; set; } = string.Empty;
        public string NombreObra { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public decimal Horas { get; set; }
        public decimal Monto { get; set; }
    }

    private sealed class LineaConsolidada
    {
        public TipoObra TipoObra { get; set; }
        public int ObraId { get; set; }
        public string NumeroObra { get; set; } = string.Empty;
        public string NombreObra { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public decimal Horas { get; set; }
        public decimal Monto { get; set; }
    }

    private sealed class ControlFuncionario
    {
        public int FuncionarioId { get; set; }
        public TipoObra TipoObra { get; set; }
        public decimal TotalFuncionario { get; set; }
        public decimal TotalDistribuido { get; set; }
    }

    private sealed class RegistroDistribuible
    {
        public HoraMensual Hora { get; set; } = default!;
        public Obra ObraDestino { get; set; } = default!;
    }

    private static string NormalizarCategoria(string? categoria)
    {
        if (string.IsNullOrWhiteSpace(categoria))
            return "Sin categoria";

        var tokens = categoria
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var compacta = string.Join(' ', tokens).ToLowerInvariant();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(compacta);
    }

    private static bool EsCategoriaAdministrativa(string? categoria)
    {
        if (string.IsNullOrWhiteSpace(categoria))
            return false;

        return categoria.Contains("administrativo", StringComparison.OrdinalIgnoreCase);
    }

    private Obra ObtenerObraDestinoParaDistribucion(HoraMensual reg, Dictionary<TipoObra, Obra> obrasSedePorTipo)
    {
        var obraBase = reg.Obra!;
        if (!EsCategoriaAdministrativa(reg.Categoria))
            return obraBase;

        if (obraBase.TipoObra is TipoObra.Construccion or TipoObra.IndustriaYComercio or TipoObra.NA
            && obrasSedePorTipo.TryGetValue(obraBase.TipoObra, out var obraSede))
        {
            return obraSede;
        }

        return obraBase;
    }

    private static void ValidarSedesParaAdministrativos(
        List<HoraMensual> horasDistribuibles,
        Dictionary<TipoObra, Obra> obrasSedePorTipo)
    {
        var tiposNecesarios = horasDistribuibles
            .Where(x => x.Obra is not null && EsCategoriaAdministrativa(x.Categoria))
            .Select(x => x.Obra!.TipoObra)
            .Where(t => t is TipoObra.Construccion or TipoObra.IndustriaYComercio or TipoObra.NA)
            .Distinct()
            .ToList();

        var faltantes = tiposNecesarios
            .Where(t => !obrasSedePorTipo.ContainsKey(t))
            .ToList();

        if (faltantes.Count == 0)
            return;

        var detalle = string.Join(", ", faltantes.Select(t => t.ToString()));
        throw new InvalidOperationException(
            "No se puede distribuir categorías Administrativas porque faltan obras SEDE por tipo. " +
            $"Tipos faltantes: {detalle}. " +
            "Debe existir al menos una obra cuyo nombre contenga 'SEDE' para cada tipo requerido.");
    }

    private async Task<Dictionary<TipoObra, Obra>> ObtenerObrasSedePorTipoAsync()
    {
        var obrasActivas = await _db.Obras
            .Where(x => x.Activa)
            .OrderBy(x => x.NumeroObra)
            .ToListAsync();

        return obrasActivas
            .Where(x => x.Nombre.Contains("sede", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.TipoObra)
            .ToDictionary(g => g.Key, g => g.First());
    }
}
