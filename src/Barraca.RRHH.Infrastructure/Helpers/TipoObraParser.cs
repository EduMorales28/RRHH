using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Infrastructure.Helpers;

public static class TipoObraParser
{
    public static TipoObra Parse(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();

        if (normalized.StartsWith("ADMINISTRACION"))
            return TipoObra.Administracion;

        return normalized switch
        {
            "CONSTRUCCION" => TipoObra.Construccion,
            "INDUSTRIA Y COMERCIO" => TipoObra.IndustriaYComercio,
            "INDUSTRIAYCOMERCIO" => TipoObra.IndustriaYComercio,
            "N-A" => TipoObra.NA,
            _ => TipoObra.NA
        };
    }
}
