using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Application.DTOs;

public class DistribucionLineaDto
{
    public int Id { get; set; }
    public TipoObra TipoObra { get; set; }
    public int ObraId { get; set; }
    public string NumeroObra { get; set; } = string.Empty;
    public string NombreObra { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public decimal HorasLinea { get; set; }
    public decimal HorasTotalesTipoObra { get; set; }
    public decimal CostoTotalTipoObra { get; set; }
    public decimal PorcentajeParticipacion { get; set; }
    public decimal MontoLinea { get; set; }
    public decimal ValorHora { get; set; }
    public decimal Jornales { get; set; }
}
