using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Domain.Entities;

public class Obra
{
    public int Id { get; set; }
    public string NumeroObra { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public TipoObra TipoObra { get; set; }
    public string TipoObraOriginal { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public bool Activa { get; set; } = true;
}
