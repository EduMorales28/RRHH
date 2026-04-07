using Barraca.RRHH.Domain.Enums;
using System.Globalization;
using System.Text;

namespace Barraca.RRHH.Infrastructure.Helpers;

public static class TipoObraParser
{
    public static TipoObra Parse(string? value)
    {
        var normalized = NormalizarTexto(value);

        if (normalized.StartsWith("ADMINISTRACION", StringComparison.Ordinal))
            return TipoObra.Administracion;

        if (normalized is "REDPAGOS" or "REDPAGO")
            return TipoObra.Construccion;

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
