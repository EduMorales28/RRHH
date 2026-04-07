namespace Barraca.RRHH.Application.Interfaces;

public interface IExcelImportService
{
    Task<string> ImportarFuncionariosAsync(string filePath, string usuario = "sistema");
    Task<string> ImportarObrasAsync(string filePath, string usuario = "sistema");
    Task<string> ImportarHorasAsync(string filePath, string periodo, string usuario = "sistema");
    Task<string> ImportarPagosAsync(string filePath, string periodo, string usuario = "sistema");
}
