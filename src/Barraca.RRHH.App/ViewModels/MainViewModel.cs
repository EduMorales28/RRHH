using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.App.Windows;
using Barraca.RRHH.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace Barraca.RRHH.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IExcelImportService _excelImportService;
    private readonly IDistribucionService _distribucionService;
    private readonly IPeriodoService _periodoService;
    private readonly IReportService _reportService;
    private readonly IConsistenciaService _consistenciaService;
    private readonly BarracaDbContext _db;

    private string _periodo = "";
    private string _status = "Listo";
    private DashboardResumenDto _dashboard = new();
    private string _carpetaReportes = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BarracaRRHH",
        "Reportes");

    public MainViewModel(
        IDashboardService dashboardService,
        IExcelImportService excelImportService,
        IDistribucionService distribucionService,
        IPeriodoService periodoService,
        IReportService reportService,
        IConsistenciaService consistenciaService,
        BarracaDbContext db)
    {
        _dashboardService = dashboardService;
        _excelImportService = excelImportService;
        _distribucionService = distribucionService;
        _periodoService = periodoService;
        _reportService = reportService;
        _consistenciaService = consistenciaService;
        _db = db;

        ImportarFuncionariosCommand = new AsyncRelayCommand(() => ImportarAsync("FUNCIONARIOS"));
        ImportarObrasCommand = new AsyncRelayCommand(() => ImportarAsync("OBRAS"));
        ImportarHorasCommand = new AsyncRelayCommand(() => ImportarAsync("HORAS"));
        ImportarPagosCommand = new AsyncRelayCommand(() => ImportarAsync("PAGOS"));
        RefrescarDashboardCommand = new AsyncRelayCommand(RefrescarTodoAsync);
        CalcularDistribucionCommand = new AsyncRelayCommand(CalcularDistribucionAsync);
        CerrarPeriodoCommand = new AsyncRelayCommand(CerrarPeriodoAsync);
        ReabrirPeriodoCommand = new AsyncRelayCommand(ReabrirPeriodoAsync);
        GenerarTodosLosReportesCommand = new AsyncRelayCommand(GenerarTodosLosReportesAsync);
        CambiarPeriodoCommand = new AsyncRelayCommand(CambiarPeriodoAsync);

        _ = InicializarAsync();
    }

    public string Periodo
    {
        get => _periodo;
        set => SetProperty(ref _periodo, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string CarpetaReportes
    {
        get => _carpetaReportes;
        set => SetProperty(ref _carpetaReportes, value);
    }

    public DashboardResumenDto Dashboard
    {
        get => _dashboard;
        set => SetProperty(ref _dashboard, value);
    }

    public ObservableCollection<string> PeriodosDisponibles { get; } = new();
    public ObservableCollection<DistribucionLineaDto> DistribucionLineas { get; } = new();
    public ObservableCollection<SimpleRowViewModel> Funcionarios { get; } = new();
    public ObservableCollection<SimpleRowViewModel> Obras { get; } = new();
    public ObservableCollection<SimpleRowViewModel> Auditoria { get; } = new();
    public ObservableCollection<SimpleRowViewModel> Periodos { get; } = new();
    public ObservableCollection<SimpleRowViewModel> Incidencias { get; } = new();
    public ObservableCollection<SimpleRowViewModel> ReportesGenerados { get; } = new();

    public AsyncRelayCommand ImportarFuncionariosCommand { get; }
    public AsyncRelayCommand ImportarObrasCommand { get; }
    public AsyncRelayCommand ImportarHorasCommand { get; }
    public AsyncRelayCommand ImportarPagosCommand { get; }
    public AsyncRelayCommand RefrescarDashboardCommand { get; }
    public AsyncRelayCommand CalcularDistribucionCommand { get; }
    public AsyncRelayCommand CerrarPeriodoCommand { get; }
    public AsyncRelayCommand ReabrirPeriodoCommand { get; }
    public AsyncRelayCommand GenerarTodosLosReportesCommand { get; }
    public AsyncRelayCommand CambiarPeriodoCommand { get; }

    // command for deletion removed (handled in UI code-behind)

    // expose method to reload tables from UI
    public async Task CargarTablasAsyncPublic()
    {
        await CargarTablasAsync();
    }

    // EliminarDistribucionAsync se realiza desde el code-behind (MainWindow) usando ICrudService desde el ServiceProvider.

    private async Task InicializarAsync()
    {
        try
        {
            Directory.CreateDirectory(CarpetaReportes);
            await _db.Database.EnsureCreatedAsync();
            await CargarPeriodosDisponiblesAsync();

            // If no period selected yet, pick the most recent or create one for current month
            if (string.IsNullOrEmpty(Periodo))
            {
                Periodo = PeriodosDisponibles.FirstOrDefault()
                    ?? DateTime.Now.ToString("yyyy-MM");
            }

            await _periodoService.AbrirPeriodoAsync(Periodo, "admin");
            await CargarPeriodosDisponiblesAsync();
            await CargarDashboardAsync();
            await CargarTablasAsync();
        }
        catch (Exception ex)
        {
            Status = $"Error al iniciar: {ex.Message}";
        }
    }

    private async Task CargarPeriodosDisponiblesAsync()
    {
        var periodos = await _db.Periodos
            .OrderByDescending(x => x.Codigo)
            .Select(x => x.Codigo)
            .ToListAsync();

        PeriodosDisponibles.Clear();
        foreach (var p in periodos)
            PeriodosDisponibles.Add(p);
    }

    private async Task CambiarPeriodoAsync()
    {
        try
        {
            var periodoNormalizado = NormalizarPeriodoOError(Periodo);

            await _periodoService.AbrirPeriodoAsync(periodoNormalizado, "admin");
            Periodo = periodoNormalizado;
            await CargarPeriodosDisponiblesAsync();
            await CargarDashboardAsync();
            DistribucionLineas.Clear();
            ReportesGenerados.Clear();
            await CargarTablasAsync();
            Status = $"Período activo: {periodoNormalizado}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Período inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            Status = $"Error cambiando período: {ex.Message}";
        }
    }

    private async Task RefrescarTodoAsync()
    {
        await CargarDashboardAsync();
        DistribucionLineas.Clear();
        ReportesGenerados.Clear();
        await CargarTablasAsync();
    }

    private async Task ImportarAsync(string tipo)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var periodoActivo = await ObtenerPeriodoActivoParaOperacionAsync(tipo);

            string resultado = tipo switch
            {
                "FUNCIONARIOS" => await _excelImportService.ImportarFuncionariosAsync(dialog.FileName, "admin"),
                "OBRAS" => await _excelImportService.ImportarObrasAsync(dialog.FileName, "admin"),
                "HORAS" => await _excelImportService.ImportarHorasAsync(dialog.FileName, periodoActivo, "admin"),
                "PAGOS" => await _excelImportService.ImportarPagosAsync(dialog.FileName, periodoActivo, "admin"),
                _ => "Tipo de importación no soportado"
            };

            Status = resultado;
            await CargarDashboardAsync();
            await CargarTablasAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, $"Error importando {tipo}", MessageBoxButton.OK, MessageBoxImage.Error);
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task CargarDashboardAsync()
    {
        Dashboard = await _dashboardService.ObtenerResumenAsync(Periodo);
    }

    private async Task CalcularDistribucionAsync()
    {
        try
        {
            var periodoActivo = await ObtenerPeriodoActivoParaOperacionAsync("CALCULAR DISTRIBUCION");
            if (!await ValidarYCorregirConsistenciaAntesDeContinuarAsync(periodoActivo, "calcular la distribución"))
                return;

            var lineas = await _distribucionService.CalcularDistribucionAsync(periodoActivo, "admin", true);
            DistribucionLineas.Clear();
            foreach (var item in lineas)
                DistribucionLineas.Add(item);

            Status = $"Distribución recalculada: {lineas.Count} líneas";
            await CargarDashboardAsync();
            await CargarTablasAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error al calcular distribución", MessageBoxButton.OK, MessageBoxImage.Error);
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task CerrarPeriodoAsync()
    {
        try
        {
            var periodoActivo = await ObtenerPeriodoActivoParaOperacionAsync("CERRAR");
            await _periodoService.CerrarPeriodoAsync(periodoActivo, "admin");
            Status = $"Período {periodoActivo} cerrado";
            await CargarPeriodosDisponiblesAsync();
            await CargarTablasAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error cerrando período", MessageBoxButton.OK, MessageBoxImage.Error);
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task ReabrirPeriodoAsync()
    {
        try
        {
            var periodoActivo = await ObtenerPeriodoActivoParaOperacionAsync("REABRIR");
            await _periodoService.ReabrirPeriodoAsync(periodoActivo, "admin");
            Status = $"Período {periodoActivo} reabierto";
            await CargarPeriodosDisponiblesAsync();
            await CargarTablasAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error reabriendo período", MessageBoxButton.OK, MessageBoxImage.Error);
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task<string> ObtenerPeriodoActivoParaOperacionAsync(string tipoOperacion)
    {
        if (!string.IsNullOrWhiteSpace(Periodo))
            return NormalizarPeriodoOError(Periodo);

        var sugerido = PeriodosDisponibles.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sugerido))
            sugerido = DateTime.Now.ToString("yyyy-MM");

        var confirm = MessageBox.Show(
            $"No hay período seleccionado. Se usará '{sugerido}' para {tipoOperacion}. ¿Continuar?",
            "Período requerido",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            throw new InvalidOperationException("Debe seleccionar un período antes de continuar.");

        Periodo = sugerido;
        await _periodoService.AbrirPeriodoAsync(Periodo, "admin");
        await CargarPeriodosDisponiblesAsync();
        return Periodo;
    }

    private static string NormalizarPeriodoOError(string? value)
    {
        var periodo = (value ?? string.Empty).Trim();
        if (Regex.IsMatch(periodo, "^\\d{4}-(0[1-9]|1[0-2])$"))
            return periodo;

        throw new InvalidOperationException("Formato de período inválido. Use AAAA-MM (por ejemplo, 2026-04).");
    }

    private async Task GenerarTodosLosReportesAsync()
    {
        try
        {
            var periodoActivo = await ObtenerPeriodoActivoParaOperacionAsync("GENERAR REPORTES");
            if (!await ValidarYCorregirConsistenciaAntesDeContinuarAsync(periodoActivo, "generar los reportes"))
                return;

            Directory.CreateDirectory(CarpetaReportes);
            var rutas = new List<string>
            {
                await _reportService.GenerarAdelantosAsync(periodoActivo, CarpetaReportes, "admin"),
                await _reportService.GenerarDistribucionObrasAsync(periodoActivo, CarpetaReportes, "admin"),
                await _reportService.GenerarRedPagosAsync(periodoActivo, CarpetaReportes, "admin"),
                await _reportService.GenerarNAAsync(periodoActivo, CarpetaReportes, "admin"),
                await _reportService.GenerarRetencionesAsync(periodoActivo, CarpetaReportes, "admin"),
                await _reportService.GenerarConsolidadoGeneralAsync(periodoActivo, CarpetaReportes, "admin")
            };

            ReportesGenerados.Clear();
            foreach (var ruta in rutas)
                ReportesGenerados.Add(new SimpleRowViewModel { Codigo = Path.GetFileName(ruta), Descripcion = ruta, Extra = "PDF generado" });

            Status = $"Reportes generados en {CarpetaReportes}";
            await CargarTablasAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error generando reportes", MessageBoxButton.OK, MessageBoxImage.Error);
            Status = $"Error reportes: {ex.Message}";
        }
    }

    private async Task<bool> ValidarYCorregirConsistenciaAntesDeContinuarAsync(string periodo, string operacion)
    {
        var errores = await _consistenciaService.ValidarConsistenciaFuncionarioAsync(periodo);
        if (errores.Count == 0)
            return true;

        var ventana = new ErroresConsistenciaWindow(_consistenciaService, periodo, "admin", operacion)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        var resultado = ventana.ShowDialog();
        if (resultado != true)
        {
            Status = "Operación cancelada por inconsistencias de horas/pagos.";
            return false;
        }

        var pendientes = await _consistenciaService.ValidarConsistenciaFuncionarioAsync(periodo);
        if (pendientes.Count > 0)
        {
            Status = $"No se puede continuar. Quedan {pendientes.Count} inconsistencias por corregir.";
            return false;
        }

        await CargarDashboardAsync();
        await CargarTablasAsync();
        return true;
    }

    private async Task CargarTablasAsync()
    {
        Funcionarios.Clear();
        foreach (var item in await _db.Funcionarios.OrderBy(x => x.NumeroFuncionario).Take(250)
                     .Select(x => new SimpleRowViewModel { Codigo = x.NumeroFuncionario, Descripcion = x.Nombre, Extra = x.Activo ? x.Categoria : $"INACTIVO - {x.Categoria}" }).ToListAsync())
            Funcionarios.Add(item);

        Obras.Clear();
        foreach (var item in await _db.Obras.OrderBy(x => x.NumeroObra).Take(250)
                     .Select(x => new SimpleRowViewModel { Codigo = x.NumeroObra, Descripcion = x.Nombre, Extra = x.Activa ? x.TipoObraOriginal : $"INACTIVA - {x.TipoObraOriginal}" }).ToListAsync())
            Obras.Add(item);

        // Load recent audit events but hide low-value insertion/deletion noise
        Auditoria.Clear();
        var recentAudit = await _db.AuditoriaEventos
            .Where(x => x.Accion != "Ingreso" && x.Accion != "Eliminar")
            .OrderByDescending(x => x.FechaHora)
            .Take(120)
            .Select(x => new SimpleRowViewModel { Id = x.Id, Codigo = x.FechaHora.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), Descripcion = $"{x.Modulo} - {x.Accion}", Extra = x.Detalle })
            .ToListAsync();

        foreach (var item in recentAudit)
            Auditoria.Add(item);

        Periodos.Clear();
        foreach (var item in await _db.Periodos.OrderByDescending(x => x.Codigo).Take(36)
                     .Select(x => new SimpleRowViewModel { Codigo = x.Codigo, Descripcion = x.Estado.ToString(), Extra = x.FechaCierre.HasValue ? x.FechaCierre.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "Abierto" }).ToListAsync())
            Periodos.Add(item);

        Incidencias.Clear();
        foreach (var item in await _db.IncidenciasImportacion.OrderByDescending(x => x.FechaHora).Take(100)
                     .Select(x => new SimpleRowViewModel { Codigo = x.TipoArchivo, Descripcion = x.CodigoReferencia, Extra = x.Descripcion }).ToListAsync())
            Incidencias.Add(item);

        if (ReportesGenerados.Count == 0 && Directory.Exists(CarpetaReportes))
        {
            foreach (var file in Directory.GetFiles(CarpetaReportes, "*.pdf").OrderByDescending(File.GetCreationTime).Take(20))
                ReportesGenerados.Add(new SimpleRowViewModel { Codigo = Path.GetFileName(file), Descripcion = file, Extra = "PDF" });
        }

        // Always reload distribution lines for the selected period
        if (DistribucionLineas.Count == 0)
        {
            var periodoId = await _db.Periodos.Where(x => x.Codigo == Periodo).Select(x => (int?)x.Id).FirstOrDefaultAsync();
            if (periodoId.HasValue)
            {
                var existentes = await _db.DistribucionesCosto.Include(x => x.Obra)
                    .Where(x => x.PeriodoId == periodoId.Value)
                    .OrderBy(x => x.TipoObra).ThenBy(x => x.Obra!.NumeroObra).ThenBy(x => x.Categoria).Take(500).ToListAsync();

                DistribucionLineas.Clear();
                foreach (var item in existentes)
                {
                    DistribucionLineas.Add(new DistribucionLineaDto
                    {
                        TipoObra = item.TipoObra,
                        ObraId = item.ObraId,
                        NumeroObra = item.Obra?.NumeroObra ?? string.Empty,
                        NombreObra = item.Obra?.Nombre ?? string.Empty,
                        Categoria = item.Categoria,
                        HorasLinea = item.HorasLinea,
                        HorasTotalesTipoObra = item.HorasTotalesTipoObra,
                        CostoTotalTipoObra = item.CostoTotalTipoObra,
                        PorcentajeParticipacion = item.PorcentajeParticipacion,
                        MontoLinea = item.MontoLinea,
                        ValorHora = item.ValorHora,
                        Jornales = item.Jornales
                    });
                }
            }
        }
    }
}

public class SimpleRowViewModel
{
    public int? Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Extra { get; set; } = string.Empty;
}
