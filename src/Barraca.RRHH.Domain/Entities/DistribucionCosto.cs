using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Domain.Entities;

public class DistribucionCosto
{
    public int Id { get; set; }
    public int PeriodoId { get; set; }
    public int CorridaProcesoId { get; set; }
    public TipoObra TipoObra { get; set; }
    public int ObraId { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public decimal HorasLinea { get; set; }
    public decimal HorasTotalesTipoObra { get; set; }
    public decimal CostoTotalTipoObra { get; set; }
    public decimal PorcentajeParticipacion { get; set; }
    public decimal MontoLinea { get; set; }
    public decimal ValorHora { get; set; }
    public decimal Jornales { get; set; }

    public Periodo? Periodo { get; set; }
    public CorridaProceso? CorridaProceso { get; set; }
    public Obra? Obra { get; set; }
}
