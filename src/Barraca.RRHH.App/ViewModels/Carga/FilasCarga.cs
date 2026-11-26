using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Helpers;

namespace Barraca.RRHH.App.ViewModels.Carga;

public static class OpcionesCarga
{
    public static string[] TiposObra { get; } =
    [
        TipoObraParser.Texto(TipoObra.Construccion),
        TipoObraParser.Texto(TipoObra.IndustriaYComercio),
        TipoObraParser.Texto(TipoObra.Administracion),
        TipoObraParser.Texto(TipoObra.Metalurgica),
        TipoObraParser.Texto(TipoObra.NA)
    ];

    public static string[] TiposPago { get; } = ["RED PAGOS", "EFECTIVO"];
}

// Funcionarios y obras guardados, para completar nombres y validar números mientras se carga.
public sealed class CatalogoMaestros
{
    public static CatalogoMaestros Actual { get; private set; } = new([], []);

    public IReadOnlyDictionary<string, FuncionarioEdicionDto> Funcionarios { get; }
    public IReadOnlyDictionary<string, ObraEdicionDto> Obras { get; }

    private CatalogoMaestros(IEnumerable<FuncionarioEdicionDto> funcionarios, IEnumerable<ObraEdicionDto> obras)
    {
        Funcionarios = funcionarios
            .GroupBy(x => x.NumeroFuncionario.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        Obras = obras
            .GroupBy(x => x.NumeroObra.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public static void Actualizar(IEnumerable<FuncionarioEdicionDto> funcionarios, IEnumerable<ObraEdicionDto> obras) =>
        Actual = new CatalogoMaestros(funcionarios, obras);

    public FuncionarioEdicionDto? Funcionario(string? numero) =>
        Funcionarios.TryGetValue((numero ?? string.Empty).Trim(), out var f) ? f : null;

    public ObraEdicionDto? Obra(string? numero) =>
        Obras.TryGetValue((numero ?? string.Empty).Trim(), out var o) ? o : null;
}

public abstract class FilaEdicionBase : ViewModelBase
{
    private bool _modificada;
    private string _problema = string.Empty;
    private bool _tieneError;
    private bool _cargando;

    public int Id { get; set; }
    public bool EsNueva => Id == 0;

    public bool Modificada
    {
        get => _modificada;
        set => SetProperty(ref _modificada, value);
    }

    public string Problema
    {
        get => _problema;
        private set => SetProperty(ref _problema, value);
    }

    public bool TieneError
    {
        get => _tieneError;
        private set => SetProperty(ref _tieneError, value);
    }

    public bool TieneAviso => !TieneError && Problema.Length > 0;

    // Texto usado por el buscador de la tabla.
    public abstract string TextoBusqueda { get; }

    // Fila agregada y nunca completada: se descarta al guardar.
    public abstract bool EstaVacia { get; }

    protected void Editar<T>(ref T campo, T valor, [System.Runtime.CompilerServices.CallerMemberName] string? nombre = null)
    {
        if (!SetProperty(ref campo, valor, nombre))
            return;

        if (!_cargando)
            Modificada = true;
        Recalcular();
    }

    // Asigna valores iniciales sin marcar la fila como modificada.
    public void Inicializar(Action asignar)
    {
        _cargando = true;
        try { asignar(); }
        finally { _cargando = false; }
        Modificada = false;
        Recalcular();
    }

    public void Recalcular()
    {
        var (error, aviso) = Validar();
        TieneError = error is not null;
        Problema = error ?? aviso ?? string.Empty;
        OnPropertyChanged(nameof(TieneAviso));
        AlRecalcular();
    }

    protected abstract (string? Error, string? Aviso) Validar();

    protected virtual void AlRecalcular() { }
}

public sealed class PagoFila : FilaEdicionBase
{
    private string _numeroFuncionario = string.Empty;
    private string _tipoObraTexto = OpcionesCarga.TiposObra[0];
    private string _cliente = string.Empty;
    private decimal _adelanto;
    private decimal _liquido;
    private decimal _retencion;
    private string _observacion = string.Empty;

    public string NumeroFuncionario { get => _numeroFuncionario; set => Editar(ref _numeroFuncionario, (value ?? string.Empty).Trim()); }
    public string TipoObraTexto { get => _tipoObraTexto; set => Editar(ref _tipoObraTexto, value ?? string.Empty); }
    public string Cliente { get => EsAdministracion ? _cliente : "Almirtaun"; set => Editar(ref _cliente, value ?? string.Empty); }
    public decimal Adelanto { get => _adelanto; set => Editar(ref _adelanto, value); }
    public decimal Liquido { get => _liquido; set => Editar(ref _liquido, value); }
    public decimal Retencion { get => _retencion; set => Editar(ref _retencion, value); }
    public string Observacion { get => _observacion; set => Editar(ref _observacion, value ?? string.Empty); }

    public string Nombre => CatalogoMaestros.Actual.Funcionario(NumeroFuncionario)?.Nombre ?? string.Empty;
    public decimal Total => Adelanto + Liquido + Retencion;
    public TipoObra TipoObra => TipoObraParser.Parse(TipoObraTexto);
    public bool EsAdministracion => TipoObra == TipoObra.Administracion && TipoObraParser.EsReconocido(TipoObraTexto);
    public string TipoPago => TipoObra == TipoObra.NA ? "Efectivo" : "RedPagos";

    public override string TextoBusqueda => $"{NumeroFuncionario} {Nombre} {TipoObraTexto} {Observacion}";
    public override bool EstaVacia => NumeroFuncionario.Length == 0 && Total == 0 && Observacion.Length == 0;

    protected override (string? Error, string? Aviso) Validar()
    {
        if (NumeroFuncionario.Length == 0)
            return ("Falta el N° de funcionario.", null);
        var funcionario = CatalogoMaestros.Actual.Funcionario(NumeroFuncionario);
        if (funcionario is null)
            return ($"No existe el funcionario {NumeroFuncionario}. Agréguelo en Funcionarios.", null);
        if (!TipoObraParser.EsReconocido(TipoObraTexto))
            return ("Elija el tipo de obra.", null);
        if (Adelanto < 0 || Retencion < 0)
            return ("Adelanto o retención negativos.", null);

        if (Liquido < 0)
            return (null, "Pago negativo: en RedPagos se descuenta del otro pago del funcionario.");
        if (!funcionario.Activo)
            return (null, "Funcionario marcado como inactivo.");
        if (Liquido > 0 && TipoObra != TipoObra.NA && funcionario.Banco.Length == 0 && funcionario.CuentaNueva.Length == 0 && funcionario.CuentaVieja.Length == 0)
            return (null, "Cobra por RedPagos y no tiene cuenta cargada.");
        if (Retencion > 0 && funcionario.ResponsableRetencion.Length == 0)
            return (null, "Tiene retención y no tiene responsable de retención.");
        return (null, null);
    }

    protected override void AlRecalcular()
    {
        OnPropertyChanged(nameof(Nombre));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TipoPago));
        OnPropertyChanged(nameof(Cliente));
        OnPropertyChanged(nameof(EsAdministracion));
    }

    public static PagoFila Desde(PagoEdicionDto dto)
    {
        var fila = new PagoFila { Id = dto.Id };
        fila.Inicializar(() =>
        {
            fila.NumeroFuncionario = dto.NumeroFuncionario;
            fila.TipoObraTexto = TipoObraParser.Texto(dto.TipoObra);
            fila.Cliente = dto.Cliente;
            fila.Adelanto = dto.Adelanto;
            fila.Liquido = dto.Liquido;
            fila.Retencion = dto.Retencion;
            fila.Observacion = dto.Observacion;
        });
        return fila;
    }

    public PagoEdicionDto ADto() => new()
    {
        Id = Id,
        NumeroFuncionario = NumeroFuncionario,
        TipoObra = TipoObra,
        Cliente = _cliente,
        Adelanto = Adelanto,
        Liquido = Liquido,
        Retencion = Retencion,
        Observacion = Observacion
    };
}

public sealed class HoraFila : FilaEdicionBase
{
    private string _numeroFuncionario = string.Empty;
    private string _numeroObra = string.Empty;
    private decimal _horasComunes;
    private decimal _horasExtras;
    private decimal _horasLluvia;

    public string NumeroFuncionario { get => _numeroFuncionario; set => Editar(ref _numeroFuncionario, (value ?? string.Empty).Trim()); }
    public string NumeroObra { get => _numeroObra; set => Editar(ref _numeroObra, (value ?? string.Empty).Trim()); }
    public decimal HorasComunes { get => _horasComunes; set => Editar(ref _horasComunes, value); }
    public decimal HorasExtras { get => _horasExtras; set => Editar(ref _horasExtras, value); }
    public decimal HorasLluvia { get => _horasLluvia; set => Editar(ref _horasLluvia, value); }

    public string Nombre => CatalogoMaestros.Actual.Funcionario(NumeroFuncionario)?.Nombre ?? string.Empty;
    public string Categoria => CatalogoMaestros.Actual.Funcionario(NumeroFuncionario)?.Categoria ?? string.Empty;
    public string NombreObra => CatalogoMaestros.Actual.Obra(NumeroObra)?.Nombre ?? string.Empty;
    public string TipoObraTexto => CatalogoMaestros.Actual.Obra(NumeroObra) is { } o ? TipoObraParser.Texto(o.TipoObra) : string.Empty;
    public decimal HorasEquivalentes => Domain.Entities.HoraMensual.CalcularEquivalentes(HorasComunes, HorasExtras, HorasLluvia);

    public override string TextoBusqueda => $"{NumeroFuncionario} {Nombre} {NumeroObra} {NombreObra} {TipoObraTexto}";
    public override bool EstaVacia => NumeroFuncionario.Length == 0 && NumeroObra.Length == 0 && HorasEquivalentes == 0;

    protected override (string? Error, string? Aviso) Validar()
    {
        if (NumeroFuncionario.Length == 0)
            return ("Falta el N° de funcionario.", null);
        var funcionario = CatalogoMaestros.Actual.Funcionario(NumeroFuncionario);
        if (funcionario is null)
            return ($"No existe el funcionario {NumeroFuncionario}. Agréguelo en Funcionarios.", null);
        if (NumeroObra.Length == 0)
            return ("Falta el N° de obra.", null);
        var obra = CatalogoMaestros.Actual.Obra(NumeroObra);
        if (obra is null)
            return ($"No existe la obra {NumeroObra}. Agréguela en Obras.", null);
        if (HorasComunes < 0 || HorasExtras < 0 || HorasLluvia < 0)
            return ("Hay horas negativas.", null);

        if (!funcionario.Activo)
            return (null, "Funcionario marcado como inactivo.");
        if (!obra.Activa)
            return (null, "Obra marcada como inactiva.");
        return (null, null);
    }

    protected override void AlRecalcular()
    {
        OnPropertyChanged(nameof(Nombre));
        OnPropertyChanged(nameof(Categoria));
        OnPropertyChanged(nameof(NombreObra));
        OnPropertyChanged(nameof(TipoObraTexto));
        OnPropertyChanged(nameof(HorasEquivalentes));
    }

    public static HoraFila Desde(HoraEdicionDto dto)
    {
        var fila = new HoraFila { Id = dto.Id };
        fila.Inicializar(() =>
        {
            fila.NumeroFuncionario = dto.NumeroFuncionario;
            fila.NumeroObra = dto.NumeroObra;
            fila.HorasComunes = dto.HorasComunes;
            fila.HorasExtras = dto.HorasExtras;
            fila.HorasLluvia = dto.HorasLluvia;
        });
        return fila;
    }

    public HoraEdicionDto ADto() => new()
    {
        Id = Id,
        NumeroFuncionario = NumeroFuncionario,
        NumeroObra = NumeroObra,
        HorasComunes = HorasComunes,
        HorasExtras = HorasExtras,
        HorasLluvia = HorasLluvia
    };
}

public sealed class FuncionarioFila : FilaEdicionBase
{
    private string _numero = string.Empty;
    private string _nombre = string.Empty;
    private string _categoria = string.Empty;
    private string _tipoPago = string.Empty;
    private string _banco = string.Empty;
    private string _cuentaNueva = string.Empty;
    private string _cuentaVieja = string.Empty;
    private string _responsable = string.Empty;
    private string _bancoRetencion = string.Empty;
    private string _cuentaRetencion = string.Empty;
    private bool _activo = true;
    private decimal _retencionFija;
    private bool _horaFijaSede;

    public string NumeroFuncionario { get => _numero; set { if (EsNueva || _numero.Length == 0) Editar(ref _numero, (value ?? string.Empty).Trim()); } }
    public string Nombre { get => _nombre; set => Editar(ref _nombre, value ?? string.Empty); }
    public string Categoria { get => _categoria; set => Editar(ref _categoria, value ?? string.Empty); }
    public string TipoPago { get => _tipoPago; set => Editar(ref _tipoPago, value ?? string.Empty); }
    public string Banco { get => _banco; set => Editar(ref _banco, value ?? string.Empty); }
    public string CuentaNueva { get => _cuentaNueva; set => Editar(ref _cuentaNueva, value ?? string.Empty); }
    public string CuentaVieja { get => _cuentaVieja; set => Editar(ref _cuentaVieja, value ?? string.Empty); }
    public string ResponsableRetencion { get => _responsable; set => Editar(ref _responsable, value ?? string.Empty); }
    public string BancoRetencion { get => _bancoRetencion; set => Editar(ref _bancoRetencion, value ?? string.Empty); }
    public string CuentaRetencion { get => _cuentaRetencion; set => Editar(ref _cuentaRetencion, value ?? string.Empty); }
    public bool Activo { get => _activo; set => Editar(ref _activo, value); }
    // Retención de monto fijo: al cargar la planilla de ábaco se descuenta de Adelantos y pasa a Retención.
    public decimal RetencionFija { get => _retencionFija; set => Editar(ref _retencionFija, value); }
    // Si cobra y no tiene horas en el mes, al cargar las horas se le pone 1 hora en la obra SEDE del tipo.
    public bool HoraFijaSede { get => _horaFijaSede; set => Editar(ref _horaFijaSede, value); }

    public bool NumeroEditable => EsNueva;
    public override string TextoBusqueda => $"{NumeroFuncionario} {Nombre} {Categoria} {Banco}";
    public override bool EstaVacia => NumeroFuncionario.Length == 0 && Nombre.Trim().Length == 0;

    protected override (string? Error, string? Aviso) Validar()
    {
        if (NumeroFuncionario.Length == 0)
            return ("Falta el número.", null);
        if (Nombre.Trim().Length == 0)
            return ("Falta el nombre.", null);
        if (EsNueva && CatalogoMaestros.Actual.Funcionario(NumeroFuncionario) is not null)
            return ($"El número {NumeroFuncionario} ya existe.", null);
        if (RetencionFija < 0)
            return ("La retención fija no puede ser negativa.", null);

        if (RetencionFija > 0 && ResponsableRetencion.Trim().Length == 0)
            return (null, "Tiene retención fija y no tiene responsable de retención.");
        if (Categoria.Trim().Length == 0)
            return (null, "Sin categoría (se usa en la distribución).");
        if (TipoPago.Contains("RED", StringComparison.OrdinalIgnoreCase) && CuentaNueva.Trim().Length == 0 && CuentaVieja.Trim().Length == 0)
            return (null, "Cobra por RedPagos y no tiene cuenta.");
        return (null, null);
    }

    public static FuncionarioFila Desde(FuncionarioEdicionDto dto)
    {
        var fila = new FuncionarioFila { Id = dto.Id };
        fila.Inicializar(() =>
        {
            fila._numero = dto.NumeroFuncionario;
            fila.Nombre = dto.Nombre;
            fila.Categoria = dto.Categoria;
            fila.TipoPago = dto.TipoPago;
            fila.Banco = dto.Banco;
            fila.CuentaNueva = dto.CuentaNueva;
            fila.CuentaVieja = dto.CuentaVieja;
            fila.ResponsableRetencion = dto.ResponsableRetencion;
            fila.BancoRetencion = dto.BancoRetencion;
            fila.CuentaRetencion = dto.CuentaRetencion;
            fila.RetencionFija = dto.RetencionFija;
            fila.HoraFijaSede = dto.HoraFijaSede;
            fila.Activo = dto.Activo;
        });
        return fila;
    }

    public FuncionarioEdicionDto ADto() => new()
    {
        Id = Id,
        NumeroFuncionario = NumeroFuncionario,
        Nombre = Nombre,
        Categoria = Categoria,
        TipoPago = TipoPago,
        Banco = Banco,
        CuentaNueva = CuentaNueva,
        CuentaVieja = CuentaVieja,
        ResponsableRetencion = ResponsableRetencion,
        BancoRetencion = BancoRetencion,
        CuentaRetencion = CuentaRetencion,
        RetencionFija = RetencionFija,
        HoraFijaSede = HoraFijaSede,
        Activo = Activo
    };
}

public sealed class ObraFila : FilaEdicionBase
{
    private string _numero = string.Empty;
    private string _nombre = string.Empty;
    private string _tipoObraTexto = OpcionesCarga.TiposObra[0];
    private string _cliente = "Almirtaun";
    private bool _activa = true;

    public string NumeroObra { get => _numero; set { if (EsNueva || _numero.Length == 0) Editar(ref _numero, (value ?? string.Empty).Trim()); } }
    public string Nombre { get => _nombre; set => Editar(ref _nombre, value ?? string.Empty); }
    public string TipoObraTexto { get => _tipoObraTexto; set => Editar(ref _tipoObraTexto, value ?? string.Empty); }
    public string Cliente { get => _cliente; set => Editar(ref _cliente, value ?? string.Empty); }
    public bool Activa { get => _activa; set => Editar(ref _activa, value); }

    public bool NumeroEditable => EsNueva;
    public override string TextoBusqueda => $"{NumeroObra} {Nombre} {TipoObraTexto} {Cliente}";
    public override bool EstaVacia => NumeroObra.Length == 0 && Nombre.Trim().Length == 0;

    protected override (string? Error, string? Aviso) Validar()
    {
        if (NumeroObra.Length == 0)
            return ("Falta el número.", null);
        if (Nombre.Trim().Length == 0)
            return ("Falta el nombre.", null);
        if (!TipoObraParser.EsReconocido(TipoObraTexto))
            return ("Elija el tipo de obra.", null);
        if (EsNueva && CatalogoMaestros.Actual.Obra(NumeroObra) is not null)
            return ($"El número {NumeroObra} ya existe.", null);
        return (null, null);
    }

    public static ObraFila Desde(ObraEdicionDto dto)
    {
        var fila = new ObraFila { Id = dto.Id };
        fila.Inicializar(() =>
        {
            fila._numero = dto.NumeroObra;
            fila.Nombre = dto.Nombre;
            fila.TipoObraTexto = TipoObraParser.Texto(dto.TipoObra);
            fila.Cliente = dto.Cliente;
            fila.Activa = dto.Activa;
        });
        return fila;
    }

    public ObraEdicionDto ADto() => new()
    {
        Id = Id,
        NumeroObra = NumeroObra,
        Nombre = Nombre,
        TipoObra = TipoObraParser.Parse(TipoObraTexto),
        Cliente = Cliente,
        Activa = Activa
    };
}
