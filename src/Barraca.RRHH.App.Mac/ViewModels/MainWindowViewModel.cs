using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Barraca.RRHH.Application.Interfaces;

namespace Barraca.RRHH.App.Mac.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IDashboardService _dashboardService;
    private readonly IPeriodoService _periodoService;
    private readonly IDistribucionService _distribucionService;
    private readonly IReportService _reportService;

    private string _periodo = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    private string _status = "Listo";
    private string _totalGenerado = "0.00";
    private string _adelantos = "0.00";
    private string _liquidos = "0.00";
    private string _retenciones = "0.00";
    private int _funcionariosActivos;
    private int _obrasActivas;
    private int _lineasDistribuidas;

    public MainWindowViewModel(
        IDashboardService dashboardService,
        IPeriodoService periodoService,
        IDistribucionService distribucionService,
        IReportService reportService)
    {
        _dashboardService = dashboardService;
        _periodoService = periodoService;
        _distribucionService = distribucionService;
        _reportService = reportService;

        CarpetaReportes = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BarracaRRHH",
            "Reportes");

        RefrescarCommand = new AsyncCommand(RefrescarAsync);
        CalcularDistribucionCommand = new AsyncCommand(CalcularDistribucionAsync);
        GenerarReportesCommand = new AsyncCommand(GenerarReportesAsync);

        _ = RefrescarAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Periodo
    {
        get => _periodo;
        set => SetField(ref _periodo, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string CarpetaReportes { get; }

    public string TotalGenerado
    {
        get => _totalGenerado;
        set => SetField(ref _totalGenerado, value);
    }

    public string Adelantos
    {
        get => _adelantos;
        set => SetField(ref _adelantos, value);
    }

    public string Liquidos
    {
        get => _liquidos;
        set => SetField(ref _liquidos, value);
    }

    public string Retenciones
    {
        get => _retenciones;
        set => SetField(ref _retenciones, value);
    }

    public int FuncionariosActivos
    {
        get => _funcionariosActivos;
        set => SetField(ref _funcionariosActivos, value);
    }

    public int ObrasActivas
    {
        get => _obrasActivas;
        set => SetField(ref _obrasActivas, value);
    }

    public int LineasDistribuidas
    {
        get => _lineasDistribuidas;
        set => SetField(ref _lineasDistribuidas, value);
    }

    public ObservableCollection<string> ReportesRecientes { get; } = new();

    public ICommand RefrescarCommand { get; }
    public ICommand CalcularDistribucionCommand { get; }
    public ICommand GenerarReportesCommand { get; }

    private async Task RefrescarAsync()
    {
        try
        {
            var periodoNormalizado = NormalizarPeriodo(Periodo);
            await _periodoService.AbrirPeriodoAsync(periodoNormalizado, "admin");
            var dashboard = await _dashboardService.ObtenerResumenAsync(periodoNormalizado);

            Periodo = periodoNormalizado;
            TotalGenerado = dashboard.TotalGenerado.ToString("N2", CultureInfo.InvariantCulture);
            Adelantos = dashboard.Adelantos.ToString("N2", CultureInfo.InvariantCulture);
            Liquidos = dashboard.Liquidos.ToString("N2", CultureInfo.InvariantCulture);
            Retenciones = dashboard.Retenciones.ToString("N2", CultureInfo.InvariantCulture);
            FuncionariosActivos = dashboard.FuncionariosActivos;
            ObrasActivas = dashboard.ObrasActivas;
            LineasDistribuidas = dashboard.LineasDistribuidas;
            Status = $"Período activo: {Periodo}";
        }
        catch (Exception ex)
        {
            Status = $"Error refrescando datos: {ex.Message}";
        }
    }

    private async Task CalcularDistribucionAsync()
    {
        try
        {
            var periodoNormalizado = NormalizarPeriodo(Periodo);
            var lineas = await _distribucionService.CalcularDistribucionAsync(periodoNormalizado, "admin", true);
            await RefrescarAsync();
            Status = $"Distribución recalculada: {lineas.Count} líneas";
        }
        catch (Exception ex)
        {
            Status = $"Error calculando distribución: {ex.Message}";
        }
    }

    private async Task GenerarReportesAsync()
    {
        try
        {
            var periodoNormalizado = NormalizarPeriodo(Periodo);
            Directory.CreateDirectory(CarpetaReportes);

            var reportes = new[]
            {
                await _reportService.GenerarAdelantosAsync(periodoNormalizado, CarpetaReportes, "admin"),
                await _reportService.GenerarDistribucionObrasAsync(periodoNormalizado, CarpetaReportes, "admin"),
                await _reportService.GenerarRedPagosAsync(periodoNormalizado, CarpetaReportes, "admin"),
                await _reportService.GenerarNAAsync(periodoNormalizado, CarpetaReportes, "admin"),
                await _reportService.GenerarRetencionesAsync(periodoNormalizado, CarpetaReportes, "admin"),
                await _reportService.GenerarConsolidadoGeneralAsync(periodoNormalizado, CarpetaReportes, "admin")
            };

            ReportesRecientes.Clear();
            foreach (var ruta in reportes)
            {
                ReportesRecientes.Add(Path.GetFileName(ruta));
            }

            Status = $"Reportes generados: {reportes.Length} archivos";
        }
        catch (Exception ex)
        {
            Status = $"Error generando reportes: {ex.Message}";
        }
    }

    private static string NormalizarPeriodo(string input)
    {
        var value = input.Trim();
        if (!Regex.IsMatch(value, "^\\d{4}-\\d{2}$"))
            throw new InvalidOperationException("El período debe tener formato yyyy-MM.");

        var month = int.Parse(value.Substring(5, 2), CultureInfo.InvariantCulture);
        if (month is < 1 or > 12)
            throw new InvalidOperationException("El mes del período debe estar entre 01 y 12.");

        return value;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private bool _running;

    public AsyncCommand(Func<Task> execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running;

    public async void Execute(object? parameter)
    {
        if (_running)
            return;

        _running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await _execute();
        }
        finally
        {
            _running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
