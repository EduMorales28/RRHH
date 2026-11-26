using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.App.ViewModels.Carga;

public static class OpcionesBps
{
    public static readonly string[] Empresas = { "Construcción", "Industria y Comercio" };

    // Las facturas que trae el BPS cada mes; se proponen vacías en un mes sin facturas.
    public static readonly (TipoObra Tipo, string Descripcion)[] FacturasTipicas =
    {
        (TipoObra.Construccion, "Nómina (FRL, FGCL, adicional SNIS)"),
        (TipoObra.Construccion, "Fondos de la construcción"),
        (TipoObra.Construccion, "IRPF"),
        (TipoObra.IndustriaYComercio, "Nómina (jubilatorio, FONASA, FRL, FGCL)"),
        (TipoObra.IndustriaYComercio, "Fondos de la construcción"),
        (TipoObra.IndustriaYComercio, "IRPF"),
        (TipoObra.IndustriaYComercio, "Fondo Social Metalúrgico"),
    };

    public static string Texto(TipoObra tipo) => tipo == TipoObra.IndustriaYComercio ? Empresas[1] : Empresas[0];
}

// Una factura del BPS del mes en la pestaña "BPS por obra".
public sealed class FacturaBpsFila : ViewModelBase
{
    private string _empresa = OpcionesBps.Empresas[0];
    private string _descripcion = string.Empty;
    private decimal _importe;

    public string Empresa { get => _empresa; set { if (SetProperty(ref _empresa, value ?? string.Empty)) AlCambiar?.Invoke(); } }
    public string Descripcion { get => _descripcion; set { if (SetProperty(ref _descripcion, value ?? string.Empty)) AlCambiar?.Invoke(); } }
    public decimal Importe { get => _importe; set { if (SetProperty(ref _importe, value)) AlCambiar?.Invoke(); } }

    public TipoObra TipoObra => Empresa == OpcionesBps.Empresas[1] ? TipoObra.IndustriaYComercio : TipoObra.Construccion;
    public bool EstaVacia => Descripcion.Trim().Length == 0 && Importe == 0;

    internal Action? AlCambiar { get; set; }

    public static FacturaBpsFila Desde(TipoObra tipo, string descripcion, decimal importe) => new()
    {
        _empresa = OpcionesBps.Texto(tipo),
        _descripcion = descripcion,
        _importe = importe
    };

    public FacturaBpsDto ADto() => new() { TipoObra = TipoObra, Descripcion = Descripcion.Trim(), Importe = Importe };
}
