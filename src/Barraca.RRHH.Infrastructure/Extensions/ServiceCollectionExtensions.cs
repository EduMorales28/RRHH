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
        services.AddDbContext<BarracaDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

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
