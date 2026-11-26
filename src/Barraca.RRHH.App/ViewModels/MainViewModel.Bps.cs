using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using Barraca.RRHH.App.Helpers;
using Barraca.RRHH.App.ViewModels.Carga;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.App.ViewModels;

// Pestaña "BPS por obra" (26/11/2026): facturas BPS del mes, tasa de la Sede, reparto por obra y planilla para SP.
public partial class MainViewModel
{
    private IDistribucionBpsService _bpsService = default!;
    private bool _bpsHayCambios;
    private string _tasaSedeTexto = string.Empty;
    private string _bpsTasaNota = string.Empty;
    private string _bpsResumen = string.Empty;
    private FacturaBpsFila? _facturaBpsSeleccionada;

    public ObservableCollection<FacturaBpsFila> FacturasBps { get; } = new();
    public ObservableCollection<string> BpsProblemas { get; } = new();
    public ObservableCollection<string> BpsAvisos { get; } = new();
    public ObservableCollection<RepartoBpsLineaDto> BpsLineas { get; } = new();
    public ObservableCollection<SedeBpsPersonaDto> BpsSede { get; } = new();
    public ObservableCollection<FacturaSpDto> BpsFacturasSp { get; } = new();

    public AsyncRelayCommand GuardarBpsCommand { get; private set; } = default!;
    public AsyncRelayCommand CalcularBpsCommand { get; private set; } = default!;
    public AsyncRelayCommand PlanillaSpBpsCommand { get; private set; } = default!;
    public RelayCommand AgregarFacturaBpsCommand { get; private set; } = default!;
    public RelayCommand QuitarFacturaBpsCommand { get; private set; } = default!;

    public bool BpsHayCambios
    {
        get => _bpsHayCambios;
        private set { if (SetProperty(ref _bpsHayCambios, value)) OnPropertyChanged(nameof(BpsTotales)); }
    }

    // Porcentaje como lo escribe el usuario: "39,656".
    public string TasaSedeTexto
    {
        get => _tasaSedeTexto;
        set { if (SetProperty(ref _tasaSedeTexto, value ?? string.Empty)) { BpsHayCambios = true; BpsTasaNota = string.Empty; } }
    }

    public string BpsTasaNota { get => _bpsTasaNota; private set => SetProperty(ref _bpsTasaNota, value); }
    public string BpsResumen { get => _bpsResumen; private set => SetProperty(ref _bpsResumen, value); }
    public FacturaBpsFila? FacturaBpsSeleccionada { get => _facturaBpsSeleccionada; set => SetProperty(ref _facturaBpsSeleccionada, value); }
    public bool BpsPuedeEditar => !PeriodoCerrado && !string.IsNullOrEmpty(PeriodoCargado);
    public bool BpsHayProblemas => BpsProblemas.Count > 0;
    public bool BpsHayAvisos => BpsAvisos.Count > 0;

    public string BpsTotales
    {
        get
        {
            var c = FacturasBps.Where(f => f.TipoObra == TipoObra.Construccion).Sum(f => f.Importe);
            var ic = FacturasBps.Where(f => f.TipoObra == TipoObra.IndustriaYComercio).Sum(f => f.Importe);
            return $"Construcción {Pesos(c)} · Industria y Comercio {Pesos(ic)}" + (BpsHayCambios ? " · cambios sin guardar" : string.Empty);
        }
    }

    private void InicializarBps(IDistribucionBpsService bpsService)
    {
        _bpsService = bpsService;
        GuardarBpsCommand = new AsyncRelayCommand(GuardarBpsAsync);
        CalcularBpsCommand = new AsyncRelayCommand(() => CalcularBpsAsync(avisarErrores: true));
        PlanillaSpBpsCommand = new AsyncRelayCommand(GenerarPlanillaSpBpsAsync);
        AgregarFacturaBpsCommand = new RelayCommand(() =>
        {
            var fila = new FacturaBpsFila { AlCambiar = MarcarCambioBps };
            FacturasBps.Add(fila);
            FacturaBpsSeleccionada = fila;
            MarcarCambioBps();
        });
        QuitarFacturaBpsCommand = new RelayCommand(() =>
        {
            if (FacturaBpsSeleccionada is null)
                return;
            FacturasBps.Remove(FacturaBpsSeleccionada);
            MarcarCambioBps();
        });
    }

    private void MarcarCambioBps()
    {
        BpsHayCambios = true;
        OnPropertyChanged(nameof(BpsTotales));
    }

    private async Task CargarBpsAsync()
    {
        FacturasBps.Clear();
        LimpiarResultadoBps();
        OnPropertyChanged(nameof(BpsPuedeEditar));
        if (string.IsNullOrEmpty(PeriodoCargado))
            return;

        var mes = await _bpsService.ObtenerAsync(PeriodoCargado);
        foreach (var f in mes.Facturas)
            FacturasBps.Add(Fila(f.TipoObra, f.Descripcion, f.Importe));

        // Un mes abierto sin facturas propone las de siempre, vacías: quedan pendientes hasta completarlas.
        if (mes.Facturas.Count == 0 && !mes.Cerrado)
            foreach (var (tipo, descripcion) in OpcionesBps.FacturasTipicas)
                FacturasBps.Add(Fila(tipo, descripcion, 0m));

        _tasaSedeTexto = mes.TasaSede is { } tasa ? (tasa * 100m).ToString("0.####", NumeroParser.Cultura) : string.Empty;
        OnPropertyChanged(nameof(TasaSedeTexto));
        BpsTasaNota = mes.TasaSedeDe.Length > 0 ? $"Es la de {mes.TasaSedeDe}: confírmela y guarde." : string.Empty;
        BpsHayCambios = false;
        OnPropertyChanged(nameof(BpsTotales));

        await CalcularBpsAsync(avisarErrores: false);
    }

    private FacturaBpsFila Fila(TipoObra tipo, string descripcion, decimal importe)
    {
        var fila = FacturaBpsFila.Desde(tipo, descripcion, importe);
        fila.AlCambiar = MarcarCambioBps;
        return fila;
    }

    private async Task GuardarBpsAsync()
    {
        try
        {
            Status = await GuardarBpsInternoAsync();
            await CalcularBpsAsync(avisarErrores: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "No se guardaron las facturas BPS", MessageBoxButton.OK, MessageBoxImage.Warning);
            Status = "No se guardaron las facturas BPS.";
        }
    }

    private async Task<string> GuardarBpsInternoAsync()
    {
        decimal? tasa = null;
        if (TasaSedeTexto.Trim().Length > 0)
        {
            if (!NumeroParser.TryParse(TasaSedeTexto, out var porcentaje) || porcentaje <= 0 || porcentaje >= 100)
                throw new InvalidOperationException("La tasa de la Sede no es válida. Escríbala como porcentaje, por ejemplo 39,656.");
            tasa = porcentaje / 100m;
        }

        var filas = FacturasBps.Where(f => !f.EstaVacia).Select(f => f.ADto()).ToList();
        var mensaje = await _bpsService.GuardarAsync(PeriodoCargado, tasa, filas, "admin");
        BpsHayCambios = false;
        BpsTasaNota = string.Empty;
        return mensaje;
    }

    private async Task CalcularBpsAsync(bool avisarErrores)
    {
        try
        {
            if (string.IsNullOrEmpty(PeriodoCargado))
                return;
            if (BpsHayCambios && BpsPuedeEditar)
                Status = await GuardarBpsInternoAsync();

            var r = await _bpsService.CalcularAsync(PeriodoCargado);
            MostrarResultadoBps(r);
            if (avisarErrores)
                Status = r.Listo ? $"BPS de {r.Periodo} repartido: {r.FacturasSp.Count} facturas para SP." : $"BPS de {r.Periodo}: falta información para repartir.";
        }
        catch (Exception ex)
        {
            if (avisarErrores)
                MessageBox.Show(ex.Message, "BPS por obra", MessageBoxButton.OK, MessageBoxImage.Warning);
            Status = $"BPS por obra: {ex.Message}";
        }
    }

    private async Task GenerarPlanillaSpBpsAsync()
    {
        try
        {
            if (BpsHayCambios && BpsPuedeEditar)
                Status = await GuardarBpsInternoAsync();

            Directory.CreateDirectory(CarpetaReportes);
            var ruta = await _bpsService.GenerarPlanillaSpAsync(PeriodoCargado, CarpetaReportes, "admin");
            ReportesGenerados.Insert(0, new SimpleRowViewModel { Codigo = Path.GetFileName(ruta), Descripcion = ruta, Extra = "Excel generado" });
            Status = $"Planilla para SP generada: {Path.GetFileName(ruta)}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
            await CalcularBpsAsync(avisarErrores: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Planilla para SP", MessageBoxButton.OK, MessageBoxImage.Warning);
            Status = "No se generó la planilla para SP.";
        }
    }

    private void LimpiarResultadoBps()
    {
        BpsProblemas.Clear();
        BpsAvisos.Clear();
        BpsLineas.Clear();
        BpsSede.Clear();
        BpsFacturasSp.Clear();
        BpsResumen = string.Empty;
        OnPropertyChanged(nameof(BpsHayProblemas));
        OnPropertyChanged(nameof(BpsHayAvisos));
    }

    private void MostrarResultadoBps(RepartoBpsResultadoDto r)
    {
        LimpiarResultadoBps();
        foreach (var p in r.Problemas) BpsProblemas.Add(p);
        foreach (var a in r.Avisos) BpsAvisos.Add(a);
        foreach (var l in r.Lineas) BpsLineas.Add(l);
        foreach (var s in r.PersonasSede) BpsSede.Add(s);
        foreach (var f in r.FacturasSp) BpsFacturasSp.Add(f);

        var obrasC = r.Lineas.Count(l => l.TipoObra == TipoObra.Construccion);
        var obrasIc = r.Lineas.Count(l => l.TipoObra == TipoObra.IndustriaYComercio);
        BpsResumen = r.Listo
            ? $"Construcción {Pesos(r.FacturadoConstruccion)} entre {obrasC} obras por {r.HorasConstruccion.ToString("#,##0.#", NumeroParser.Cultura)} horas.\n" +
              $"Industria y Comercio {Pesos(r.FacturadoIndustria)} = Sede {Pesos(r.Sede)} ({r.PersonasSede.Count} personas) + {Pesos(r.ARepartirIndustria)} entre {obrasIc} obras por {r.HorasIndustria.ToString("#,##0.#", NumeroParser.Cultura)} horas.\n" +
              $"{r.FacturasSp.Count} facturas para SP por {Pesos(r.FacturasSp.Sum(f => f.Importe))} ✓ suma lo facturado."
            : "Falta información para repartir el BPS de este mes. Revise lo que está en rojo.";
        OnPropertyChanged(nameof(BpsHayProblemas));
        OnPropertyChanged(nameof(BpsHayAvisos));
    }

    private static string Pesos(decimal v) => "$ " + v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-UY"));
}
