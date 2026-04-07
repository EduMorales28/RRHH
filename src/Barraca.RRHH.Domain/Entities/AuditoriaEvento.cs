namespace Barraca.RRHH.Domain.Entities;

public class AuditoriaEvento
{
    public int Id { get; set; }
    public DateTime FechaHora { get; set; } = DateTime.UtcNow;
    public string Usuario { get; set; } = string.Empty;
    public string Modulo { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
    public string Entidad { get; set; } = string.Empty;
    public string EntidadClave { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
}
