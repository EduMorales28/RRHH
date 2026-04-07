using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Domain.Entities;

public class Periodo
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public EstadoPeriodo Estado { get; set; } = EstadoPeriodo.Abierto;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaCierre { get; set; }
}
