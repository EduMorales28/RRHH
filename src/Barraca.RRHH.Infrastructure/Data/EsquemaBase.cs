using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Data;

// La base se crea con EnsureCreated (sin migraciones). Lo que se agrega al modelo después
// se incorpora acá a las bases existentes, sin borrar datos. Debe llamarse al iniciar, antes de cualquier consulta.
public static class EsquemaBase
{
    public static async Task ActualizarAsync(BarracaDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await AgregarColumnaSqliteAsync(db, "Funcionarios", "RetencionFija", "TEXT NOT NULL DEFAULT '0.0'");
            await AgregarColumnaSqliteAsync(db, "Funcionarios", "HoraFijaSede", "INTEGER NOT NULL DEFAULT 0");
            await AgregarColumnaSqliteAsync(db, "HorasMensuales", "HorasLluvia", "TEXT NOT NULL DEFAULT '0.0'");

            foreach (var (tabla, columna, referencia) in new[] { ("CodigosHoras", "Codigo", "Funcionario"), ("ObrasHoras", "Nombre", "Obra") })
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    CREATE TABLE IF NOT EXISTS "{tabla}" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_{tabla}" PRIMARY KEY AUTOINCREMENT,
                        "TipoObra" INTEGER NOT NULL,
                        "{columna}" TEXT NOT NULL,
                        "{referencia}Id" INTEGER NOT NULL,
                        CONSTRAINT "FK_{tabla}_{referencia}s_{referencia}Id" FOREIGN KEY ("{referencia}Id") REFERENCES "{referencia}s" ("Id") ON DELETE CASCADE)
                    """);
                await db.Database.ExecuteSqlRawAsync(
                    $"CREATE UNIQUE INDEX IF NOT EXISTS \"IX_{tabla}_TipoObra_{columna}\" ON \"{tabla}\" (\"TipoObra\", \"{columna}\")");
            }

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "CodigosAbaco" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_CodigosAbaco" PRIMARY KEY AUTOINCREMENT,
                    "TipoObra" INTEGER NOT NULL,
                    "Codigo" TEXT NOT NULL,
                    "FuncionarioId" INTEGER NOT NULL,
                    CONSTRAINT "FK_CodigosAbaco_Funcionarios_FuncionarioId" FOREIGN KEY ("FuncionarioId") REFERENCES "Funcionarios" ("Id") ON DELETE CASCADE)
                """);
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_CodigosAbaco_TipoObra_Codigo\" ON \"CodigosAbaco\" (\"TipoObra\", \"Codigo\")");
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS \"IX_CodigosAbaco_FuncionarioId\" ON \"CodigosAbaco\" (\"FuncionarioId\")");
            return;
        }

        if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('Funcionarios', 'RetencionFija') IS NULL
                    ALTER TABLE Funcionarios ADD RetencionFija decimal(18,2) NOT NULL CONSTRAINT DF_Funcionarios_RetencionFija DEFAULT 0;
                IF COL_LENGTH('Funcionarios', 'HoraFijaSede') IS NULL
                    ALTER TABLE Funcionarios ADD HoraFijaSede bit NOT NULL CONSTRAINT DF_Funcionarios_HoraFijaSede DEFAULT 0;
                IF COL_LENGTH('HorasMensuales', 'HorasLluvia') IS NULL
                    ALTER TABLE HorasMensuales ADD HorasLluvia decimal(18,2) NOT NULL CONSTRAINT DF_HorasMensuales_HorasLluvia DEFAULT 0;
                IF OBJECT_ID('CodigosHoras') IS NULL
                BEGIN
                    CREATE TABLE CodigosHoras (
                        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CodigosHoras PRIMARY KEY,
                        TipoObra int NOT NULL,
                        Codigo nvarchar(50) NOT NULL,
                        FuncionarioId int NOT NULL CONSTRAINT FK_CodigosHoras_Funcionarios_FuncionarioId REFERENCES Funcionarios (Id) ON DELETE CASCADE);
                    CREATE UNIQUE INDEX IX_CodigosHoras_TipoObra_Codigo ON CodigosHoras (TipoObra, Codigo);
                END
                IF OBJECT_ID('ObrasHoras') IS NULL
                BEGIN
                    CREATE TABLE ObrasHoras (
                        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ObrasHoras PRIMARY KEY,
                        TipoObra int NOT NULL,
                        Nombre nvarchar(200) NOT NULL,
                        ObraId int NOT NULL CONSTRAINT FK_ObrasHoras_Obras_ObraId REFERENCES Obras (Id) ON DELETE CASCADE);
                    CREATE UNIQUE INDEX IX_ObrasHoras_TipoObra_Nombre ON ObrasHoras (TipoObra, Nombre);
                END
                IF OBJECT_ID('CodigosAbaco') IS NULL
                BEGIN
                    CREATE TABLE CodigosAbaco (
                        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CodigosAbaco PRIMARY KEY,
                        TipoObra int NOT NULL,
                        Codigo nvarchar(50) NOT NULL,
                        FuncionarioId int NOT NULL CONSTRAINT FK_CodigosAbaco_Funcionarios_FuncionarioId REFERENCES Funcionarios (Id) ON DELETE CASCADE);
                    CREATE UNIQUE INDEX IX_CodigosAbaco_TipoObra_Codigo ON CodigosAbaco (TipoObra, Codigo);
                END
                """);
        }
    }

    private static async Task AgregarColumnaSqliteAsync(BarracaDbContext db, string tabla, string columna, string definicion)
    {
        if (!await ExisteColumnaSqliteAsync(db, tabla, columna))
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{tabla}\" ADD COLUMN \"{columna}\" {definicion}");
    }

    private static async Task<bool> ExisteColumnaSqliteAsync(BarracaDbContext db, string tabla, string columna)
    {
        var conexion = db.Database.GetDbConnection();
        var abiertaAca = conexion.State != System.Data.ConnectionState.Open;
        if (abiertaAca)
            await conexion.OpenAsync();

        try
        {
            await using var comando = conexion.CreateCommand();
            comando.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tabla}') WHERE name = '{columna}'";
            return Convert.ToInt64(await comando.ExecuteScalarAsync()) > 0;
        }
        finally
        {
            if (abiertaAca)
                await conexion.CloseAsync();
        }
    }
}
