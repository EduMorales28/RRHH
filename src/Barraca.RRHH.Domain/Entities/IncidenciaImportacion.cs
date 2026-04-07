namespace Barraca.RRHH.Domain.Entities;

public class IncidenciaImportacion
{
    public int Id { get; set; }
    public DateTime FechaHora { get; set; } = DateTime.UtcNow;
    public string TipoArchivo { get; set; } = string.Empty;
    public string PeriodoCodigo { get; set; } = string.Empty;
    public int? FilaOrigen { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool Resuelta { get; set; }
    public string Resolucion { get; set; } = string.Empty;
}
