using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly BarracaDbContext _db;

    public DashboardService(BarracaDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardResumenDto> ObtenerResumenAsync(string? periodo = null)
    {
        periodo ??= await _db.Periodos
            .OrderByDescending(x => x.Codigo)
            .Select(x => x.Codigo)
            .FirstOrDefaultAsync() ?? string.Empty;

        var periodoId = await _db.Periodos
            .Where(x => x.Codigo == periodo)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync();

        if (periodoId is null)
        {
            return new DashboardResumenDto
            {
                Periodo = periodo ?? string.Empty,
                FuncionariosActivos = await _db.Funcionarios.CountAsync(x => x.Activo),
                ObrasActivas = await _db.Obras.CountAsync(x => x.Activa)
            };
        }

        if (_db.Database.IsSqlite())
        {
            var pagos = await _db.PagosMensuales
                .Where(x => x.PeriodoId == periodoId)
                .Select(x => new { x.Adelanto, x.Liquido, x.Retencion, x.TotalGenerado })
                .ToListAsync();

            return new DashboardResumenDto
            {
                Periodo = periodo!,
                FuncionariosActivos = await _db.Funcionarios.CountAsync(x => x.Activo),
                ObrasActivas = await _db.Obras.CountAsync(x => x.Activa),
                Adelantos = pagos.Sum(x => x.Adelanto),
                Liquidos = pagos.Sum(x => x.Liquido),
                Retenciones = pagos.Sum(x => x.Retencion),
                TotalGenerado = pagos.Sum(x => x.TotalGenerado),
                LineasDistribuidas = await _db.DistribucionesCosto.Where(x => x.PeriodoId == periodoId).CountAsync()
            };
        }

        return new DashboardResumenDto
        {
            Periodo = periodo!,
            FuncionariosActivos = await _db.Funcionarios.CountAsync(x => x.Activo),
            ObrasActivas = await _db.Obras.CountAsync(x => x.Activa),
            Adelantos = await _db.PagosMensuales.Where(x => x.PeriodoId == periodoId).SumAsync(x => x.Adelanto),
            Liquidos = await _db.PagosMensuales.Where(x => x.PeriodoId == periodoId).SumAsync(x => x.Liquido),
            Retenciones = await _db.PagosMensuales.Where(x => x.PeriodoId == periodoId).SumAsync(x => x.Retencion),
            TotalGenerado = await _db.PagosMensuales.Where(x => x.PeriodoId == periodoId).SumAsync(x => x.TotalGenerado),
            LineasDistribuidas = await _db.DistribucionesCosto.Where(x => x.PeriodoId == periodoId).CountAsync()
        };
    }
}
