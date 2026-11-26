using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Domain.Entities;

public class PagoMensual
{
    public int Id { get; set; }
    public int PeriodoId { get; set; }
    public int FuncionarioId { get; set; }
    public string NombreFuncionarioExcel { get; set; } = string.Empty;
    public TipoObra TipoObra { get; set; }
    public string TipoObraOriginal { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public decimal Adelanto { get; set; }
    public decimal Liquido { get; set; }
    public decimal Retencion { get; set; }
    public decimal TotalGenerado { get; set; }
    // Nominal de la planilla de ábaco (0 si el pago no vino de ábaco). Se usa para el BPS de la Sede.
    public decimal Nominal { get; set; }
    public string TipoPago { get; set; } = string.Empty;
    public string Observacion { get; set; } = string.Empty;

    public Periodo? Periodo { get; set; }
    public Funcionario? Funcionario { get; set; }
}
