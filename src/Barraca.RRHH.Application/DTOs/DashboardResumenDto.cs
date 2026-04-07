namespace Barraca.RRHH.Application.DTOs;

public class DashboardResumenDto
{
    public string Periodo { get; set; } = string.Empty;
    public int FuncionariosActivos { get; set; }
    public int ObrasActivas { get; set; }
    public decimal TotalGenerado { get; set; }
    public decimal Adelantos { get; set; }
    public decimal Liquidos { get; set; }
    public decimal Retenciones { get; set; }
    public int LineasDistribuidas { get; set; }
}
