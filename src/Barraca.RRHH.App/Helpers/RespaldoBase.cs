using System.IO;
using Barraca.RRHH.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.App.Helpers;

// Copias de seguridad de la base SQLite en <datos>\respaldos.
public static class RespaldoBase
{
    private const int CopiasDiariasAConservar = 30;

    public static string Carpeta => Path.Combine(App.UserDataRoot, "respaldos");

    // La primera vez que se abre "Sueldos Almirtaun" copia los datos de la carpeta anterior del programa.
    // La carpeta anterior no se modifica; la base se copia al final para reintentar si algo se interrumpe.
    public static void TraerDatosDeCarpetaAnterior(string connectionString)
    {
        var baseNueva = ArchivoDeBase(connectionString);
        // BARRACA_RRHH_DATA_DIR_ANTERIOR solo se usa junto con BARRACA_RRHH_DATA_DIR, para probar con copias.
        var carpetaAnterior = Environment.GetEnvironmentVariable("BARRACA_RRHH_DATA_DIR") is { Length: > 0 }
            ? Environment.GetEnvironmentVariable("BARRACA_RRHH_DATA_DIR_ANTERIOR")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BarracaRRHH");

        if (baseNueva is null || File.Exists(baseNueva) || string.IsNullOrEmpty(carpetaAnterior))
            return;

        var baseAnterior = Path.Combine(carpetaAnterior, "barraca_rrhh.db");
        if (!File.Exists(baseAnterior))
            return;

        var carpetaNueva = Path.GetDirectoryName(baseNueva)!;
        Directory.CreateDirectory(carpetaNueva);

        foreach (var subcarpeta in new[] { "Reportes", "respaldos" })
        {
            var origen = Path.Combine(carpetaAnterior, subcarpeta);
            if (!Directory.Exists(origen))
                continue;

            var destino = Path.Combine(carpetaNueva, subcarpeta);
            Directory.CreateDirectory(destino);
            foreach (var archivo in Directory.GetFiles(origen))
            {
                var copia = Path.Combine(destino, Path.GetFileName(archivo));
                if (!File.Exists(copia))
                    File.Copy(archivo, copia);
            }
        }

        var temporal = baseNueva + ".tmp";
        if (File.Exists(temporal))
            File.Delete(temporal);

        using (var origen = new SqliteConnection($"Data Source={baseAnterior};Mode=ReadOnly;Pooling=False"))
        using (var copia = new SqliteConnection($"Data Source={temporal};Pooling=False"))
        {
            origen.Open();
            copia.Open();
            origen.BackupDatabase(copia);
        }

        File.Move(temporal, baseNueva);
    }

    // Al abrir el programa: una copia por día del archivo de la base (antes de abrir conexiones).
    public static void CopiaDiaria(string connectionString)
    {
        var archivo = ArchivoDeBase(connectionString);
        if (archivo is null || !File.Exists(archivo))
            return;

        Directory.CreateDirectory(Carpeta);
        var destino = Path.Combine(Carpeta, $"diaria_{DateTime.Now:yyyy-MM-dd}.db");
        if (!File.Exists(destino))
        {
            // Respaldo de SQLite: incluye lo que esté pendiente en el archivo -wal.
            using var origen = new SqliteConnection($"Data Source={archivo};Mode=ReadOnly;Pooling=False");
            using var copia = new SqliteConnection($"Data Source={destino};Pooling=False");
            origen.Open();
            copia.Open();
            origen.BackupDatabase(copia);
        }

        foreach (var viejo in Directory.GetFiles(Carpeta, "diaria_*.db").OrderByDescending(f => f).Skip(CopiasDiariasAConservar))
            File.Delete(viejo);
    }

    // Copia inmediata con la base abierta (por ejemplo, antes de borrar todo).
    public static async Task<string?> CopiaAhoraAsync(BarracaDbContext db, string motivo)
    {
        if (!db.Database.IsSqlite())
            return null;

        Directory.CreateDirectory(Carpeta);
        var destino = Path.Combine(Carpeta, $"{motivo}_{DateTime.Now:yyyy-MM-dd_HHmmss}.db");
#pragma warning disable EF1002 // La ruta la arma el programa, no el usuario.
        await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{destino.Replace("'", "''")}'");
#pragma warning restore EF1002
        return destino;
    }

    private static string? ArchivoDeBase(string connectionString)
    {
        try
        {
            var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
            return string.IsNullOrWhiteSpace(dataSource) ? null : Path.GetFullPath(dataSource);
        }
        catch (ArgumentException)
        {
            return null; // No es una cadena SQLite (SQL Server).
        }
    }
}
