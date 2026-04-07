namespace Barraca.RRHH.Application.Interfaces;

public interface IReportService
{
    Task<string> GenerarAdelantosAsync(string periodo, string carpetaDestino, string usuario);
    Task<string> GenerarDistribucionObrasAsync(string periodo, string carpetaDestino, string usuario);
    Task<string> GenerarRedPagosAsync(string periodo, string carpetaDestino, string usuario);
    Task<string> GenerarNAAsync(string periodo, string carpetaDestino, string usuario);
    Task<string> GenerarRetencionesAsync(string periodo, string carpetaDestino, string usuario);
    Task<string> GenerarConsolidadoGeneralAsync(string periodo, string carpetaDestino, string usuario);

    // Delete report record (if persisted) - placeholder for API parity
    Task<int> EliminarReporteAsync(string ruta, string usuario);
}
