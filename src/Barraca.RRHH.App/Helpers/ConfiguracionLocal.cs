using System.IO;
using System.Text.Json;

namespace Barraca.RRHH.App.Helpers;

// Valores que el usuario cambia desde el programa y se recuerdan entre meses (en <datos>\configuracion.json).
public sealed class ConfiguracionLocal
{
    public decimal ValorTicket { get; set; } = 193.68m;

    private static string Ruta => Path.Combine(App.UserDataRoot, "configuracion.json");

    public static ConfiguracionLocal Cargar()
    {
        try
        {
            return File.Exists(Ruta)
                ? JsonSerializer.Deserialize<ConfiguracionLocal>(File.ReadAllText(Ruta)) ?? new ConfiguracionLocal()
                : new ConfiguracionLocal();
        }
        catch (JsonException)
        {
            return new ConfiguracionLocal();
        }
    }

    public void Guardar() =>
        File.WriteAllText(Ruta, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
