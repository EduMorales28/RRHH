namespace Barraca.RRHH.Domain.Entities;

public class HoraMensual
{
    public int Id { get; set; }
    public int PeriodoId { get; set; }
    public int FuncionarioId { get; set; }
    public int ObraId { get; set; }
    public int RegistroOrigen { get; set; }
    public string NombreFuncionarioExcel { get; set; } = string.Empty;
    public string NombreObraExcel { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public decimal HorasComunes { get; set; }
    public decimal HorasExtras { get; set; }
    // Horas de lluvia: se pagan y cuentan como horas comunes para repartir el costo entre obras.
    public decimal HorasLluvia { get; set; }
    public decimal HorasEquivalentes { get; set; }

    public static decimal CalcularEquivalentes(decimal comunes, decimal extras, decimal lluvia) =>
        comunes + (extras * 2m) + lluvia;

    public Periodo? Periodo { get; set; }
    public Funcionario? Funcionario { get; set; }
    public Obra? Obra { get; set; }
}
