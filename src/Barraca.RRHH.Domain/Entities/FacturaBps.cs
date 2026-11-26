using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Domain.Entities;

// Factura del BPS del mes de una de las dos empresas (Construcción o Industria y Comercio),
// por el total tal cual se paga. Se reparte entre las obras en la pestaña "BPS por obra".
public class FacturaBps
{
    public int Id { get; set; }
    public int PeriodoId { get; set; }
    public TipoObra TipoObra { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Importe { get; set; }

    public Periodo? Periodo { get; set; }
}
