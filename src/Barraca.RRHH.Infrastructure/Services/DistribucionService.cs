using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Services;

public class DistribucionService : IDistribucionService
{
    private readonly BarracaDbContext _db;

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

        foreach (var tipoObra in Enum.GetValues<TipoObra>())
        {
            var horasTipo = horas.Where(x => x.Obra != null && x.Obra.TipoObra == tipoObra).ToList();
            if (!horasTipo.Any())
                continue;

            // Regla de negocio: el total del tipo de obra siempre sale de
            // Adelanto + Liquido + Retencion, no de un total importado en crudo.
            var costoTotalTipo = pagos
                .Where(x => x.TipoObra == tipoObra)
                .Sum(x => x.Adelanto + x.Liquido + x.Retencion);

            var horasTotalesTipo = horasTipo.Sum(x => x.HorasEquivalentes);

            if (horasTotalesTipo <= 0 || costoTotalTipo <= 0)
                continue;

            var obrasDelTipo = horasTipo
                .GroupBy(x => new
                {
                    x.ObraId,
                    NumeroObra = x.Obra!.NumeroObra,
                    NombreObra = x.Obra!.Nombre
                })
                .Select(g => new
                {
                    g.Key.ObraId,
                    g.Key.NumeroObra,
                    g.Key.NombreObra,
                    HorasObra = g.Sum(x => x.HorasEquivalentes)
                })
                .OrderBy(x => x.NumeroObra)
                .ToList();

            var montosPorObra = DistribuirMontoConCierre(
                obrasDelTipo.Select(x => (x.ObraId, x.HorasObra)).ToList(),
                costoTotalTipo);

            foreach (var obra in obrasDelTipo)
            {
                if (!montosPorObra.TryGetValue(obra.ObraId, out var montoObra) || montoObra <= 0)
                    continue;

                var categoriasObra = horasTipo
                    .Where(x => x.ObraId == obra.ObraId)
                    .GroupBy(x => x.Categoria)
                    .Select(g => new
                    {
                        Categoria = g.Key,
                        HorasCategoria = g.Sum(x => x.HorasEquivalentes)
                    })
                    .OrderBy(x => x.Categoria)
                    .ToList();

                var montosPorCategoria = DistribuirMontoConCierre(
                    categoriasObra.Select(x => (x.Categoria, x.HorasCategoria)).ToList(),
                    montoObra);

                foreach (var cat in categoriasObra)
                {
                    if (!montosPorCategoria.TryGetValue(cat.Categoria, out var montoLinea) || cat.HorasCategoria <= 0)
                        continue;

                    var porcentaje = cat.HorasCategoria / horasTotalesTipo;
                    var valorHora = Math.Round(montoLinea / cat.HorasCategoria, 2, MidpointRounding.AwayFromZero);
                    var jornales = Math.Round(cat.HorasCategoria / 8.8m, 2, MidpointRounding.AwayFromZero);

                    var dto = new DistribucionLineaDto
                    {
                        TipoObra = tipoObra,
                        ObraId = obra.ObraId,
                        NumeroObra = obra.NumeroObra,
                        NombreObra = obra.NombreObra,
                        Categoria = cat.Categoria,
                        HorasLinea = cat.HorasCategoria,
                        HorasTotalesTipoObra = horasTotalesTipo,
                        CostoTotalTipoObra = costoTotalTipo,
                        PorcentajeParticipacion = porcentaje,
                        MontoLinea = montoLinea,
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

    private static Dictionary<TKey, decimal> DistribuirMontoConCierre<TKey>(
        IReadOnlyList<(TKey Key, decimal Horas)> items,
        decimal montoTotal)
        where TKey : notnull
    {
        var resultado = new Dictionary<TKey, decimal>();
        if (items.Count == 0 || montoTotal <= 0)
            return resultado;

        var horasTotales = items.Sum(x => x.Horas);
        if (horasTotales <= 0)
            return resultado;

        var calculados = items
            .Select(x => new
            {
                x.Key,
                x.Horas,
                Monto = Math.Round((x.Horas / horasTotales) * montoTotal, 2, MidpointRounding.AwayFromZero)
            })
            .ToList();

        var suma = calculados.Sum(x => x.Monto);
        var diferencia = Math.Round(montoTotal - suma, 2, MidpointRounding.AwayFromZero);

        if (diferencia != 0m)
        {
            var ancla = calculados
                .OrderByDescending(x => x.Horas)
                .First();

            calculados.Remove(ancla);
            calculados.Add(new
            {
                ancla.Key,
                ancla.Horas,
                Monto = ancla.Monto + diferencia
            });
        }

        foreach (var item in calculados)
            resultado[item.Key] = item.Monto;

        return resultado;
    }
}
