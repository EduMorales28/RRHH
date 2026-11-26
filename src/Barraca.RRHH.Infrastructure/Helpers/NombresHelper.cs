using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Barraca.RRHH.Infrastructure.Helpers;

// Comparación de nombres de personas y obras escritos de forma distinta en cada sistema
// ("SANCHEZ, FABRICIO DANIEL", "Fabricio Sanchez(V-Medio Oficial)").
public static class NombresHelper
{
    // Mayúsculas, sin tildes y sin signos.
    public static string Normalizar(string? texto)
    {
        var descompuesto = (texto ?? string.Empty).ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    // Palabras de 3 o más letras, sin lo que está entre paréntesis (la categoría).
    public static List<string> Tokens(string? nombre) =>
        Normalizar(Regex.Replace(nombre ?? string.Empty, @"\(.*?\)", " "))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3)
            .ToList();

    // Categoría entre paréntesis sin el grado en números romanos: "(VIII-Oficial Albañil)" -> "OFICIAL ALBANIL".
    public static string Categoria(string? nombre)
    {
        var m = Regex.Match(nombre ?? string.Empty, @"\(([^)]*)\)");
        if (!m.Success)
            return string.Empty;

        var palabras = Normalizar(m.Groups[1].Value).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !Regex.IsMatch(p, "^[IVX]+$"));
        return string.Join(' ', palabras);
    }

    // Palabras de "programa" que aparecen en "otro" (tolera una letra distinta en palabras largas: Alonso/Alonzo).
    public static int Coincidencias(IReadOnlyCollection<string> otro, IReadOnlyCollection<string> programa) =>
        programa.Count(p => otro.Any(a => a == p || (p.Length >= 5 && a.Length >= 5 && Distancia(a, p) <= 1)));

    // Algún apellido de 5+ letras que solo tiene una persona del programa coincide (ej. "Ferrizo").
    public static bool CoincideTokenUnico(IReadOnlyCollection<string> otro, IReadOnlyCollection<string> programa, IReadOnlyDictionary<string, int> frecuencia) =>
        programa.Any(p => p.Length >= 5 && frecuencia.TryGetValue(p, out var n) && n == 1 && otro.Contains(p));

    // En cuántos nombres aparece cada palabra.
    public static Dictionary<string, int> Frecuencias(IEnumerable<string> nombres) =>
        nombres.SelectMany(n => Tokens(n).Distinct()).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());

    public static int Distancia(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}
