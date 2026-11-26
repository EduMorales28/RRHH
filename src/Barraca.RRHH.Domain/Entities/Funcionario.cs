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
    // Retención de monto fijo mensual. La planilla de ábaco la incluye dentro de "Adelantos".
    public decimal RetencionFija { get; set; }
    // Si cobra en el mes y no tiene horas, se le carga 1 hora en la obra SEDE del tipo (ej. Gastón De León).
    public bool HoraFijaSede { get; set; }
    public bool Activo { get; set; } = true;

    public ICollection<CuentaPagoFuncionario> CuentasPago { get; set; } = new List<CuentaPagoFuncionario>();
}
