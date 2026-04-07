using System.IO;
using System.Windows;
using Barraca.RRHH.App.ViewModels;
using Barraca.RRHH.Infrastructure.Data;
using Barraca.RRHH.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Barraca.RRHH.App;

public partial class App : System.Windows.Application
{
    public IServiceProvider Services { get; private set; } = default!;
    public static string UserDataRoot { get; private set; } = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            UserDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BarracaRRHH");
            var logsDir = Path.Combine(UserDataRoot, "logs");
            Directory.CreateDirectory(logsDir);

            var configuredLogPath = configuration["Logging:Path"];
            var logFileName = string.IsNullOrWhiteSpace(configuredLogPath)
                ? "barraca-rrhh-.log"
                : Path.GetFileName(configuredLogPath);
            var logPath = Path.Combine(logsDir, logFileName);

            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));
            services.AddBarracaInfrastructure(configuration);
            services.AddSingleton<MainViewModel>();
            services.AddTransient<MainWindow>();

            Services = services.BuildServiceProvider();

            // Ensure database is created at application startup
            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BarracaDbContext>();
                db.Database.EnsureCreated();
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
}
