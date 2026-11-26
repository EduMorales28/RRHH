using Barraca.RRHH.Application.DTOs;

namespace Barraca.RRHH.Application.Interfaces;

// Carga y edición de maestros y movimientos del mes desde el propio programa.
public interface ICargaMensualService
{
    Task<List<FuncionarioEdicionDto>> ObtenerFuncionariosAsync();
    Task<string> GuardarFuncionariosAsync(IReadOnlyList<FuncionarioEdicionDto> modificados, string usuario);

    Task<List<ObraEdicionDto>> ObtenerObrasAsync();
    Task<string> GuardarObrasAsync(IReadOnlyList<ObraEdicionDto> modificadas, string usuario);

    Task<List<PagoEdicionDto>> ObtenerPagosAsync(string periodo);
    // Reemplaza los pagos del período por las filas recibidas (actualiza, agrega y borra).
    Task<string> GuardarPagosAsync(string periodo, IReadOnlyList<PagoEdicionDto> filas, string usuario);

    Task<List<HoraEdicionDto>> ObtenerHorasAsync(string periodo);
    // Reemplaza las horas del período por las filas recibidas (actualiza, agrega y borra).
    Task<string> GuardarHorasAsync(string periodo, IReadOnlyList<HoraEdicionDto> filas, string usuario);

    Task<PeriodoInfoDto?> ObtenerPeriodoAsync(string periodo);
    // Crea un período abierto; si se indica origen, copia sus filas de pagos y horas con montos y horas en 0.
    Task<string> CrearPeriodoAsync(string periodo, string? copiarDe, string usuario);
}
