namespace Barraca.RRHH.Application.DTOs;

public class ConsistenciaFuncionarioErrorDto
{
    public string Tipo { get; set; } = string.Empty;
    public int FuncionarioId { get; set; }
    public string NumeroFuncionario { get; set; } = string.Empty;
    public string NombreFuncionario { get; set; } = string.Empty;
    public int RegistrosHoras { get; set; }
    public int RegistrosPagos { get; set; }
    public decimal TotalHoras { get; set; }
    public decimal TotalPagos { get; set; }
    public string Mensaje { get; set; } = string.Empty;
}
