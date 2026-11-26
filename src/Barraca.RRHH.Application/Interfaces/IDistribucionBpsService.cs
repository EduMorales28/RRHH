using Barraca.RRHH.Application.DTOs;

namespace Barraca.RRHH.Application.Interfaces;

public interface IDistribucionBpsService
{
    Task<BpsMesDto> ObtenerAsync(string periodo);
    Task<string> GuardarAsync(string periodo, decimal? tasaSede, IReadOnlyList<FacturaBpsDto> facturas, string usuario);
    Task<RepartoBpsResultadoDto> CalcularAsync(string periodo);
    Task<string> GenerarPlanillaSpAsync(string periodo, string carpetaDestino, string usuario);
}
