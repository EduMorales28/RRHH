using Barraca.RRHH.Application.DTOs;

namespace Barraca.RRHH.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardResumenDto> ObtenerResumenAsync(string? periodo = null);
}
