using System.Globalization;
using System.Windows;
using Barraca.RRHH.App.Helpers;
using Barraca.RRHH.App.ViewModels.Carga;
using Barraca.RRHH.App.Windows;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Infrastructure.Helpers;

namespace Barraca.RRHH.App.ViewModels;

// Carga del mes dentro del programa: pagos, horas, funcionarios y obras.
public partial class MainViewModel
{
    private ICargaMensualService _cargaMensualService = default!;
    private IPlanillaAbacoService _planillaAbacoService = default!;
    private IPlanillaHorasService _planillaHorasService = default!;
    private string _periodoCargado = string.Empty;
    private bool _periodoCerrado;

    public PagosMesViewModel Pagos { get; private set; } = default!;
    public HorasMesViewModel Horas { get; private set; } = default!;
    public FuncionariosEdicionViewModel FuncionariosEdicion { get; private set; } = default!;
    public ObrasEdicionViewModel ObrasEdicion { get; private set; } = default!;

    public AsyncRelayCommand NuevoMesCommand { get; private set; } = default!;
    public AsyncRelayCommand CargarAbacoCommand { get; private set; } = default!;
    public AsyncRelayCommand CargarHorasCommand { get; private set; } = default!;
    public AsyncRelayCommand RevisarCrucesCommand { get; private set; } = default!;
    public RelayCommand QuitarPagosSinMontosCommand { get; private set; } = default!;
    public RelayCommand QuitarHorasSinHorasCommand { get; private set; } = default!;

    // Período cuyos datos se ven y se editan en pantalla.
    public string PeriodoCargado
    {
        get => _periodoCargado;
        private set { if (SetProperty(ref _periodoCargado, value)) OnPropertyChanged(nameof(EstadoPeriodoTexto)); }
    }

    public bool PeriodoCerrado
    {
        get => _periodoCerrado;
        private set { if (SetProperty(ref _periodoCerrado, value)) OnPropertyChanged(nameof(EstadoPeriodoTexto)); }
    }

    public string EstadoPeriodoTexto =>
        string.IsNullOrEmpty(PeriodoCargado) ? "Sin período"
        : PeriodoCerrado ? $"{PeriodoCargado} · CERRADO (solo lectura)"
        : $"{PeriodoCargado} · abierto";

    public bool HayCambiosSinGuardar =>
        Pagos.HayCambios || Horas.HayCambios || FuncionariosEdicion.HayCambios || ObrasEdicion.HayCambios || BpsHayCambios;

    private void InicializarCarga(ICargaMensualService cargaMensualService, IPlanillaAbacoService planillaAbacoService, IPlanillaHorasService planillaHorasService)
    {
        _cargaMensualService = cargaMensualService;
        _planillaAbacoService = planillaAbacoService;
        _planillaHorasService = planillaHorasService;
        CargarAbacoCommand = new AsyncRelayCommand(CargarPlanillaAbacoAsync);
        CargarHorasCommand = new AsyncRelayCommand(CargarArchivoHorasAsync);
        Pagos = new PagosMesViewModel(cargaMensualService);
        Horas = new HorasMesViewModel(cargaMensualService);
        FuncionariosEdicion = new FuncionariosEdicionViewModel(cargaMensualService);
        ObrasEdicion = new ObrasEdicionViewModel(cargaMensualService);

        NuevoMesCommand = new AsyncRelayCommand(NuevoMesAsync);
        RevisarCrucesCommand = new AsyncRelayCommand(RevisarCrucesAsync);
        QuitarPagosSinMontosCommand = new RelayCommand(() => Pagos.QuitarFilasSinMontos());
        QuitarHorasSinHorasCommand = new RelayCommand(() => Horas.QuitarFilasSinHoras());
    }

    // ---------------------------------------------------------------- Carga de datos en pantalla

    private async Task CargarEdicionAsync()
    {
        await FuncionariosEdicion.CargarAsync();
        await ObrasEdicion.CargarAsync();
        CatalogoMaestros.Actualizar(FuncionariosEdicion.UltimosCargados, ObrasEdicion.UltimasCargadas);
        await CargarMesAsync();
    }

    private async Task CargarMesAsync()
    {
        var info = string.IsNullOrEmpty(PeriodoCargado) ? null : await _cargaMensualService.ObtenerPeriodoAsync(PeriodoCargado);
        PeriodoCerrado = info?.Cerrado ?? false;
        Pagos.SoloLectura = Horas.SoloLectura = info is null || info.Cerrado;
        await Pagos.CargarAsync(PeriodoCargado);
        await Horas.CargarAsync(PeriodoCargado);
        await CargarBpsAsync();
    }

    private async Task SeleccionarPeriodoAsync(string codigo)
    {
        PeriodoCargado = codigo;
        Periodo = codigo;
        await CargarPeriodosDisponiblesAsync();
        Periodo = codigo;
        await CargarDashboardAsync();
        DistribucionLineas.Clear();
        ReportesGenerados.Clear();
        await CargarTablasAsync();
        await CargarMesAsync();
    }

    private async Task CambiarPeriodoAsync()
    {
        try
        {
            if (!await ConfirmarCambiosPendientesAsync())
            {
                Periodo = PeriodoCargado;
                return;
            }

            var codigo = NormalizarPeriodoOError(Periodo);
            var info = await _cargaMensualService.ObtenerPeriodoAsync(codigo);
            if (info is null)
            {
                if (!await CrearMesConVentanaAsync(codigo))
                    Periodo = PeriodoCargado;
                return;
            }

            await SeleccionarPeriodoAsync(codigo);
            Status = $"Período en pantalla: {EstadoPeriodoTexto}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cambiar período", MessageBoxButton.OK, MessageBoxImage.Warning);
            Periodo = PeriodoCargado;
        }
    }

    private async Task NuevoMesAsync()
    {
        if (!await ConfirmarCambiosPendientesAsync())
            return;

        var ultimo = PeriodosDisponibles.OrderByDescending(x => x).FirstOrDefault();
        var sugerido = ultimo is not null && DateTime.TryParseExact(ultimo + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha.AddMonths(1).ToString("yyyy-MM")
            : DateTime.Now.ToString("yyyy-MM");

        await CrearMesConVentanaAsync(sugerido);
    }

    private async Task<bool> CrearMesConVentanaAsync(string sugerido)
    {
        var conDatos = new List<string>();
        foreach (var p in PeriodosDisponibles.OrderByDescending(x => x))
        {
            var info = await _cargaMensualService.ObtenerPeriodoAsync(p);
            if (info is not null && (info.Pagos > 0 || info.Horas > 0))
                conDatos.Add(p);
        }

        var ventana = new NuevoMesWindow(sugerido, conDatos, conDatos.FirstOrDefault())
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (ventana.ShowDialog() != true)
            return false;

        try
        {
            var resultado = await _cargaMensualService.CrearPeriodoAsync(ventana.Periodo, ventana.CopiarDe, "admin");
            await SeleccionarPeriodoAsync(ventana.Periodo);
            Status = resultado;
            MessageBox.Show(resultado + "\n\nComplete los montos en la pestaña 'Pagos del mes' y las horas en 'Horas del mes'.",
                "Nuevo mes", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Nuevo mes", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    // ---------------------------------------------------------------- Guardar

    public Task GuardarPagosAsync() => GuardarAsync("pagos", () => Pagos.GuardarAsync(PeriodoCargado, "admin"), despuesMes: true);
    public Task GuardarHorasAsync() => GuardarAsync("horas", () => Horas.GuardarAsync(PeriodoCargado, "admin"), despuesMes: true);
    public Task GuardarFuncionariosAsync() => GuardarAsync("funcionarios", () => FuncionariosEdicion.GuardarAsync("admin"), despuesMes: false);
    public Task GuardarObrasAsync() => GuardarAsync("obras", () => ObrasEdicion.GuardarAsync("admin"), despuesMes: false);

    private async Task GuardarAsync(string que, Func<Task<string>> guardar, bool despuesMes)
    {
        try
        {
            Status = await guardar();
            await DespuesDeGuardarAsync(despuesMes);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, $"No se guardaron los {que}", MessageBoxButton.OK, MessageBoxImage.Warning);
            Status = $"No se guardaron los {que}.";
        }
    }

    private async Task DespuesDeGuardarAsync(bool cambioMes)
    {
        if (cambioMes)
        {
            await CargarDashboardAsync();
            await CargarTablasAsync();
            return;
        }

        // Cambiaron funcionarios u obras: se actualizan nombres y avisos en las tablas del mes.
        CatalogoMaestros.Actualizar(FuncionariosEdicion.UltimosCargados, ObrasEdicion.UltimasCargadas);
        Pagos.RecalcularTodas();
        Horas.RecalcularTodas();
        await CargarDashboardAsync();
    }

    public async Task RecargarTodoAsync()
    {
        await CargarPeriodosDisponiblesAsync();
        if (!PeriodosDisponibles.Contains(PeriodoCargado))
            PeriodoCargado = PeriodosDisponibles.FirstOrDefault() ?? string.Empty;
        Periodo = PeriodoCargado;
        await CargarDashboardAsync();
        await CargarTablasAsync();
        await CargarEdicionAsync();
    }

    public async Task DescartarCambiosAsync()
    {
        await CargarEdicionAsync();
        Status = "Cambios descartados.";
    }

    // Antes de importar, cambiar de período o generar reportes: los cambios en pantalla se guardan o se descartan.
    public async Task<bool> ConfirmarCambiosPendientesAsync()
    {
        if (!HayCambiosSinGuardar)
            return true;

        var tablas = new List<string>();
        if (Pagos.HayCambios) tablas.Add("Pagos del mes");
        if (Horas.HayCambios) tablas.Add("Horas del mes");
        if (FuncionariosEdicion.HayCambios) tablas.Add("Funcionarios");
        if (ObrasEdicion.HayCambios) tablas.Add("Obras");
        if (BpsHayCambios) tablas.Add("BPS por obra");

        var respuesta = MessageBox.Show(
            $"Hay cambios sin guardar en: {string.Join(", ", tablas)}.\n\n¿Guardarlos ahora?\n\nSí: guardar   ·   No: descartarlos   ·   Cancelar: volver",
            "Cambios sin guardar", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (respuesta == MessageBoxResult.Cancel)
            return false;

        if (respuesta == MessageBoxResult.No)
        {
            await CargarEdicionAsync();
            return true;
        }

        try
        {
            // Primero maestros, para que los pagos y horas encuentren funcionarios y obras nuevos.
            if (FuncionariosEdicion.HayCambios) Status = await FuncionariosEdicion.GuardarAsync("admin");
            if (ObrasEdicion.HayCambios) Status = await ObrasEdicion.GuardarAsync("admin");
            CatalogoMaestros.Actualizar(FuncionariosEdicion.UltimosCargados, ObrasEdicion.UltimasCargadas);
            Pagos.RecalcularTodas();
            Horas.RecalcularTodas();
            if (Pagos.HayCambios) Status = await Pagos.GuardarAsync(PeriodoCargado, "admin");
            if (Horas.HayCambios) Status = await Horas.GuardarAsync(PeriodoCargado, "admin");
            if (BpsHayCambios) Status = await GuardarBpsInternoAsync();
            await CargarDashboardAsync();
            await CargarTablasAsync();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "No se pudo guardar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    // ---------------------------------------------------------------- Planilla de ábaco

    private async Task CargarPlanillaAbacoAsync()
    {
        if (Pagos.SoloLectura)
        {
            MessageBox.Show("El período en pantalla está cerrado o no existe. Elija o cree el mes a cargar.", "Cargar planilla de ábaco",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Puede crear funcionarios: los cambios pendientes en Funcionarios u Obras se resuelven antes.
        if ((FuncionariosEdicion.HayCambios || ObrasEdicion.HayCambios) && !await ConfirmarCambiosPendientesAsync())
            return;

        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Elija la planilla de Construcción y la de Industria y Comercio (Ctrl+clic para elegir las dos)",
            Filter = "Planilla de ábaco (PDF)|*.pdf",
            Multiselect = true
        };
        if (dialogo.ShowDialog() != true)
            return;

        List<Application.DTOs.PlanillaAbacoDto> planillas;
        try
        {
            planillas = dialogo.FileNames.Select(_planillaAbacoService.Leer).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cargar planilla de ábaco", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (planillas.GroupBy(p => p.TipoObra).FirstOrDefault(g => g.Count() > 1) is { } repetida)
        {
            MessageBox.Show($"Eligió más de una planilla de {repetida.First().Empresa}. Elija una de Construcción y/o una de Industria y Comercio.",
                "Cargar planilla de ábaco", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sugerencias = await _planillaAbacoService.EmparejarAsync(planillas);
        var vm = new CargaAbacoViewModel(PeriodoCargado, planillas, sugerencias);
        var ventana = new CargaAbacoWindow(vm) { Owner = System.Windows.Application.Current.MainWindow };
        if (ventana.ShowDialog() != true)
        {
            Status = "Carga de la planilla de ábaco cancelada.";
            return;
        }

        try
        {
            var nuevos = vm.Filas.Where(f => f.CrearNuevo)
                .GroupBy(f => f.NumeroFuncionario, StringComparer.OrdinalIgnoreCase)
                .Select(g => new Application.DTOs.FuncionarioEdicionDto
                {
                    NumeroFuncionario = g.Key,
                    Nombre = AbacoFila.NombreParaPrograma(g.First().NombreAbaco),
                    Activo = true
                })
                .ToList();
            if (nuevos.Count > 0)
            {
                await _cargaMensualService.GuardarFuncionariosAsync(nuevos, "admin");
                await FuncionariosEdicion.CargarAsync();
                CatalogoMaestros.Actualizar(FuncionariosEdicion.UltimosCargados, ObrasEdicion.UltimasCargadas);
            }

            await _planillaAbacoService.GuardarCodigosAsync(vm.Filas.Select(f => (f.TipoObra, f.Codigo, f.NumeroFuncionario)), "admin");

            var (actualizadas, agregadas, noVinieron) = Pagos.AplicarAbaco(vm.Filas.ToList());
            Pagos.RecalcularTodas();
            Horas.RecalcularTodas();

            Status = $"Planilla de ábaco cargada en {PeriodoCargado}: {actualizadas} filas actualizadas y {agregadas} agregadas. Falta apretar 'Guardar pagos'.";
            var mensaje = $"Se cargaron {vm.Filas.Count} empleados de ábaco en Pagos del mes ({PeriodoCargado}):\n" +
                $"• {actualizadas} filas actualizadas y {agregadas} agregadas.\n" +
                (nuevos.Count > 0 ? $"• Funcionarios nuevos creados: {string.Join(", ", nuevos.Select(n => $"{n.NumeroFuncionario} {n.Nombre}"))}. Complete su categoría y cuenta en Funcionarios.\n" : string.Empty) +
                (noVinieron.Count > 0
                    ? $"• {noVinieron.Count} fila(s) del mes no vinieron en ábaco y quedan como estaban:\n   {string.Join("\n   ", noVinieron.Take(12))}{(noVinieron.Count > 12 ? "\n   ..." : string.Empty)}\n"
                    : string.Empty) +
                "\nRevise los montos y apriete 'Guardar pagos'. N-A y TICKET se completan a mano.";
            MessageBox.Show(mensaje, "Planilla de ábaco cargada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cargar planilla de ábaco", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------------------------------------------------------- Archivo de horas

    private async Task CargarArchivoHorasAsync()
    {
        if (Horas.SoloLectura)
        {
            MessageBox.Show("El período en pantalla está cerrado o no existe. Elija o cree el mes a cargar.", "Cargar horas",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Las horas se asignan al número que cobra en el mes: los pagos tienen que estar guardados.
        if (HayCambiosSinGuardar && !await ConfirmarCambiosPendientesAsync())
            return;

        if (!Pagos.Filas.Any(f => f.Total > 0) && MessageBox.Show(
                $"El período {PeriodoCargado} todavía no tiene pagos con montos. Conviene cargar antes la planilla de ábaco, " +
                "así las horas de cada persona van al mismo número que cobra.\n\n¿Cargar las horas igual?",
                "Cargar horas", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Elija el archivo de horas del mes (DATOS SUELDO)",
            Filter = "Excel (*.xlsm;*.xlsx)|*.xlsm;*.xlsx"
        };
        if (dialogo.ShowDialog() != true)
            return;

        Application.DTOs.PlanillaHorasDto planilla;
        Application.DTOs.EmparejamientoHorasDto sugerencias;
        try
        {
            Status = "Leyendo el archivo de horas...";
            planilla = await Task.Run(() => _planillaHorasService.Leer(dialogo.FileName));
            sugerencias = await _planillaHorasService.EmparejarAsync(planilla, PeriodoCargado);
        }
        catch (Exception ex)
        {
            Status = "No se pudo leer el archivo de horas.";
            MessageBox.Show(ex.Message, "Cargar horas", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var conPago = Pagos.Filas
            .Where(f => f.Total > 0 && f.NumeroFuncionario.Length > 0 && TipoObraParser.EsReconocido(f.TipoObraTexto))
            .Select(f => (f.NumeroFuncionario, f.TipoObra))
            .Distinct();
        var configuracion = ConfiguracionLocal.Cargar();
        var vm = new CargaHorasViewModel(PeriodoCargado, planilla, sugerencias, conPago, configuracion.ValorTicket);
        var ventana = new CargaHorasWindow(vm) { Owner = System.Windows.Application.Current.MainWindow };
        if (ventana.ShowDialog() != true)
        {
            Status = "Carga de horas cancelada.";
            return;
        }

        try
        {
            var nuevas = vm.Obras.Where(o => o.CrearNueva)
                .Select(o => new Application.DTOs.ObraEdicionDto
                {
                    NumeroObra = o.NumeroObra,
                    Nombre = o.Nombre,
                    TipoObra = o.TipoObra,
                    Cliente = "Almirtaun",
                    Activa = true
                })
                .ToList();
            if (nuevas.Count > 0)
            {
                await _cargaMensualService.GuardarObrasAsync(nuevas, "admin");
                await ObrasEdicion.CargarAsync();
                CatalogoMaestros.Actualizar(FuncionariosEdicion.UltimosCargados, ObrasEdicion.UltimasCargadas);
            }

            await _planillaHorasService.GuardarEquivalenciasAsync(
                vm.Personas.Select(p => (p.TipoObra, p.Codigo, p.NumeroFuncionario)),
                vm.Obras.Select(o => (o.TipoObra, o.Nombre, o.NumeroObra)),
                "admin");

            var filas = vm.FilasACargar();
            Horas.ReemplazarConCarga(filas);
            Horas.RecalcularTodas();

            var (ticketsCargados, ticketsAgregados, ticketsEnCero) = Pagos.AplicarTickets(vm.Tickets.ToList(), vm.TiposDelArchivo);
            Pagos.RecalcularTodas();
            if (configuracion.ValorTicket != vm.ValorTicket)
            {
                configuracion.ValorTicket = vm.ValorTicket;
                configuracion.Guardar();
            }

            Status = $"Horas cargadas en {PeriodoCargado}: {filas.Count} filas y {ticketsCargados} TICKET. Falta apretar 'Guardar horas' y 'Guardar pagos'.";
            MessageBox.Show(
                $"Se cargaron {filas.Count} filas en Horas del mes ({PeriodoCargado}), incluidas {vm.HorasSede.Count} de 1 hora en SEDE." +
                (nuevas.Count > 0 ? $"\nObras nuevas creadas: {string.Join(", ", nuevas.Select(n => $"{n.NumeroObra} {n.Nombre}"))}." : string.Empty) +
                $"\n\nTickets (valor {NumeroParser.Formatear(vm.ValorTicket)}): {ticketsCargados} fila(s) TICKET en Pagos del mes ({ticketsAgregados} nueva(s)), total {NumeroParser.Formatear(vm.Tickets.Sum(t => t.Importe))}." +
                (ticketsEnCero > 0 ? $" {ticketsEnCero} fila(s) TICKET sin tickets este mes quedaron en 0." : string.Empty) +
                (vm.SiguenSinHoras.Count > 0 ? $"\n\n{vm.SiguenSinHoras.Count} funcionario(s) cobran y quedan sin horas:\n   {string.Join("\n   ", vm.SiguenSinHoras.Take(12))}" : string.Empty) +
                "\n\nRevise y apriete 'Guardar horas' y también 'Guardar pagos' (por los tickets).",
                "Horas cargadas", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cargar horas", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------------------------------------------------------- Revisión de cruces horas/pagos

    private async Task RevisarCrucesAsync()
    {
        if (!await ConfirmarCambiosPendientesAsync())
            return;

        var errores = await _consistenciaService.ValidarConsistenciaFuncionarioAsync(PeriodoCargado);
        if (errores.Count == 0)
        {
            MessageBox.Show(
                "No hay problemas: cada funcionario con horas tiene pagos (y al revés) y los tipos de obra coinciden.",
                "Revisar horas y pagos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (PeriodoCerrado)
        {
            MessageBox.Show($"Hay {errores.Count} diferencia(s) entre horas y pagos, pero el período está cerrado y no se pueden corregir.",
                "Revisar horas y pagos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ventana = new ErroresConsistenciaWindow(_consistenciaService, PeriodoCargado, "admin", "continuar")
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        ventana.ShowDialog();

        await CargarMesAsync();
        await CargarDashboardAsync();
    }
}
