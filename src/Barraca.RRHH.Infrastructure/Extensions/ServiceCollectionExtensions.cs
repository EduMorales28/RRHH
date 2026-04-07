using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Infrastructure.Data;
using Barraca.RRHH.Infrastructure.Imports;
using Barraca.RRHH.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barraca.RRHH.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBarracaInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection en la configuracion.");

        var provider = configuration["Database:Provider"]?.Trim();

        services.AddDbContext<BarracaDbContext>(options =>
        {
            if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase)
                || connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connectionString);
            }
            else
            {
                options.UseSqlServer(connectionString);
            }
        });

        services.AddScoped<IExcelImportService, ExcelImportService>();
        services.AddScoped<IDistribucionService, DistribucionService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IPeriodoService, PeriodoService>();
        services.AddScoped<IConsistenciaService, ConsistenciaService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<ICrudService, CrudService>();

        return services;
    }
}
