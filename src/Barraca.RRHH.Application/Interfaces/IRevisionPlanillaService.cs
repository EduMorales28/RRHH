using Barraca.RRHH.Application.DTOs;

namespace Barraca.RRHH.Application.Interfaces;

// Revisa una planilla antes de importarla, sin modificar la base.
public interface IRevisionPlanillaService
{
    Task<IReadOnlyList<ProblemaPlanillaDto>> RevisarFuncionariosAsync(string filePath);
    Task<IReadOnlyList<ProblemaPlanillaDto>> RevisarHorasAsync(string filePath, string periodo);
    Task<IReadOnlyList<ProblemaPlanillaDto>> RevisarPagosAsync(string filePath, string periodo);
}
