using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using Barraca.RRHH.App.Helpers;
using Barraca.RRHH.App.ViewModels;
using Barraca.RRHH.Infrastructure.Data;
using Barraca.RRHH.Infrastructure.Extensions;
using Barraca.RRHH.Infrastructure.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Barraca.RRHH.App;

public partial class App : System.Windows.Application
{
    public IServiceProvider Services { get; private set; } = default!;
    public static string UserDataRoot { get; private set; } = string.Empty;

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            var baseConfiguration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            // BARRACA_RRHH_DATA_DIR permite abrir el programa contra otra carpeta de datos (pruebas).
            UserDataRoot = Environment.GetEnvironmentVariable("BARRACA_RRHH_DATA_DIR") is { Length: > 0 } dirPrueba
                ? dirPrueba
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Sueldos Almirtaun");
            Directory.CreateDirectory(UserDataRoot);
            var logsDir = Path.Combine(UserDataRoot, "logs");
            Directory.CreateDirectory(logsDir);

            var resolvedConnectionString = (baseConfiguration.GetConnectionString("DefaultConnection") ?? string.Empty)
                .Replace("{DATA_DIR}", UserDataRoot.Replace("\\", "/"), StringComparison.OrdinalIgnoreCase);

            // Si falla, no se sigue: abrir con una base vacía haría parecer que se perdieron los datos.
            RespaldoBase.TraerDatosDeCarpetaAnterior(resolvedConnectionString);

            var configuration = new ConfigurationBuilder()
                .AddConfiguration(baseConfiguration)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = resolvedConnectionString
                })
                .Build();

            var configuredLogPath = configuration["Logging:Path"];
            var logFileName = string.IsNullOrWhiteSpace(configuredLogPath)
                ? "sueldos-almirtaun-.log"
                : Path.GetFileName(configuredLogPath);
            var logPath = Path.Combine(logsDir, logFileName);

            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                RespaldoBase.CopiaDiaria(resolvedConnectionString);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "No se pudo crear la copia diaria de la base.");
            }

            // Números y fechas con formato uruguayo en toda la interfaz.
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));
            services.AddBarracaInfrastructure(configuration);
            services.AddSingleton<MainViewModel>();
            services.AddTransient<MainWindow>();

            Services = services.BuildServiceProvider();

            // Ensure database is created and obras TipoObra is consistent with TipoObraOriginal
            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BarracaDbContext>();
                db.Database.EnsureCreated();
                await EsquemaBase.ActualizarAsync(db);
                await RepararTipoObraObrasAsync(db);
            }

            var window = Services.GetRequiredService<MainWindow>();
            window.DataContext = Services.GetRequiredService<MainViewModel>();
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Error fatal al iniciar la aplicacion.");
            MessageBox.Show($"Error fatal al iniciar la aplicacion: {ex.Message}", "Inicio fallido", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static async Task RepararTipoObraObrasAsync(BarracaDbContext db)
    {
        var obras = await db.Obras.ToListAsync();
        var changed = false;
        foreach (var obra in obras)
        {
            var tipoEsperado = TipoObraParser.Parse(obra.TipoObraOriginal);
            if (obra.TipoObra != tipoEsperado)
            {
                obra.TipoObra = tipoEsperado;
                changed = true;
            }
        }
        if (changed)
            await db.SaveChangesAsync();
    }
}
