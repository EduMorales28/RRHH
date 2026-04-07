using Barraca.RRHH.Application.DTOs;

namespace Barraca.RRHH.Application.Interfaces;

public interface IConsistenciaService
{
    Task<IReadOnlyList<ConsistenciaFuncionarioErrorDto>> ValidarConsistenciaFuncionarioAsync(string periodo);
    Task<IReadOnlyList<ConsistenciaDetalleRegistroDto>> ObtenerDetalleErrorAsync(string periodo, string tipo, int funcionarioId);
    Task CorregirConsistenciaFuncionarioAsync(string periodo, string tipo, int funcionarioIdOrigen, string numeroFuncionarioDestino, string usuario);
}
