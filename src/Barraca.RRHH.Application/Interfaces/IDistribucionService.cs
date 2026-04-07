using Barraca.RRHH.Application.DTOs;

namespace Barraca.RRHH.Application.Interfaces;

public interface IDistribucionService
{
    Task<IReadOnlyList<DistribucionLineaDto>> CalcularDistribucionAsync(string periodo, string usuario = "sistema", bool persistir = true);
}
