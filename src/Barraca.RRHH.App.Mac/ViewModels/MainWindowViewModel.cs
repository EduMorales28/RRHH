using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;

namespace Barraca.RRHH.App.Mac.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IDashboardService _dashboardService;
    private readonly IPeriodoService _periodoService;
    private readonly IDistribucionService _distribucionService;
    private readonly IReportService _reportService;
    private readonly IExcelImportService _excelImportService;
    private readonly IConsistenciaService _consistenciaService;

    private string _periodo = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    private string _status = "Listo";
    private string _totalGenerado = "0.00";
    private string _adelantos = "0.00";
    private string _liquidos = "0.00";
    private string _retenciones = "0.00";
    private int _funcionariosActivos;
    private int _obrasActivas;
    private int _lineasDistribuidas;
    private string _rutaFuncionarios = string.Empty;
    private string _rutaObras = string.Empty;
    private string _rutaHoras = string.Empty;
    private string _rutaPagos = string.Empty;
    private string _carpetaReportes = string.Empty;

    public MainWindowViewModel(
        IDashboardService dashboardService,
        IPeriodoService periodoService,
        IDistribucionService distribucionService,
        IReportService reportService,
        IExcelImportService excelImportService,
        IConsistenciaService consistenciaService)
    {
        _dashboardService = dashboardService;
        _periodoService = periodoService;
        _distribucionService = distribucionService;
        _reportService = reportService;
        _excelImportService = excelImportService;
        _consistenciaService = consistenciaService;

        _carpetaReportes = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BarracaRRHH",
            "Reportes");
        Directory.CreateDirectory(_carpetaReportes);

        RefrescarCommand = new AsyncCommand(RefrescarAsync);
        CambiarPeriodoCommand = new AsyncCommand(RefrescarAsync);
        ImportarPlantillasCommand = new AsyncCommand(ImportarPlantillasAsync);
        ImportarFuncionariosCommand = new AsyncCommand(ImportarFuncionariosAsync);
        ImportarObrasCommand = new AsyncCommand(ImportarObrasAsync);
        ImportarHorasCommand = new AsyncCommand(ImportarHorasAsync);
        ImportarPagosCommand = new AsyncCommand(ImportarPagosAsync);
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

    public string CarpetaReportes
    {
        get => _carpetaReportes;
        set
        {
            var nueva = string.IsNullOrWhiteSpace(value) ? _carpetaReportes : value.Trim();
            SetField(ref _carpetaReportes, nueva);
        }
    }

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
    public ObservableCollection<string> PeriodosDisponibles { get; } = new();

    public ICommand RefrescarCommand { get; }
    public ICommand CambiarPeriodoCommand { get; }
    public ICommand ImportarPlantillasCommand { get; }
    public ICommand ImportarFuncionariosCommand { get; }
    public ICommand ImportarObrasCommand { get; }
    public ICommand ImportarHorasCommand { get; }
    public ICommand ImportarPagosCommand { get; }
    public ICommand CalcularDistribucionCommand { get; }
    public ICommand GenerarReportesCommand { get; }

    public string RutaFuncionarios
    {
        get => _rutaFuncionarios;
        set => SetField(ref _rutaFuncionarios, value);
    }

    public string RutaObras
    {
        get => _rutaObras;
        set => SetField(ref _rutaObras, value);
    }

    public string RutaHoras
    {
        get => _rutaHoras;
        set => SetField(ref _rutaHoras, value);
    }

    public string RutaPagos
    {
        get => _rutaPagos;
        set => SetField(ref _rutaPagos, value);
    }

    private async Task RefrescarAsync()
    {
        try
        {
            var periodoNormalizado = NormalizarPeriodo(Periodo);
            await _periodoService.AbrirPeriodoAsync(periodoNormalizado, "admin");
            await CargarPeriodosAsync();
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

    private async Task CargarPeriodosAsync()
    {
        var periodos = await _periodoService.ObtenerPeriodosAsync();
        PeriodosDisponibles.Clear();
        foreach (var p in periodos.OrderByDescending(x => x.Codigo))
            PeriodosDisponibles.Add(p.Codigo);
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

    public Task CalcularDistribucionUiAsync() => CalcularDistribucionAsync();

    private async Task ImportarPlantillasAsync()
    {
        try
        {
            var periodoNormalizado = NormalizarPeriodo(Periodo);

            var f = ResolverRutaPlantilla(RutaFuncionarios, new[] { "FUNCIONARIO", "FUNCIONARIOS" });
            var o = ResolverRutaPlantilla(RutaObras, new[] { "OBRA", "OBRAS" });
            var h = ResolverRutaPlantilla(RutaHoras, new[] { "HORAS" });
            var p = ResolverRutaPlantilla(RutaPagos, new[] { "PAGO", "PAGOS" });

            RutaFuncionarios = f;
            RutaObras = o;
            RutaHoras = h;
            RutaPagos = p;

            await _excelImportService.ImportarFuncionariosAsync(f, "admin");
            await _excelImportService.ImportarObrasAsync(o, "admin");
            await _excelImportService.ImportarHorasAsync(h, periodoNormalizado, "admin");
            await _excelImportService.ImportarPagosAsync(p, periodoNormalizado, "admin");

            await RefrescarAsync();
            Status = "Plantillas importadas correctamente (Funcionarios, Obras, Horas, Pagos).";
        }
        catch (Exception ex)
        {
            Status = $"Error importando plantillas: {ex.Message}";
        }
    }

    private async Task ImportarFuncionariosAsync()
    {
        try
        {
            var ruta = ResolverRutaPlantilla(RutaFuncionarios, new[] { "FUNCIONARIO", "FUNCIONARIOS" });
            RutaFuncionarios = ruta;
            await _excelImportService.ImportarFuncionariosAsync(ruta, "admin");
            await RefrescarAsync();
            Status = "Plantilla de Funcionarios importada correctamente.";
        }
        catch (Exception ex)
        {
            Status = $"Error importando Funcionarios: {ex.Message}";
        }
    }

    private async Task ImportarObrasAsync()
    {
        try
        {
            var ruta = ResolverRutaPlantilla(RutaObras, new[] { "OBRA", "OBRAS" });
            RutaObras = ruta;
            await _excelImportService.ImportarObrasAsync(ruta, "admin");
            await RefrescarAsync();
            Status = "Plantilla de Obras importada correctamente.";
        }
        catch (Exception ex)
        {
            Status = $"Error importando Obras: {ex.Message}";
        }
    }

    private async Task ImportarHorasAsync()
    {
        try
        {
            var periodoNormalizado = NormalizarPeriodo(Periodo);
            var ruta = ResolverRutaPlantilla(RutaHoras, new[] { "HORAS" });
            RutaHoras = ruta;
            await _excelImportService.ImportarHorasAsync(ruta, periodoNormalizado, "admin");
            await RefrescarAsync();
            Status = "Plantilla de Horas importada correctamente.";
        }
        catch (Exception ex)
        {
            Status = $"Error importando Horas: {ex.Message}";
        }
    }

    private async Task ImportarPagosAsync()
    {
        try
        {
            var periodoNormalizado = NormalizarPeriodo(Periodo);
            var ruta = ResolverRutaPlantilla(RutaPagos, new[] { "PAGO", "PAGOS" });
            RutaPagos = ruta;
            await _excelImportService.ImportarPagosAsync(ruta, periodoNormalizado, "admin");
            await RefrescarAsync();
            Status = "Plantilla de Pagos importada correctamente.";
        }
        catch (Exception ex)
        {
            Status = $"Error importando Pagos: {ex.Message}";
        }
    }

    private static string ResolverRutaPlantilla(string rutaIngresada, string[] pistas)
    {
        if (!string.IsNullOrWhiteSpace(rutaIngresada) && File.Exists(rutaIngresada))
            return rutaIngresada;

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        if (!Directory.Exists(root))
            throw new InvalidOperationException($"No existe la carpeta para autodetección de plantillas: {root}");

        var files = Directory.EnumerateFiles(root, "*.xlsx", SearchOption.TopDirectoryOnly)
            .Where(x => pistas.Any(p => Path.GetFileName(x).ToUpperInvariant().Contains(p)))
            .Select(x => new FileInfo(x))
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .ToList();

        if (files.Count == 0)
            throw new InvalidOperationException($"No se encontró plantilla Excel para: {string.Join(", ", pistas)}.");

        return files[0].FullName;
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
                ReportesRecientes.Add(Path.GetFileName(ruta));

            Status = $"Reportes generados: {reportes.Length} archivos";
        }
        catch (Exception ex)
        {
            Status = $"Error generando reportes: {ex.Message}";
        }
    }

    public Task GenerarReportesUiAsync() => GenerarReportesAsync();

    public async Task<IReadOnlyList<ConsistenciaFuncionarioErrorDto>> ValidarConsistenciaAsync()
    {
        var periodoNormalizado = NormalizarPeriodo(Periodo);
        return await _consistenciaService.ValidarConsistenciaFuncionarioAsync(periodoNormalizado);
    }

    public async Task<IReadOnlyList<ConsistenciaDetalleRegistroDto>> ObtenerDetalleConsistenciaAsync(string tipo, int funcionarioId)
    {
        var periodoNormalizado = NormalizarPeriodo(Periodo);
        return await _consistenciaService.ObtenerDetalleErrorAsync(periodoNormalizado, tipo, funcionarioId);
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
