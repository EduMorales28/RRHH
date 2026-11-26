namespace Barraca.RRHH.Domain.Entities;

// Datos del BPS de un mes que no son facturas. Hoy, la tasa con la que se calcula el BPS de la Sede:
// nominal de cada administrativo × tasa. Es la suma, concepto por concepto de la factura de IyC,
// de descuento ÷ monto gravado (39,656 % en 2025). Se guarda como fracción: 0,39656.
public class BpsPeriodo
{
    public int Id { get; set; }
    public int PeriodoId { get; set; }
    public decimal TasaSede { get; set; }

    public Periodo? Periodo { get; set; }
}
