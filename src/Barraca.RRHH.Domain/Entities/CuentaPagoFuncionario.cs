namespace Barraca.RRHH.Domain.Entities;

public class CuentaPagoFuncionario
{
    public int Id { get; set; }
    public int FuncionarioId { get; set; }
    public string TipoPago { get; set; } = string.Empty;
    public string Banco { get; set; } = string.Empty;
    public string CuentaNueva { get; set; } = string.Empty;
    public string CuentaVieja { get; set; } = string.Empty;
    public bool Activa { get; set; } = true;

    public Funcionario? Funcionario { get; set; }
}
