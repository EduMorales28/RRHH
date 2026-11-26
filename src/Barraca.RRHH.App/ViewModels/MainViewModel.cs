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

public partial class MainViewModel : ViewModelBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IExcelImportService _excelImportService;
    private readonly IDistribucionService _distribucionService;
    private readonly IPeriodoService _periodoService;
    private readonly IReportService _reportService;
    private readonly IConsistenciaService _consistenciaService;
    private readonly IRevisionPlanillaService _revisionPlanillaService;
    private readonly BarracaDbContext _db;

    private string _periodo = "";
    private string _status = "Listo";
    private DashboardResumenDto _dashboard = new();
    private string _carpetaReportes = Path.Combine(App.UserDataRoot, "Reportes");

    public MainViewModel(
        IDashboardService dashboardService,
        IExcelImportService excelImportService,
        IDistribucionService distribucionService,
        IPeriodoService periodoService,
        IReportService reportService,
        IConsistenciaService consistenciaService,
        IRevisionPlanillaService revisionPlanillaService,
        ICargaMensualService cargaMensualService,
        IPlanillaAbacoService planillaAbacoService,
        IPlanillaHorasService planillaHorasService,
        IDistribucionBpsService distribucionBpsService,
        BarracaDbContext db)
    {
        _dashboardService = dashboardService;
        _excelImportService = excelImportService;
        _distribucionService = distribucionService;
        _periodoService = periodoService;
        _reportService = reportService;
        _consistenciaService = consistenciaService;
        _revisionPlanillaService = revisionPlanillaService;
        _db = db;
        InicializarCarga(cargaMensualService, planillaAbacoService, planillaHorasService);
        InicializarBps(distribucionBpsService);

        ImportarFuncionariosCommand = new AsyncRelayCommand(() => ImportarAsync("FUNCIONARIOS"));
        ImportarObrasCommand = new AsyncRelayCommand(() => ImportarAsync("OBRAS"));
        ImportarHorasCommand = new AsyncRelayCommand(() => ImportarAsync("HORAS"));
        ImportarPagosCommand = new AsyncRelayCommand(() => ImportarAsync("PAGOS"));
        RefrescarDashboardCommand = new AsyncRelayCommand(RefrescarTodoAsync);
        CalcularDistribucionCommand = new AsyncRelayCommand(CalcularDistribucionAsync);
        CerrarPeriodoCommand = new AsyncRelayCommand(CerrarPeriodoAsync);
        ReabrirPeriodoCommand = new AsyncRelayCommand(ReabrirPeriodoAsync);
        GenerarTodosLosReportesCommand = new AsyncRelayCommand(GenerarTodosLosReportesAsync);
        GenerarReportesSoloPagosCommand = new AsyncRelayCommand(GenerarReportesSoloPagosAsync);
        GenerarPlanillaPagosCommand = new AsyncRelayCommand(GenerarPlanillaPagosAsync);
        GenerarPorcentajeHorasCommand = new AsyncRelayCommand(GenerarPorcentajeHorasAsync);
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
    public AsyncRelayCommand GenerarReportesSoloPagosCommand { get; }
    public AsyncRelayCommand GenerarPlanillaPagosCommand { get; }
    public AsyncRelayCommand GenerarPorcentajeHorasCommand { get; }
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

            // Se muestra el último período sin cambiar su estado (abierto o cerrado).
            var inicial = PeriodosDisponibles.FirstOrDefault();
            if (inicial is null)
            {
                inicial = DateTime.Now.ToString("yyyy-MM");
                await _cargaMensualService.CrearPeriodoAsync(inicial, null, "admin");
            }

            await SeleccionarPeriodoAsync(inicial);
            await CargarEdicionAsync();
            Status = $"Período en pantalla: {EstadoPeriodoTexto}";
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

            if (!await RevisarPlanillaAntesDeImportarAsync(tipo, dialog.FileName, periodoActivo))
            {
                Status = $"Importación de {tipo} cancelada para corregir la planilla.";
                return;
            }

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
            await CargarEdicionAsync();
        }
        catch (OperationCanceledException)
        {
            Status = "Operación cancelada.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, $"Error importando {tipo}", MessageBoxButton.OK, MessageBoxImage.Error);
            Status = $"Error: {ex.Message}";
        }
    }

    // Muestra los problemas de la planilla y deja decidir si importar igual. Devuelve true si se puede importar.
    private async Task<bool> RevisarPlanillaAntesDeImportarAsync(string tipo, string archivo, string periodo)
    {
        IReadOnlyList<ProblemaPlanillaDto> problemas = tipo switch
        {
            "FUNCIONARIOS" => await _revisionPlanillaService.RevisarFuncionariosAsync(archivo),
            "HORAS" => await _revisionPlanillaService.RevisarHorasAsync(archivo, periodo),
            "PAGOS" => await _revisionPlanillaService.RevisarPagosAsync(archivo, periodo),
            _ => Array.Empty<ProblemaPlanillaDto>()
        };

        if (problemas.Count == 0)
            return true;

        var ventana = new RevisionPlanillaWindow(tipo, Path.GetFileName(archivo), problemas)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        return ventana.ShowDialog() == true;
    }

    private async Task CargarDashboardAsync()
    {
        Dashboard = await _dashboardService.ObtenerResumenAsync(PeriodoCargado);
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
        catch (OperationCanceledException)
        {
            Status = "Operación cancelada.";
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
            if (MessageBox.Show($"¿Cerrar el período {periodoActivo}?\n\nUna vez cerrado no se pueden modificar sus horas, pagos ni distribución (los reportes se pueden seguir generando).",
                    "Cerrar período", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            await _periodoService.CerrarPeriodoAsync(periodoActivo, "admin");
            Status = $"Período {periodoActivo} cerrado";
            await CargarTablasAsync();
            await CargarMesAsync();
        }
        catch (OperationCanceledException)
        {
            Status = "Operación cancelada.";
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
            if (MessageBox.Show($"¿Reabrir el período {periodoActivo} para corregirlo?",
                    "Reabrir período", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            await _periodoService.ReabrirPeriodoAsync(periodoActivo, "admin");
            Status = $"Período {periodoActivo} reabierto";
            await CargarTablasAsync();
            await CargarMesAsync();
        }
        catch (OperationCanceledException)
        {
            Status = "Operación cancelada.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error reabriendo período", MessageBoxButton.OK, MessageBoxImage.Error);
            Status = $"Error: {ex.Message}";
        }
    }

    // Las operaciones siempre trabajan sobre el período que está en pantalla.
    private async Task<string> ObtenerPeriodoActivoParaOperacionAsync(string tipoOperacion)
    {
        if (!await ConfirmarCambiosPendientesAsync())
            throw new OperationCanceledException();

        if (string.IsNullOrWhiteSpace(PeriodoCargado))
            throw new InvalidOperationException("Primero elija un período o cree uno con 'Nuevo mes'.");

        if (!string.Equals((Periodo ?? string.Empty).Trim(), PeriodoCargado, StringComparison.OrdinalIgnoreCase))
            Periodo = PeriodoCargado;

        return PeriodoCargado;
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

            // La distribución se recalcula con los datos actuales; en un período cerrado se usa la guardada.
            if (!PeriodoCerrado)
            {
                var lineas = await _distribucionService.CalcularDistribucionAsync(periodoActivo, "admin", true);
                DistribucionLineas.Clear();
                foreach (var item in lineas)
                    DistribucionLineas.Add(item);
                await CargarDashboardAsync();
            }

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
        catch (OperationCanceledException)
        {
            Status = "Operación cancelada.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error generando reportes", MessageBoxButton.OK, MessageBoxImage.Error);
            Status = $"Error reportes: {ex.Message}";
        }
    }

    // Reportes que solo dependen de PAGOS: no requieren HORAS ni distribución, por eso no se valida consistencia horas/pagos.
    private async Task GenerarReportesSoloPagosAsync()
    {
        try
        {
            var periodoActivo = await ObtenerPeriodoActivoParaOperacionAsync("GENERAR REPORTES DE PAGOS");

            var hayPagos = await _db.PagosMensuales.AnyAsync(x => x.Periodo!.Codigo == periodoActivo
                && (x.Adelanto > 0 || x.Liquido > 0 || x.Retencion > 0));
            if (!hayPagos)
            {
                MessageBox.Show(
                    $"No hay pagos con montos para el período {periodoActivo}. Complete los montos en la pestaña 'Pagos del mes' y vuelva a intentar.",
                    "Sin pagos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Status = $"Sin pagos para {periodoActivo}.";
                return;
            }

            Directory.CreateDirectory(CarpetaReportes);
            var rutas = new List<string>
            {
                await _reportService.GenerarRetencionesAsync(periodoActivo, CarpetaReportes, "admin"),
                await _reportService.GenerarConsolidadoGeneralAsync(periodoActivo, CarpetaReportes, "admin"),
                await _reportService.GenerarRedPagosAsync(periodoActivo, CarpetaReportes, "admin"),
                await _reportService.GenerarNAAsync(periodoActivo, CarpetaReportes, "admin")
            };

            ReportesGenerados.Clear();
            foreach (var ruta in rutas)
                ReportesGenerados.Add(new SimpleRowViewModel { Codigo = Path.GetFileName(ruta), Descripcion = ruta, Extra = "PDF generado" });

            Status = $"Reportes de pagos generados en {CarpetaReportes}";
            await CargarTablasAsync();
        }
        catch (OperationCanceledException)
        {
            Status = "Operación cancelada.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error generando reportes de pagos", MessageBoxButton.OK, MessageBoxImage.Error);
            Status = $"Error reportes: {ex.Message}";
        }
    }

    private async Task GenerarPorcentajeHorasAsync()
    {
        try
        {
            var periodoActivo = await ObtenerPeriodoActivoParaOperacionAsync("GENERAR PORCENTAJE HORAS");
            Directory.CreateDirectory(CarpetaReportes);
            var ruta = await _reportService.GenerarPorcentajeHorasExcelAsync(periodoActivo, CarpetaReportes, "admin");

            ReportesGenerados.Insert(0, new SimpleRowViewModel
            {
                Codigo = Path.GetFileName(ruta),
                Descripcion = ruta,
                Extra = "Excel generado"
            });

            Status = $"Porcentaje de horas generado: {Path.GetFileName(ruta)}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
            Status = "Operación cancelada.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error al generar reporte", MessageBoxButton.OK, MessageBoxImage.Error);
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task GenerarPlanillaPagosAsync()
    {
        try
        {
            var periodoActivo = await ObtenerPeriodoActivoParaOperacionAsync("GENERAR PLANILLA PAGOS");
            Directory.CreateDirectory(CarpetaReportes);
            var ruta = await _reportService.GenerarPlanillaPagosExcelAsync(periodoActivo, CarpetaReportes, "admin");

            ReportesGenerados.Insert(0, new SimpleRowViewModel
            {
                Codigo = Path.GetFileName(ruta),
                Descripcion = ruta,
                Extra = "Excel generado"
            });

            Status = $"Planilla de pagos generada: {Path.GetFileName(ruta)}";

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
            Status = "Operación cancelada.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error al generar planilla", MessageBoxButton.OK, MessageBoxImage.Error);
            Status = $"Error: {ex.Message}";
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
        await CargarMesAsync();
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

        var periodoId = await _db.Periodos.Where(x => x.Codigo == PeriodoCargado).Select(x => (int?)x.Id).FirstOrDefaultAsync();
        DistribucionLineas.Clear();
        if (periodoId.HasValue)
        {
            var existentes = await _db.DistribucionesCosto.Include(x => x.Obra)
                .Where(x => x.PeriodoId == periodoId.Value)
                .OrderBy(x => x.TipoObra).ThenBy(x => x.Obra!.NumeroObra).ThenBy(x => x.Categoria).Take(500).ToListAsync();

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

public class SimpleRowViewModel
{
    public int? Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Extra { get; set; } = string.Empty;
}
