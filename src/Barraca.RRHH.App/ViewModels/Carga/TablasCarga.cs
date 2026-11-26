using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using Barraca.RRHH.App.Helpers;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Infrastructure.Helpers;
using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.App.ViewModels.Carga;

// Tabla editable: lleva la cuenta de filas nuevas, modificadas y borradas hasta que se guarda.
public abstract class TablaCargaViewModel<TFila> : ViewModelBase where TFila : FilaEdicionBase, new()
{
    private string _buscar = string.Empty;
    private bool _soloLectura;
    private bool _hayCambios;
    private int _cantidadErrores;
    private int _cantidadAvisos;
    private bool _soloConProblemas;
    private bool _recargando;

    protected TablaCargaViewModel()
    {
        Filas.CollectionChanged += Filas_CollectionChanged;
        Vista = CollectionViewSource.GetDefaultView(Filas);
        Vista.Filter = Filtrar;
    }

    public ObservableCollection<TFila> Filas { get; } = new();
    public ICollectionView Vista { get; }

    // Filas guardadas que se quitaron de la tabla (se borran al guardar).
    protected int FilasBorradas { get; private set; }

    public string Buscar
    {
        get => _buscar;
        set { if (SetProperty(ref _buscar, value ?? string.Empty)) Vista.Refresh(); }
    }

    public bool SoloConProblemas
    {
        get => _soloConProblemas;
        set { if (SetProperty(ref _soloConProblemas, value)) Vista.Refresh(); }
    }

    public bool SoloLectura
    {
        get => _soloLectura;
        set { if (SetProperty(ref _soloLectura, value)) OnPropertyChanged(nameof(PuedeEditar)); }
    }

    public bool PuedeEditar => !SoloLectura;

    public bool HayCambios
    {
        get => _hayCambios;
        private set => SetProperty(ref _hayCambios, value);
    }

    public int CantidadErrores
    {
        get => _cantidadErrores;
        private set => SetProperty(ref _cantidadErrores, value);
    }

    public int CantidadAvisos
    {
        get => _cantidadAvisos;
        private set => SetProperty(ref _cantidadAvisos, value);
    }

    public int CantidadFilas => Filas.Count;

    public string Resumen =>
        $"{Filas.Count} filas" +
        (CantidadErrores > 0 ? $" · {CantidadErrores} con error" : string.Empty) +
        (CantidadAvisos > 0 ? $" · {CantidadAvisos} con aviso" : string.Empty) +
        (HayCambios ? " · cambios sin guardar" : string.Empty);

    protected void Reemplazar(IEnumerable<TFila> filas)
    {
        _recargando = true;
        try
        {
            Filas.Clear();
            foreach (var fila in filas)
                Filas.Add(fila);
        }
        finally
        {
            _recargando = false;
        }

        FilasBorradas = 0;
        Actualizar();
    }

    public TFila AgregarFila()
    {
        var fila = new TFila();
        fila.Inicializar(() => { });
        fila.Modificada = true;
        Filas.Add(fila);
        return fila;
    }

    public void EliminarFilas(IEnumerable<object> seleccionadas)
    {
        foreach (var fila in seleccionadas.OfType<TFila>().ToList())
        {
            if (PuedeBorrar(fila))
                Filas.Remove(fila);
        }
    }

    protected virtual bool PuedeBorrar(TFila fila) => true;

    public void RecalcularTodas()
    {
        foreach (var fila in Filas)
            fila.Recalcular();
        Actualizar();
    }

    // Filas que se van a guardar: sin las agregadas y dejadas en blanco.
    protected List<TFila> FilasParaGuardar() => Filas.Where(f => !(f.EsNueva && f.EstaVacia)).ToList();

    protected void VerificarSinErrores()
    {
        var conError = FilasParaGuardar().Where(f => f.TieneError).ToList();
        if (conError.Count == 0)
            return;

        var detalle = string.Join("\n", conError.Take(10).Select(f => $"• {f.TextoBusqueda.Trim()}: {f.Problema}"));
        if (conError.Count > 10)
            detalle += $"\n... y {conError.Count - 10} más.";
        throw new InvalidOperationException(
            $"Hay {conError.Count} fila(s) con error. Corríjalas antes de guardar (puede tildar 'Solo filas con problemas' para verlas):\n{detalle}");
    }

    protected virtual void AlActualizar() { }

    private bool Filtrar(object item)
    {
        if (item is not TFila fila)
            return true;
        if (SoloConProblemas && fila.Problema.Length == 0)
            return false;
        return Buscar.Length == 0 || fila.TextoBusqueda.Contains(Buscar, StringComparison.CurrentCultureIgnoreCase);
    }

    private void Filas_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TFila fila in e.OldItems)
            {
                fila.PropertyChanged -= Fila_PropertyChanged;
                if (!_recargando && !fila.EsNueva)
                    FilasBorradas++;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (TFila fila in e.NewItems)
            {
                fila.PropertyChanged += Fila_PropertyChanged;
                // Filas creadas desde la tabla (última fila en blanco) o pegadas.
                if (!_recargando && fila.EsNueva)
                {
                    fila.Inicializar(() => { });
                    fila.Modificada = true;
                }
            }
        }

        if (!_recargando)
            Actualizar();
    }

    private void Fila_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FilaEdicionBase.Modificada) or nameof(FilaEdicionBase.Problema) or nameof(FilaEdicionBase.TieneError))
            Actualizar();
        else
            AlActualizar();
    }

    protected void Actualizar()
    {
        HayCambios = FilasBorradas > 0 || Filas.Any(f => f.Modificada && !(f.EsNueva && f.EstaVacia));
        CantidadErrores = Filas.Count(f => f.TieneError && !(f.EsNueva && f.EstaVacia));
        CantidadAvisos = Filas.Count(f => f.TieneAviso);
        OnPropertyChanged(nameof(CantidadFilas));
        OnPropertyChanged(nameof(Resumen));
        AlActualizar();
    }
}

public sealed class PagosMesViewModel : TablaCargaViewModel<PagoFila>
{
    private readonly ICargaMensualService _servicio;

    public PagosMesViewModel(ICargaMensualService servicio)
    {
        _servicio = servicio;
    }

    public decimal TotalAdelantos => Filas.Sum(f => f.Adelanto);
    public decimal TotalLiquidos => Filas.Sum(f => f.Liquido);
    public decimal TotalRetenciones => Filas.Sum(f => f.Retencion);
    public decimal TotalGeneral => Filas.Sum(f => f.Total);
    public int FilasSinMontos => Filas.Count(f => f.Total == 0);

    public string Totales =>
        $"Adelantos {NumeroParser.Formatear(TotalAdelantos)}   ·   Líquidos {NumeroParser.Formatear(TotalLiquidos)}   ·   " +
        $"Retenciones {NumeroParser.Formatear(TotalRetenciones)}   ·   TOTAL {NumeroParser.Formatear(TotalGeneral)}" +
        (FilasSinMontos > 0 ? $"   ·   {FilasSinMontos} fila(s) sin montos" : string.Empty);

    public async Task CargarAsync(string periodo)
    {
        var dtos = await _servicio.ObtenerPagosAsync(periodo);
        Reemplazar(dtos.Select(PagoFila.Desde));
    }

    public async Task<string> GuardarAsync(string periodo, string usuario)
    {
        VerificarSinErrores();
        var resultado = await _servicio.GuardarPagosAsync(periodo, FilasParaGuardar().Select(f => f.ADto()).ToList(), usuario);
        await CargarAsync(periodo);
        return resultado;
    }

    public void QuitarFilasSinMontos()
    {
        foreach (var fila in Filas.Where(f => f.Total == 0).ToList())
            Filas.Remove(fila);
    }

    // Vuelca la planilla de ábaco: cada (funcionario, tipo de obra) usa sus filas del mes que no son TICKET,
    // en orden; si faltan filas se agregan. Cargar la misma planilla dos veces deja el mismo resultado.
    public (int Actualizadas, int Agregadas, List<string> NoVinieron) AplicarAbaco(IReadOnlyList<AbacoFila> filasAbaco)
    {
        int actualizadas = 0, agregadas = 0;
        var tocadas = new HashSet<PagoFila>();

        foreach (var grupo in filasAbaco.GroupBy(f => (Numero: f.NumeroFuncionario, f.TipoObra)))
        {
            var destino = Filas
                .Where(f => string.Equals(f.NumeroFuncionario, grupo.Key.Numero, StringComparison.OrdinalIgnoreCase)
                            && TipoObraParser.EsReconocido(f.TipoObraTexto) && f.TipoObra == grupo.Key.TipoObra && !EsTicket(f))
                .ToList();

            var i = 0;
            foreach (var abaco in grupo)
            {
                PagoFila fila;
                if (i < destino.Count)
                {
                    fila = destino[i];
                    actualizadas++;
                }
                else
                {
                    fila = AgregarFila();
                    fila.NumeroFuncionario = grupo.Key.Numero;
                    fila.TipoObraTexto = TipoObraParser.Texto(grupo.Key.TipoObra);
                    agregadas++;
                }

                fila.Adelanto = abaco.Adelanto;
                fila.Liquido = abaco.Liquido;
                fila.Retencion = abaco.Retencion;
                fila.Nominal = abaco.Fila.Nominal;
                tocadas.Add(fila);
                i++;
            }
        }

        // Filas de Construcción o Industria y Comercio del mes que no aparecen en ábaco (quedan como estaban).
        var tipos = filasAbaco.Select(f => f.TipoObra).ToHashSet();
        var noVinieron = Filas
            .Where(f => !tocadas.Contains(f) && !EsTicket(f) && TipoObraParser.EsReconocido(f.TipoObraTexto) && tipos.Contains(f.TipoObra) && f.NumeroFuncionario.Length > 0)
            .Select(f => $"{f.NumeroFuncionario} {f.Nombre} ({f.TipoObraTexto}){(f.Total != 0 ? $": tiene {NumeroParser.Formatear(f.Total)} cargado" : string.Empty)}")
            .ToList();

        return (actualizadas, agregadas, noVinieron);
    }

    // Tickets de alimentación del archivo de horas: Líquido de la fila TICKET = cantidad × valor del ticket.
    // En los tipos que trae el archivo, las filas TICKET de quien no tiene tickets quedan en 0.
    public (int Cargadas, int Agregadas, int EnCero) AplicarTickets(IReadOnlyList<TicketFila> tickets, IReadOnlyCollection<TipoObra> tiposDelArchivo)
    {
        int cargadas = 0, agregadas = 0, enCero = 0;
        var asignadas = new HashSet<PagoFila>();

        foreach (var ticket in tickets)
        {
            var filasTicket = Filas
                .Where(f => EsTicket(f) && string.Equals(f.NumeroFuncionario, ticket.NumeroFuncionario, StringComparison.OrdinalIgnoreCase)
                            && TipoObraParser.EsReconocido(f.TipoObraTexto) && f.TipoObra == ticket.TipoObra)
                .ToList();

            var fila = filasTicket.FirstOrDefault();
            if (fila is null)
            {
                fila = AgregarFila();
                fila.NumeroFuncionario = ticket.NumeroFuncionario;
                fila.TipoObraTexto = TipoObraParser.Texto(ticket.TipoObra);
                fila.Observacion = "TICKET";
                agregadas++;
            }

            fila.Liquido = ticket.Importe;
            asignadas.Add(fila);
            cargadas++;

            // Una persona con dos filas TICKET del mismo tipo: el importe queda en una sola.
            foreach (var duplicada in filasTicket.Skip(1))
                duplicada.Liquido = 0m;
        }

        foreach (var fila in Filas.Where(f => EsTicket(f) && !asignadas.Contains(f) && TipoObraParser.EsReconocido(f.TipoObraTexto)
                                             && tiposDelArchivo.Contains(f.TipoObra) && f.Liquido != 0).ToList())
        {
            fila.Liquido = 0m;
            enCero++;
        }

        return (cargadas, agregadas, enCero);
    }

    private static bool EsTicket(PagoFila fila) =>
        fila.Observacion.Contains("TICKET", StringComparison.OrdinalIgnoreCase);

    protected override void AlActualizar() => OnPropertyChanged(nameof(Totales));
}

public sealed class HorasMesViewModel : TablaCargaViewModel<HoraFila>
{
    private readonly ICargaMensualService _servicio;

    public HorasMesViewModel(ICargaMensualService servicio)
    {
        _servicio = servicio;
    }

    public decimal TotalComunes => Filas.Sum(f => f.HorasComunes);
    public decimal TotalExtras => Filas.Sum(f => f.HorasExtras);
    public decimal TotalLluvia => Filas.Sum(f => f.HorasLluvia);
    public decimal TotalEquivalentes => Filas.Sum(f => f.HorasEquivalentes);
    public int FilasSinHoras => Filas.Count(f => f.HorasEquivalentes == 0);

    public string Totales =>
        $"Horas comunes {NumeroParser.Formatear(TotalComunes)}   ·   Extras {NumeroParser.Formatear(TotalExtras)}   ·   Lluvia {NumeroParser.Formatear(TotalLluvia)}   ·   " +
        $"Equivalentes {NumeroParser.Formatear(TotalEquivalentes)}" +
        (FilasSinHoras > 0 ? $"   ·   {FilasSinHoras} fila(s) sin horas" : string.Empty);

    public async Task CargarAsync(string periodo)
    {
        var dtos = await _servicio.ObtenerHorasAsync(periodo);
        Reemplazar(dtos.Select(HoraFila.Desde));
    }

    public async Task<string> GuardarAsync(string periodo, string usuario)
    {
        VerificarSinErrores();
        var resultado = await _servicio.GuardarHorasAsync(periodo, FilasParaGuardar().Select(f => f.ADto()).ToList(), usuario);
        await CargarAsync(periodo);
        return resultado;
    }

    public void QuitarFilasSinHoras()
    {
        foreach (var fila in Filas.Where(f => f.HorasEquivalentes == 0).ToList())
            Filas.Remove(fila);
    }

    // Las horas del mes pasan a ser las del archivo: las filas anteriores se borran al guardar.
    public void ReemplazarConCarga(IEnumerable<HoraEdicionDto> filas)
    {
        foreach (var fila in Filas.ToList())
            Filas.Remove(fila);

        foreach (var dto in filas)
        {
            var fila = AgregarFila();
            fila.NumeroFuncionario = dto.NumeroFuncionario;
            fila.NumeroObra = dto.NumeroObra;
            fila.HorasComunes = dto.HorasComunes;
            fila.HorasExtras = dto.HorasExtras;
            fila.HorasLluvia = dto.HorasLluvia;
        }
    }

    protected override void AlActualizar() => OnPropertyChanged(nameof(Totales));
}

public sealed class FuncionariosEdicionViewModel : TablaCargaViewModel<FuncionarioFila>
{
    private readonly ICargaMensualService _servicio;

    public FuncionariosEdicionViewModel(ICargaMensualService servicio)
    {
        _servicio = servicio;
    }

    public List<FuncionarioEdicionDto> UltimosCargados { get; private set; } = new();

    public async Task CargarAsync()
    {
        UltimosCargados = await _servicio.ObtenerFuncionariosAsync();
        Reemplazar(UltimosCargados.Select(FuncionarioFila.Desde));
    }

    public async Task<string> GuardarAsync(string usuario)
    {
        VerificarSinErrores();
        var cambios = FilasParaGuardar().Where(f => f.Modificada).Select(f => f.ADto()).ToList();
        var resultado = await _servicio.GuardarFuncionariosAsync(cambios, usuario);
        await CargarAsync();
        return resultado;
    }

    // Los funcionarios guardados no se borran (tienen historia): se marcan como inactivos.
    protected override bool PuedeBorrar(FuncionarioFila fila) => fila.EsNueva;
}

public sealed class ObrasEdicionViewModel : TablaCargaViewModel<ObraFila>
{
    private readonly ICargaMensualService _servicio;

    public ObrasEdicionViewModel(ICargaMensualService servicio)
    {
        _servicio = servicio;
    }

    public List<ObraEdicionDto> UltimasCargadas { get; private set; } = new();

    public async Task CargarAsync()
    {
        UltimasCargadas = await _servicio.ObtenerObrasAsync();
        Reemplazar(UltimasCargadas.Select(ObraFila.Desde));
    }

    public async Task<string> GuardarAsync(string usuario)
    {
        VerificarSinErrores();
        var cambios = FilasParaGuardar().Where(f => f.Modificada).Select(f => f.ADto()).ToList();
        var resultado = await _servicio.GuardarObrasAsync(cambios, usuario);
        await CargarAsync();
        return resultado;
    }

    protected override bool PuedeBorrar(ObraFila fila) => fila.EsNueva;
}
