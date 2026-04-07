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

        var pagos = await _db.PagosMensuales
            .Where(x => x.PeriodoId == periodoEntity.Id)
            .ToListAsync();

        var funcionarios = await _db.Funcionarios
            .Select(x => new { x.Id, x.NumeroFuncionario, x.Nombre })
            .ToListAsync();
        var funcionariosMap = funcionarios.ToDictionary(x => x.Id, x => x);

        // Base contable por funcionario: siempre adelanto + liquido + retencion.
        var totalPorFuncionario = pagos
            .GroupBy(x => x.FuncionarioId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.Adelanto + x.Liquido + x.Retencion));

        // Solo horas distribuibles: con obra asociada y horas positivas.
        var horasDistribuibles = horas
            .Where(x => x.Obra is not null && x.HorasEquivalentes > 0m)
            .ToList();

        var horasTotalesPorFuncionario = horasDistribuibles
            .GroupBy(x => x.FuncionarioId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.HorasEquivalentes));

        var funcionariosConPagoSinHoras = totalPorFuncionario
            .Where(x => x.Value > 0m && (!horasTotalesPorFuncionario.TryGetValue(x.Key, out var horasFunc) || horasFunc <= 0m))
            .ToList();

        if (funcionariosConPagoSinHoras.Count > 0)
        {
            var detalle = string.Join("; ", funcionariosConPagoSinHoras.Select(x =>
            {
                if (funcionariosMap.TryGetValue(x.Key, out var f))
                    return $"{f.NumeroFuncionario} - {f.Nombre}: total={x.Value:N2}, horas=0";

                return $"FuncionarioId={x.Key}: total={x.Value:N2}, horas=0";
            }));

            throw new InvalidOperationException(
                "No se puede distribuir: hay funcionarios con pagos y sin horas. " +
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

        foreach (var item in totalPorFuncionario.Where(x => x.Value > 0m).OrderBy(x => x.Key))
        {
            var funcionarioId = item.Key;
            var totalFuncionario = item.Value;

            if (!horasTotalesPorFuncionario.TryGetValue(funcionarioId, out var horasFuncionario) || horasFuncionario <= 0m)
                continue;

            var registrosFuncionario = horasDistribuibles
                .Where(x => x.FuncionarioId == funcionarioId)
                .OrderBy(x => x.Id)
                .ToList();

            var valorHoraFuncionario = totalFuncionario / horasFuncionario;
            decimal sumaFuncionarioDistribuida = 0m;

            for (var i = 0; i < registrosFuncionario.Count; i++)
            {
                var reg = registrosFuncionario[i];
                var montoRegistro = reg.HorasEquivalentes * valorHoraFuncionario;

                if (i == registrosFuncionario.Count - 1)
                {
                    var ajuste = totalFuncionario - (sumaFuncionarioDistribuida + montoRegistro);
                    montoRegistro += ajuste;
                }

                sumaFuncionarioDistribuida += montoRegistro;

                var obra = reg.Obra!;
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
                TotalFuncionario = totalFuncionario,
                TotalDistribuido = sumaFuncionarioDistribuida
            });
        }

        var diferenciasFuncionario = controlPorFuncionario
            .Select(x => new
            {
                x.FuncionarioId,
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
                    return $"{f.NumeroFuncionario} - {f.Nombre}: esperado={x.TotalFuncionario:N2}, distribuido={x.TotalDistribuido:N2}, dif={x.Diferencia:N2}";

                return $"FuncionarioId={x.FuncionarioId}: esperado={x.TotalFuncionario:N2}, distribuido={x.TotalDistribuido:N2}, dif={x.Diferencia:N2}";
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
                    return $"{f.NumeroFuncionario} - {f.Nombre}: dif={diff:N2}";

                return $"FuncionarioId={x.FuncionarioId}: dif={diff:N2}";
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
        public decimal TotalFuncionario { get; set; }
        public decimal TotalDistribuido { get; set; }
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
}
