namespace Barraca.RRHH.Domain.Entities;

public class Funcionario
{
    public int Id { get; set; }
    public string NumeroFuncionario { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Cedula { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public bool TieneRetencion { get; set; }
    public string ResponsableRetencion { get; set; } = string.Empty;
    public string BancoRetencion { get; set; } = string.Empty;
    public string CuentaRetencion { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;

    public ICollection<CuentaPagoFuncionario> CuentasPago { get; set; } = new List<CuentaPagoFuncionario>();
}
