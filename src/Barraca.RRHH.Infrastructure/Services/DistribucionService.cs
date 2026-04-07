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

            var costoTotalTipo = pagos.Where(x => x.TipoObra == tipoObra).Sum(x => x.TotalGenerado);
            var horasTotalesTipo = horasTipo.Sum(x => x.HorasEquivalentes);

            if (horasTotalesTipo <= 0 || costoTotalTipo <= 0)
                continue;

            var agrupadas = horasTipo
                .GroupBy(x => new
                {
                    x.ObraId,
                    NumeroObra = x.Obra!.NumeroObra,
                    NombreObra = x.Obra!.Nombre,
                    x.Categoria
                })
                .Select(g => new
                {
                    g.Key.ObraId,
                    g.Key.NumeroObra,
                    g.Key.NombreObra,
                    g.Key.Categoria,
                    HorasLinea = g.Sum(x => x.HorasEquivalentes)
                })
                .OrderBy(x => x.NumeroObra)
                .ThenBy(x => x.Categoria)
                .ToList();

            foreach (var item in agrupadas)
            {
                var porcentaje = item.HorasLinea / horasTotalesTipo;
                var monto = Math.Round(porcentaje * costoTotalTipo, 2, MidpointRounding.AwayFromZero);
                var valorHora = item.HorasLinea == 0 ? 0 : Math.Round(monto / item.HorasLinea, 2, MidpointRounding.AwayFromZero);
                var jornales = Math.Round(item.HorasLinea / 8.8m, 2, MidpointRounding.AwayFromZero);

                var dto = new DistribucionLineaDto
                {
                    TipoObra = tipoObra,
                    ObraId = item.ObraId,
                    NumeroObra = item.NumeroObra,
                    NombreObra = item.NombreObra,
                    Categoria = item.Categoria,
                    HorasLinea = item.HorasLinea,
                    HorasTotalesTipoObra = horasTotalesTipo,
                    CostoTotalTipoObra = costoTotalTipo,
                    PorcentajeParticipacion = porcentaje,
                    MontoLinea = monto,
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
}
