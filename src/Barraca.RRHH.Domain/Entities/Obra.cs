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
    // Número de la obra en SP cuando no es el mismo del programa (vacío = el mismo). Lo usa la planilla
    // del BPS por obra; por ejemplo, la 9201 ESCUELA FLORES va en SP a la 079 (pedido del 27/11/2026).
    public string CodigoSp { get; set; } = string.Empty;
}
