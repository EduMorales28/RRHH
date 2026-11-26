using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Domain.Entities;

// Equivalencias confirmadas para el archivo de horas ("DATOS SUELDO"), que usa sus propios códigos y nombres.

// Código de funcionario del archivo de horas (ej. "032", "32-2") -> funcionario del programa.
public class CodigoHoras
{
    public int Id { get; set; }
    public TipoObra TipoObra { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int FuncionarioId { get; set; }

    public Funcionario? Funcionario { get; set; }
}

// Nombre de obra del archivo de horas (ej. "Wiebe") -> obra del programa.
public class ObraHoras
{
    public int Id { get; set; }
    public TipoObra TipoObra { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int ObraId { get; set; }

    public Obra? Obra { get; set; }
}
