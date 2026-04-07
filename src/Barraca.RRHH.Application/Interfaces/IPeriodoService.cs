using Barraca.RRHH.Domain.Entities;

namespace Barraca.RRHH.Application.Interfaces;

public interface IPeriodoService
{
    Task<List<Periodo>> ObtenerPeriodosAsync();
    Task<string> ObtenerPeriodoPredeterminadoAsync(string? preferido = null);
    Task AbrirPeriodoAsync(string codigo, string usuario);
    Task CerrarPeriodoAsync(string codigo, string usuario);
    Task ReabrirPeriodoAsync(string codigo, string usuario);
}
