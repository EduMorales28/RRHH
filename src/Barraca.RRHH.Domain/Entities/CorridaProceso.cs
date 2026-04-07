namespace Barraca.RRHH.Domain.Entities;

public class CorridaProceso
{
    public int Id { get; set; }
    public int PeriodoId { get; set; }
    public string TipoProceso { get; set; } = string.Empty;
    public string CodigoCorrida { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; } = DateTime.UtcNow;

    public Periodo? Periodo { get; set; }
}
