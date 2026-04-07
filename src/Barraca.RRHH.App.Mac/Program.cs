using System.Globalization;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Infrastructure.Data;
using Barraca.RRHH.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Barraca.RRHH.App.Mac;

internal sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var userDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BarracaRRHH");
            var logsDir = Path.Combine(userDataRoot, "logs");
            var reportesDir = Path.Combine(userDataRoot, "Reportes");
            Directory.CreateDirectory(logsDir);
            Directory.CreateDirectory(reportesDir);

            var logFileName = Path.GetFileName(configuration["Logging:Path"] ?? "barraca-rrhh-.log");
            var logPath = Path.Combine(logsDir, logFileName);

            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));
            services.AddBarracaInfrastructure(configuration);

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<BarracaDbContext>();
            await db.Database.EnsureCreatedAsync();

            var periodoService = scope.ServiceProvider.GetRequiredService<IPeriodoService>();
            var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();
            var distribucionService = scope.ServiceProvider.GetRequiredService<IDistribucionService>();
            var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();

            var periodo = args.Length > 0 ? args[0] : DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            periodo = NormalizarPeriodo(periodo);

            await periodoService.AbrirPeriodoAsync(periodo, "admin");

            Console.WriteLine("Barraca RRHH macOS (CLI)");
            Console.WriteLine($"Periodo activo: {periodo}");
            while (true)
            {
                Console.WriteLine();
                var dashboard = await dashboardService.ObtenerResumenAsync(periodo);
                Console.WriteLine($"Total generado: {dashboard.TotalGenerado:N2}");
                Console.WriteLine($"Adelantos: {dashboard.Adelantos:N2}");
                Console.WriteLine($"Liquidos: {dashboard.Liquidos:N2}");
                Console.WriteLine($"Retenciones: {dashboard.Retenciones:N2}");
                Console.WriteLine($"Funcionarios activos: {dashboard.FuncionariosActivos}");
                Console.WriteLine($"Obras activas: {dashboard.ObrasActivas}");
                Console.WriteLine($"Lineas distribuidas: {dashboard.LineasDistribuidas}");
                Console.WriteLine();

                Console.WriteLine("Acciones:");
                Console.WriteLine("1) Recalcular distribucion");
                Console.WriteLine("2) Generar reportes PDF");
                Console.WriteLine("3) Salir");
                Console.Write("Selecciona opcion: ");

                var option = Console.ReadLine()?.Trim();
                if (option == "1")
                {
                    var lineas = await distribucionService.CalcularDistribucionAsync(periodo, "admin", true);
                    Console.WriteLine($"Distribucion recalculada: {lineas.Count} lineas");
                }
                else if (option == "2")
                {
                    var files = new[]
                    {
                        await reportService.GenerarAdelantosAsync(periodo, reportesDir, "admin"),
                        await reportService.GenerarDistribucionObrasAsync(periodo, reportesDir, "admin"),
                        await reportService.GenerarRedPagosAsync(periodo, reportesDir, "admin"),
                        await reportService.GenerarNAAsync(periodo, reportesDir, "admin"),
                        await reportService.GenerarRetencionesAsync(periodo, reportesDir, "admin"),
                        await reportService.GenerarConsolidadoGeneralAsync(periodo, reportesDir, "admin")
                    };

                    Console.WriteLine("Reportes generados:");
                    foreach (var f in files)
                        Console.WriteLine($"- {f}");
                }
                else if (option == "3")
                {
                    break;
                }
            }

            Log.CloseAndFlush();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
            Console.Error.WriteLine("Presiona Enter para cerrar...");
            Console.ReadLine();
            Log.CloseAndFlush();
            return 1;
        }
    }

    private static string NormalizarPeriodo(string input)
    {
        var value = input.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^\\d{4}-\\d{2}$"))
            throw new InvalidOperationException("El periodo debe tener formato yyyy-MM.");

        var month = int.Parse(value.Substring(5, 2), CultureInfo.InvariantCulture);
        if (month is < 1 or > 12)
            throw new InvalidOperationException("El mes del periodo debe estar entre 01 y 12.");

        return value;
    }
}
