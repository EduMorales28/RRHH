using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Application.Interfaces;

public interface IPlanillaAbacoService
{
    // Lee el PDF "Planilla de Sueldos a Pagar" de ábaco.
    PlanillaAbacoDto Leer(string rutaPdf);

    // Sugiere el funcionario del programa para cada fila (por código recordado, código y nombre, o nombre).
    Task<List<EmparejamientoAbacoDto>> EmparejarAsync(IEnumerable<PlanillaAbacoDto> planillas);

    // Recuerda qué funcionario corresponde a cada código de ábaco, para los meses siguientes.
    Task GuardarCodigosAsync(IEnumerable<(TipoObra TipoObra, string Codigo, string NumeroFuncionario)> codigos, string usuario);
}
