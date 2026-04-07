using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Services;

public class PeriodoService : IPeriodoService
{
    private readonly BarracaDbContext _db;

    public PeriodoService(BarracaDbContext db)
    {
        _db = db;
    }

    public Task<List<Periodo>> ObtenerPeriodosAsync() =>
        _db.Periodos.OrderByDescending(x => x.Codigo).ToListAsync();

    public async Task<string> ObtenerPeriodoPredeterminadoAsync(string? preferido = null)
    {
        var p = (preferido ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(p) && await TieneMovimientosAsync(p))
            return p;

        var periodosConMovimientos = await _db.Periodos
            .Where(pe =>
                _db.HorasMensuales.Any(h => h.PeriodoId == pe.Id) ||
                _db.PagosMensuales.Any(pg => pg.PeriodoId == pe.Id) ||
                _db.DistribucionesCosto.Any(d => d.PeriodoId == pe.Id))
            .OrderByDescending(pe => pe.Codigo)
            .Select(pe => pe.Codigo)
            .ToListAsync();

        if (periodosConMovimientos.Count > 0)
            return periodosConMovimientos[0];

        if (!string.IsNullOrWhiteSpace(p))
            return p;

        var ultimoPeriodo = await _db.Periodos
            .OrderByDescending(x => x.Codigo)
            .Select(x => x.Codigo)
            .FirstOrDefaultAsync();

        return string.IsNullOrWhiteSpace(ultimoPeriodo)
            ? DateTime.Now.ToString("yyyy-MM")
            : ultimoPeriodo;
    }

    public async Task AbrirPeriodoAsync(string codigo, string usuario)
    {
        var periodo = await ObtenerOCrearAsync(codigo);
        var ahora = DateTime.UtcNow;

        var periodosAbiertos = await _db.Periodos
            .Where(x => x.Id != periodo.Id && (x.Estado == EstadoPeriodo.Abierto || x.Estado == EstadoPeriodo.Reabierto))
            .ToListAsync();

        foreach (var p in periodosAbiertos)
        {
            p.Estado = EstadoPeriodo.Cerrado;
            if (!p.FechaCierre.HasValue)
                p.FechaCierre = ahora;
        }

        periodo.Estado = EstadoPeriodo.Abierto;
        periodo.FechaCierre = null;
        var detalle = periodosAbiertos.Count == 0
            ? "Periodo abierto automaticamente como periodo activo"
            : $"Periodo abierto automaticamente como periodo activo. Se cerraron {periodosAbiertos.Count} periodo(s) previo(s).";

        _db.AuditoriaEventos.Add(new AuditoriaEvento { Usuario = usuario, Modulo = "Periodos", Accion = "Abrir", Entidad = "Periodo", EntidadClave = codigo, Detalle = detalle });
        await _db.SaveChangesAsync();
    }

    public async Task CerrarPeriodoAsync(string codigo, string usuario)
    {
        var periodo = await ObtenerOCrearAsync(codigo);
        periodo.Estado = EstadoPeriodo.Cerrado;
        periodo.FechaCierre = DateTime.UtcNow;
        _db.AuditoriaEventos.Add(new AuditoriaEvento { Usuario = usuario, Modulo = "Periodos", Accion = "Cerrar", Entidad = "Periodo", EntidadClave = codigo, Detalle = "Periodo cerrado" });
        await _db.SaveChangesAsync();
    }

    public async Task ReabrirPeriodoAsync(string codigo, string usuario)
    {
        var periodo = await ObtenerOCrearAsync(codigo);
        periodo.Estado = EstadoPeriodo.Reabierto;
        periodo.FechaCierre = null;
        _db.AuditoriaEventos.Add(new AuditoriaEvento { Usuario = usuario, Modulo = "Periodos", Accion = "Reabrir", Entidad = "Periodo", EntidadClave = codigo, Detalle = "Periodo reabierto" });
        await _db.SaveChangesAsync();
    }

    private async Task<Periodo> ObtenerOCrearAsync(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ArgumentException("El codigo de periodo es requerido.", nameof(codigo));

        var periodo = await _db.Periodos.FirstOrDefaultAsync(x => x.Codigo == codigo);
        if (periodo is not null) return periodo;

        periodo = new Periodo { Codigo = codigo.Trim(), Estado = EstadoPeriodo.Abierto };
        _db.Periodos.Add(periodo);

        try
        {
            await _db.SaveChangesAsync();
            return periodo;
        }
        catch (DbUpdateException)
        {
            _db.Entry(periodo).State = EntityState.Detached;
            var existente = await _db.Periodos.FirstOrDefaultAsync(x => x.Codigo == codigo.Trim());
            if (existente is not null)
                return existente;

            throw;
        }
    }

    private async Task<bool> TieneMovimientosAsync(string codigo)
    {
        var periodoId = await _db.Periodos
            .Where(x => x.Codigo == codigo)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync();

        if (periodoId is null)
            return false;

        return await _db.HorasMensuales.AnyAsync(x => x.PeriodoId == periodoId)
            || await _db.PagosMensuales.AnyAsync(x => x.PeriodoId == periodoId)
            || await _db.DistribucionesCosto.AnyAsync(x => x.PeriodoId == periodoId);
    }
}
