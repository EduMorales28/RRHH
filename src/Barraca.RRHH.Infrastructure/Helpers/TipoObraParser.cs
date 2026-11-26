using Barraca.RRHH.Domain.Enums;
using System.Globalization;
using System.Text;

namespace Barraca.RRHH.Infrastructure.Helpers;

public static class TipoObraParser
{
    // Texto con el que se guarda y se muestra cada tipo (igual que en las planillas).
    public static string Texto(TipoObra tipo) => tipo switch
    {
        TipoObra.Construccion => "CONSTRUCCION",
        TipoObra.IndustriaYComercio => "INDUSTRIA Y COMERCIO",
        TipoObra.Administracion => "ADMINISTRACION",
        TipoObra.Metalurgica => "METALURGICA",
        _ => "N-A"
    };

    // Indica si el texto corresponde a un tipo conocido; Parse devuelve N-A para cualquier texto desconocido.
    public static bool EsReconocido(string? value)
    {
        var normalized = NormalizarTexto(value);
        return normalized.StartsWith("ADMINISTRACION", StringComparison.Ordinal)
            || normalized.StartsWith("METALURG", StringComparison.Ordinal)
            || normalized is "REDPAGOS" or "REDPAGO"
                or "CONSTRUCCION" or "CONSTRUCCIONES"
                or "INDUSTRIA Y COMERCIO" or "INDUSTRIAYCOMERCIO" or "IND Y COM" or "IND COM" or "INDUSTRIA COMERCIO"
                or "NA" or "N A";
    }

    public static TipoObra Parse(string? value)
    {
        var normalized = NormalizarTexto(value);

        if (normalized.StartsWith("ADMINISTRACION", StringComparison.Ordinal))
            return TipoObra.Administracion;

        if (normalized is "REDPAGOS" or "REDPAGO")
            return TipoObra.Construccion;

        if (normalized.StartsWith("METALURG", StringComparison.Ordinal))
            return TipoObra.Metalurgica;

        return normalized switch
        {
            "CONSTRUCCION" => TipoObra.Construccion,
            "CONSTRUCCIONES" => TipoObra.Construccion,
            "INDUSTRIA Y COMERCIO" => TipoObra.IndustriaYComercio,
            "INDUSTRIAYCOMERCIO" => TipoObra.IndustriaYComercio,
            "IND Y COM" => TipoObra.IndustriaYComercio,
            "IND COM" => TipoObra.IndustriaYComercio,
            "INDUSTRIA COMERCIO" => TipoObra.IndustriaYComercio,
            "NA" => TipoObra.NA,
            "N A" => TipoObra.NA,
            "N-A" => TipoObra.NA,
            _ => TipoObra.NA
        };
    }

    private static string NormalizarTexto(string? value)
    {
        var input = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var descompuesto = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);

        foreach (var ch in descompuesto)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                continue;
            }

            if (ch is ' ' or '-' or '_' or '/' or '.')
                sb.Append(' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
