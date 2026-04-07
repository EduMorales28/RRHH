namespace Barraca.RRHH.Infrastructure.Reports;

public class PlaceholderReportService
{
    public Task GenerarPdfAsync(string nombreReporte, string destino)
    {
        File.WriteAllText(destino, $"Pendiente integrar QuestPDF para: {nombreReporte}");
        return Task.CompletedTask;
    }
}
