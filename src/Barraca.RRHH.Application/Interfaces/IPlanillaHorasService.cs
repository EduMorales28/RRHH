using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Application.Interfaces;

public interface IPlanillaHorasService
{
    // Lee el archivo de horas del mes (hojas por tipo de obra: CONSTRUCCION, IYC, N-A).
    PlanillaHorasDto Leer(string ruta);

    // Sugiere funcionario y obra del programa para cada código y obra del archivo.
    // Ante varios números de una misma persona, prefiere el que tiene pagos de ese tipo en el período.
    Task<EmparejamientoHorasDto> EmparejarAsync(PlanillaHorasDto planilla, string periodo);

    // Recuerda las equivalencias confirmadas para los meses siguientes.
    Task GuardarEquivalenciasAsync(
        IEnumerable<(TipoObra TipoObra, string Codigo, string NumeroFuncionario)> personas,
        IEnumerable<(TipoObra TipoObra, string Nombre, string NumeroObra)> obras,
        string usuario);
}
