using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Barraca.RRHH.App.Helpers;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Helpers;

namespace Barraca.RRHH.App.ViewModels.Carga;

// Persona del archivo de horas y el funcionario del programa al que se le cargan sus horas.
public sealed class PersonaHorasFila : ViewModelBase
{
    private string _numeroFuncionario;
    private string _estado;
    private string _problema = string.Empty;

    public PersonaHorasFila(EmparejamientoPersonaHorasDto sugerencia, decimal horas)
    {
        TipoObra = sugerencia.TipoObra;
        Codigo = sugerencia.Codigo;
        Nombre = sugerencia.Nombre;
        RequiereRevision = sugerencia.RequiereRevision;
        Horas = horas;
        _numeroFuncionario = sugerencia.NumeroFuncionario;
        _estado = sugerencia.Estado;
    }

    public TipoObra TipoObra { get; }
    public string Tipo => TipoObraParser.Texto(TipoObra);
    public string Codigo { get; }
    public string Nombre { get; }
    public bool RequiereRevision { get; }
    public decimal Horas { get; }

    public string NumeroFuncionario
    {
        get => _numeroFuncionario;
        set
        {
            if (!SetProperty(ref _numeroFuncionario, (value ?? string.Empty).Trim()))
                return;
            Estado = "Elegido a mano";
            OnPropertyChanged(nameof(NombrePrograma));
            AlCambiar?.Invoke();
        }
    }

    public string Estado { get => _estado; private set => SetProperty(ref _estado, value); }
    public string NombrePrograma => CatalogoMaestros.Actual.Funcionario(NumeroFuncionario)?.Nombre ?? string.Empty;

    public string Problema
    {
        get => _problema;
        set { if (SetProperty(ref _problema, value)) OnPropertyChanged(nameof(TieneError)); }
    }

    public bool TieneError => Problema.Length > 0;
    internal Action? AlCambiar { get; set; }
}

// Obra del archivo de horas y la obra del programa (existente o nueva).
public sealed class ObraHorasFila : ViewModelBase
{
    private string _numeroObra;
    private bool _crearNueva;
    private string _estado;
    private string _problema = string.Empty;

    public ObraHorasFila(EmparejamientoObraHorasDto sugerencia, decimal horas)
    {
        TipoObra = sugerencia.TipoObra;
        Nombre = sugerencia.Nombre;
        RequiereRevision = sugerencia.RequiereRevision;
        Horas = horas;
        _numeroObra = sugerencia.NumeroObra;
        _crearNueva = sugerencia.CrearNueva;
        _estado = sugerencia.Estado;
    }

    public TipoObra TipoObra { get; }
    public string Tipo => TipoObraParser.Texto(TipoObra);
    public string Nombre { get; }
    public bool RequiereRevision { get; }
    public decimal Horas { get; }

    public string NumeroObra
    {
        get => _numeroObra;
        set
        {
            if (!SetProperty(ref _numeroObra, (value ?? string.Empty).Trim()))
                return;
            Estado = "Elegida a mano";
            OnPropertyChanged(nameof(NombrePrograma));
            AlCambiar?.Invoke();
        }
    }

    public bool CrearNueva
    {
        get => _crearNueva;
        set
        {
            if (!SetProperty(ref _crearNueva, value))
                return;
            OnPropertyChanged(nameof(NombrePrograma));
            AlCambiar?.Invoke();
        }
    }

    public string Estado { get => _estado; private set => SetProperty(ref _estado, value); }

    public string NombrePrograma => CrearNueva
        ? $"(nueva) {Nombre}"
        : CatalogoMaestros.Actual.Obra(NumeroObra) is { } o ? $"{o.Nombre} ({TipoObraParser.Texto(o.TipoObra)})" : string.Empty;

    public string Problema
    {
        get => _problema;
        set { if (SetProperty(ref _problema, value)) OnPropertyChanged(nameof(TieneError)); }
    }

    public bool TieneError => Problema.Length > 0;
    internal Action? AlCambiar { get; set; }
}

// Funcionario con pagos y sin horas al que se le carga 1 hora en la obra SEDE.
public sealed record HoraSedeFila(string NumeroFuncionario, string Nombre, TipoObra TipoObra, string NumeroObra, string NombreObra, string Motivo)
{
    public string Tipo => TipoObraParser.Texto(TipoObra);
}

// Tickets de alimentación de un funcionario en un tipo de obra: van al Líquido de su fila TICKET.
public sealed record TicketFila(string NumeroFuncionario, string Nombre, TipoObra TipoObra, decimal Cantidad, decimal Importe)
{
    public string Tipo => TipoObraParser.Texto(TipoObra);
}

public sealed class CargaHorasViewModel : ViewModelBase
{
    private readonly HashSet<(string Numero, TipoObra Tipo)> _conPago;
    private bool _soloARevisar;
    private decimal _valorTicket;

    public CargaHorasViewModel(string periodo, PlanillaHorasDto planilla, EmparejamientoHorasDto sugerencias, IEnumerable<(string Numero, TipoObra Tipo)> conPago, decimal valorTicket)
    {
        Periodo = periodo;
        Planilla = planilla;
        _conPago = conPago.ToHashSet();
        _valorTicket = valorTicket;

        foreach (var p in sugerencias.Personas)
        {
            var horas = planilla.Filas.Where(f => f.TipoObra == p.TipoObra && f.Codigo == p.Codigo && f.Nombre == p.Nombre).Sum(Equivalentes);
            Personas.Add(new PersonaHorasFila(p, horas) { AlCambiar = Recalcular });
        }

        var proximoNumero = ProximoNumeroObra();
        foreach (var o in sugerencias.Obras)
        {
            if (o.CrearNueva && o.NumeroObra.Length == 0)
                o.NumeroObra = (proximoNumero++).ToString();
            var horas = planilla.Filas.Where(f => f.TipoObra == o.TipoObra && f.Obra == o.Nombre).Sum(Equivalentes);
            Obras.Add(new ObraHorasFila(o, horas) { AlCambiar = Recalcular });
        }

        _soloARevisar = Personas.Any(p => p.RequiereRevision);
        VistaPersonas = CollectionViewSource.GetDefaultView(Personas);
        VistaPersonas.Filter = o => !SoloARevisar || o is PersonaHorasFila { RequiereRevision: true } or PersonaHorasFila { TieneError: true };
        Recalcular();
    }

    public string Periodo { get; }
    public PlanillaHorasDto Planilla { get; }
    public ObservableCollection<PersonaHorasFila> Personas { get; } = new();
    public ObservableCollection<ObraHorasFila> Obras { get; } = new();
    public ObservableCollection<HoraSedeFila> HorasSede { get; } = new();
    public ObservableCollection<string> SiguenSinHoras { get; } = new();
    public ObservableCollection<TicketFila> Tickets { get; } = new();
    public ICollectionView VistaPersonas { get; }

    // Valor de un ticket de alimentación; se recuerda para los meses siguientes.
    public decimal ValorTicket
    {
        get => _valorTicket;
        set { if (SetProperty(ref _valorTicket, value)) Recalcular(); }
    }

    public string ResumenTickets =>
        $"{Tickets.Count} persona(s) · {NumeroParser.Formatear(Tickets.Sum(t => t.Cantidad))} tickets · Total {NumeroParser.Formatear(Tickets.Sum(t => t.Importe))}";

    public bool SoloARevisar
    {
        get => _soloARevisar;
        set { if (SetProperty(ref _soloARevisar, value)) VistaPersonas.Refresh(); }
    }

    public string ResumenArchivo =>
        $"{Planilla.Archivo}: {Planilla.Filas.Count} filas con horas ({Planilla.FilasSinHoras} filas en 0 no se cargan) · " +
        $"Comunes {NumeroParser.Formatear(Planilla.Filas.Sum(f => f.Comunes))} · Extras {NumeroParser.Formatear(Planilla.Filas.Sum(f => f.Extras))} · " +
        $"Lluvia {NumeroParser.Formatear(Planilla.Filas.Sum(f => f.Lluvia))} · Equivalentes {NumeroParser.Formatear(Planilla.Filas.Sum(Equivalentes))}";

    public string Resumen =>
        $"Funcionarios: {Personas.Count} ({Personas.Count(p => p.RequiereRevision)} para revisar) · Obras: {Obras.Count} ({Obras.Count(o => o.RequiereRevision)} para revisar, {Obras.Count(o => o.CrearNueva)} nuevas) · " +
        $"1 hora en SEDE: {HorasSede.Count} · Con pagos y sin horas: {SiguenSinHoras.Count} · Errores: {Personas.Count(p => p.TieneError) + Obras.Count(o => o.TieneError)}";

    public string TituloPersonas => $"Funcionarios ({Personas.Count})";
    public string TituloObras => $"Obras ({Obras.Count})";
    public string TituloSede => $"1 hora en SEDE ({HorasSede.Count})";
    public string TituloSinHoras => $"Con pagos y sin horas ({SiguenSinHoras.Count})";
    public string TituloTickets => $"Tickets ({Tickets.Count})";

    // Tipos de obra que trae el archivo: sus filas TICKET quedan como dice el archivo (sin tickets = 0).
    public IReadOnlyCollection<TipoObra> TiposDelArchivo => Planilla.Filas.Select(f => f.TipoObra).Distinct().ToList();

    public bool HayErrores => Personas.Any(p => p.TieneError) || Obras.Any(o => o.TieneError);

    // Filas de horas a cargar: archivo agrupado por funcionario y obra, más las horas en SEDE.
    public List<HoraEdicionDto> FilasACargar()
    {
        var personas = Personas.ToDictionary(p => (p.TipoObra, p.Codigo, p.Nombre));
        var obras = Obras.ToDictionary(o => (o.TipoObra, o.Nombre));

        var filas = Planilla.Filas
            .GroupBy(f => (Funcionario: personas[(f.TipoObra, f.Codigo, f.Nombre)].NumeroFuncionario, Obra: obras[(f.TipoObra, f.Obra)].NumeroObra))
            .Select(g => new HoraEdicionDto
            {
                NumeroFuncionario = g.Key.Funcionario,
                NumeroObra = g.Key.Obra,
                HorasComunes = g.Sum(f => f.Comunes),
                HorasExtras = g.Sum(f => f.Extras),
                HorasLluvia = g.Sum(f => f.Lluvia)
            })
            .ToList();

        filas.AddRange(HorasSede.Select(s => new HoraEdicionDto { NumeroFuncionario = s.NumeroFuncionario, NumeroObra = s.NumeroObra, HorasComunes = 1m }));
        return filas;
    }

    private void Recalcular()
    {
        foreach (var p in Personas)
            p.Problema = p.NumeroFuncionario.Length == 0 ? "Indique el N° de funcionario."
                : CatalogoMaestros.Actual.Funcionario(p.NumeroFuncionario) is null ? $"No existe el funcionario {p.NumeroFuncionario}."
                : string.Empty;

        foreach (var o in Obras)
        {
            var obra = CatalogoMaestros.Actual.Obra(o.NumeroObra);
            o.Problema = o.NumeroObra.Length == 0 ? "Indique el N° de obra o marque 'Crear'."
                : o.CrearNueva && obra is not null ? $"El número {o.NumeroObra} ya existe: escriba otro número para la obra nueva."
                : !o.CrearNueva && obra is null ? $"No existe la obra {o.NumeroObra}."
                : !o.CrearNueva && obra!.TipoObra != o.TipoObra ? $"La obra {o.NumeroObra} es de {TipoObraParser.Texto(obra.TipoObra)} y las horas son de {o.Tipo}."
                : string.Empty;
        }

        CalcularHorasSede();
        CalcularTickets();

        foreach (var nombre in new[] { nameof(Resumen), nameof(HayErrores), nameof(TituloPersonas), nameof(TituloObras), nameof(TituloSede), nameof(TituloSinHoras), nameof(TituloTickets), nameof(ResumenTickets) })
            OnPropertyChanged(nombre);
    }

    // Tickets del archivo por funcionario del programa y tipo (dos códigos de una misma persona se suman).
    private void CalcularTickets()
    {
        Tickets.Clear();
        var personas = Personas.ToDictionary(p => (p.TipoObra, p.Codigo, p.Nombre));
        var porFuncionario = Planilla.Tickets
            .GroupBy(t => (Numero: personas[(t.TipoObra, t.Codigo, t.Nombre)].NumeroFuncionario, t.TipoObra))
            .OrderBy(g => g.Key.TipoObra).ThenBy(g => g.Key.Numero.Length).ThenBy(g => g.Key.Numero);

        foreach (var g in porFuncionario)
        {
            var cantidad = g.Sum(t => t.Cantidad);
            var nombre = CatalogoMaestros.Actual.Funcionario(g.Key.Numero)?.Nombre ?? g.First().Nombre;
            Tickets.Add(new TicketFila(g.Key.Numero, nombre, g.Key.TipoObra, cantidad, Math.Round(cantidad * ValorTicket, 2, MidpointRounding.AwayFromZero)));
        }
    }

    // Quien cobra de un tipo y no tiene horas de ese tipo recibe 1 hora en la SEDE del tipo:
    // - Industria y Comercio: son administrativos (SEDE de oficina).
    // - Metalúrgica y Administración: no vienen en el archivo de horas (obra METALURGICA / Sede A).
    // - Otros tipos: solo si su ficha tiene marcado "1 h SEDE" (ej. Gastón De León). El resto queda listado como aviso.
    private void CalcularHorasSede()
    {
        HorasSede.Clear();
        SiguenSinHoras.Clear();

        var conHoras = Personas.Where(p => p.Horas > 0).Select(p => (p.NumeroFuncionario, p.TipoObra)).ToHashSet();
        foreach (var (numero, tipo) in _conPago.Where(c => !conHoras.Contains(c)).OrderBy(c => c.Tipo).ThenBy(c => c.Numero))
        {
            var funcionario = CatalogoMaestros.Actual.Funcionario(numero);
            var nombre = funcionario?.Nombre ?? numero;
            var motivo = tipo switch
            {
                TipoObra.IndustriaYComercio => "Administrativo de Industria y Comercio",
                TipoObra.Metalurgica => "Metalúrgica (no viene en el archivo de horas)",
                TipoObra.Administracion => "Administración (no viene en el archivo de horas)",
                _ when funcionario?.HoraFijaSede == true => "Marcado '1 h SEDE' en su ficha",
                _ => null
            };
            var sede = motivo is null ? null : ObraSede(tipo);

            if (sede is not null)
                HorasSede.Add(new HoraSedeFila(numero, nombre, tipo, sede.NumeroObra, sede.Nombre, motivo!));
            else
                SiguenSinHoras.Add($"{numero} {nombre} ({TipoObraParser.Texto(tipo)})" + (motivo is not null ? ": no hay obra SEDE activa de este tipo" : string.Empty));
        }
    }

    // Obra SEDE activa del tipo (en Industria y Comercio, la de oficina). Metalúrgica no tiene SEDE: se usa su obra activa.
    private static ObraEdicionDto? ObraSede(TipoObra tipo)
    {
        var activas = CatalogoMaestros.Actual.Obras.Values.Where(o => o.Activa && o.TipoObra == tipo).ToList();
        var sedes = activas
            .Where(o => o.Nombre.Contains("SEDE", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.Nombre.Contains("OFICINA", StringComparison.OrdinalIgnoreCase))
            .ThenBy(o => o.NumeroObra)
            .ToList();
        if (sedes.Count == 0 && tipo == TipoObra.Metalurgica)
            return activas.OrderBy(o => o.NumeroObra).FirstOrDefault();
        return sedes.FirstOrDefault();
    }

    // Próximo número libre para obras nuevas (las numeradas por debajo de 1000).
    private static int ProximoNumeroObra() =>
        CatalogoMaestros.Actual.Obras.Keys.Select(k => int.TryParse(k, out var n) ? n : 0).Where(n => n < 1000).DefaultIfEmpty(0).Max() + 1;

    private static decimal Equivalentes(FilaHorasDto f) => Domain.Entities.HoraMensual.CalcularEquivalentes(f.Comunes, f.Extras, f.Lluvia);
}
