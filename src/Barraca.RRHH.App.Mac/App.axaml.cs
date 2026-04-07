using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Barraca.RRHH.App.Mac.ViewModels;
using Barraca.RRHH.Infrastructure.Data;
using Barraca.RRHH.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Barraca.RRHH.App.Mac;

public partial class App : Avalonia.Application
{
    public IServiceProvider Services { get; private set; } = default!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            var baseConfiguration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var userDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BarracaRRHH");
            var logsDir = Path.Combine(userDataRoot, "logs");
            Directory.CreateDirectory(logsDir);

            var resolvedConnectionString = (baseConfiguration.GetConnectionString("DefaultConnection") ?? string.Empty)
                .Replace("{DATA_DIR}", userDataRoot.Replace("\\", "/"), StringComparison.OrdinalIgnoreCase);

            var configuration = new ConfigurationBuilder()
                .AddConfiguration(baseConfiguration)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = resolvedConnectionString
                })
                .Build();

            var logFileName = Path.GetFileName(configuration["Logging:Path"] ?? "barraca-rrhh-.log");
            var logPath = Path.Combine(logsDir, logFileName);

            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));
            services.AddBarracaInfrastructure(configuration);
            services.AddSingleton<MainWindowViewModel>();
            services.AddTransient<MainWindow>();

            Services = services.BuildServiceProvider();

            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BarracaDbContext>();
                db.Database.EnsureCreated();
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = Services.GetRequiredService<MainWindow>();
                window.DataContext = Services.GetRequiredService<MainWindowViewModel>();
                desktop.MainWindow = window;
                desktop.Exit += (_, _) => Log.CloseAndFlush();
            }
        }
        catch (Exception ex)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new Window
                {
                    Width = 720,
                    Height = 260,
                    Title = "Error de inicio",
                    Content = new TextBlock
                    {
                        Margin = new Thickness(16),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Text = $"No se pudo iniciar la aplicación.\n\n{ex.Message}"
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
