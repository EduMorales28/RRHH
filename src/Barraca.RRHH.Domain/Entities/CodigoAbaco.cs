using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Domain.Entities;

// Código de un empleado en la planilla de ábaco de una empresa (Construcción o Industria y Comercio).
// Los códigos son por empresa y no siempre coinciden con el número de funcionario del programa.
public class CodigoAbaco
{
    public int Id { get; set; }
    public TipoObra TipoObra { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int FuncionarioId { get; set; }

    public Funcionario? Funcionario { get; set; }
}
