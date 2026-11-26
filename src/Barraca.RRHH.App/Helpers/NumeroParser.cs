using System.Globalization;
using System.Windows.Data;

namespace Barraca.RRHH.App.Helpers;

public static class NumeroParser
{
    public static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-UY");

    // Lee números escritos a mano o pegados desde Excel: "41.398", "3.279,60", "3279,6", "3279.60", "$ 1.500".
    public static bool TryParse(string? texto, out decimal valor)
    {
        valor = 0m;
        var s = new string((texto ?? string.Empty).Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray());
        if (s.Length == 0)
            return string.IsNullOrWhiteSpace(texto) || texto.Trim() is "-" or "$";

        var ultimaComa = s.LastIndexOf(',');
        var ultimoPunto = s.LastIndexOf('.');

        if (ultimaComa >= 0 && ultimoPunto >= 0)
        {
            // El separador que aparece último es el decimal.
            s = ultimaComa > ultimoPunto
                ? s.Replace(".", string.Empty).Replace(',', '.')
                : s.Replace(",", string.Empty);
        }
        else if (ultimaComa >= 0)
        {
            s = s.Count(c => c == ',') == 1 ? s.Replace(',', '.') : s.Replace(",", string.Empty);
        }
        else if (ultimoPunto >= 0)
        {
            // Un solo punto con exactamente 3 dígitos detrás se toma como separador de miles ("41.398").
            var digitosDetras = s.Length - ultimoPunto - 1;
            if (s.Count(c => c == '.') > 1 || digitosDetras == 3)
                s = s.Replace(".", string.Empty);
        }

        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valor);
    }

    public static string Formatear(decimal valor) => valor.ToString("#,##0.##", Cultura);
}

// Muestra montos y horas con formato uruguayo y acepta lo que escribe el usuario.
public class NumeroConverter : IValueConverter
{
    // Los ceros se muestran vacíos para que se vea rápido qué falta completar.
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is decimal d && d != 0m ? NumeroParser.Formatear(d) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (NumeroParser.TryParse(value as string, out var d))
            return d;

        throw new FormatException("Número inválido. Ejemplos válidos: 41.398 o 3.279,60");
    }
}
