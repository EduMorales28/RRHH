using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Application.DTOs;

// BPS por obra (pedido del 26/11/2026): las facturas del BPS de Construcción y de Industria y Comercio
// se reparten entre las obras por horas equivalentes; de IyC antes se separa la Sede (nominal × tasa).

public class FacturaBpsDto
{
    public int Id { get; set; }
    // Construcción o IndustriaYComercio.
    public TipoObra TipoObra { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Importe { get; set; }
}

public class BpsMesDto
{
    public string Periodo { get; set; } = string.Empty;
    public bool Cerrado { get; set; }
    public List<FacturaBpsDto> Facturas { get; set; } = new();
    // Fracción (0,39656). Null si nunca se cargó.
    public decimal? TasaSede { get; set; }
    // Si el mes no tiene tasa propia, de qué mes se tomó la sugerida (vacío si es propia).
    public string TasaSedeDe { get; set; } = string.Empty;
}

public class RepartoBpsLineaDto
{
    public TipoObra TipoObra { get; set; }
    public string Tipo => TipoObra == TipoObra.Construccion ? "Construcción" : "Ind. y Comercio";
    public string NumeroObra { get; set; } = string.Empty;
    public string NombreObra { get; set; } = string.Empty;
    public bool EsSede { get; set; }
    public decimal Horas { get; set; }
    // Parte de las horas de su tipo (0 a 1).
    public decimal Porcentaje { get; set; }
    public decimal Importe { get; set; }
}

public class SedeBpsPersonaDto
{
    public string NumeroFuncionario { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public decimal Nominal { get; set; }
    public decimal Bps { get; set; }
}

// Una factura para cargar en SP: igual que como se cargó el BPS hasta 10/2025
// (proveedor BPS, a crédito, sin IVA, una por obra con C e IC sumados, insumo BPS × 1).
public class FacturaSpDto
{
    public string Numero { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string NumeroObra { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string Observacion { get; set; } = string.Empty;
    public decimal Importe { get; set; }
}

public class RepartoBpsResultadoDto
{
    public string Periodo { get; set; } = string.Empty;
    // Si hay problemas no se reparte nada: un dato pendiente no se toma como cero.
    public List<string> Problemas { get; set; } = new();
    // Avisos que no frenan el reparto.
    public List<string> Avisos { get; set; } = new();
    public bool Listo => Problemas.Count == 0;

    public decimal FacturadoConstruccion { get; set; }
    public decimal FacturadoIndustria { get; set; }
    public decimal TasaSede { get; set; }
    public decimal Sede { get; set; }
    public decimal ARepartirConstruccion { get; set; }
    public decimal ARepartirIndustria { get; set; }
    public decimal HorasConstruccion { get; set; }
    public decimal HorasIndustria { get; set; }

    public List<RepartoBpsLineaDto> Lineas { get; set; } = new();
    public List<SedeBpsPersonaDto> PersonasSede { get; set; } = new();
    public List<FacturaSpDto> FacturasSp { get; set; } = new();
}
