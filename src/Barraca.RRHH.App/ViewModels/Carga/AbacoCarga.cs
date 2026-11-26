using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Helpers;

namespace Barraca.RRHH.App.ViewModels.Carga;

// Una fila de la planilla de ábaco con el funcionario del programa al que se le carga.
public sealed class AbacoFila : ViewModelBase
{
    private string _numeroFuncionario;
    private bool _crearNuevo;
    private string _estado;
    private decimal _retencionFijaAplicada;
    private string _problema = string.Empty;

    public AbacoFila(TipoObra tipoObra, FilaAbacoDto fila, EmparejamientoAbacoDto sugerencia)
    {
        TipoObra = tipoObra;
        Fila = fila;
        _numeroFuncionario = sugerencia.NumeroFuncionario;
        _estado = sugerencia.Estado;
        RequiereRevision = sugerencia.RequiereRevision;
        if (sugerencia.Estado == EstadosAbaco.NoExiste)
        {
            _crearNuevo = true;
            _numeroFuncionario = fila.Codigo;
        }
    }

    public TipoObra TipoObra { get; }
    public FilaAbacoDto Fila { get; }
    public bool RequiereRevision { get; }

    public string Empresa => TipoObra == TipoObra.Construccion ? "Construcción" : "Ind. y Comercio";
    public string Codigo => Fila.Codigo;
    public string NombreAbaco => Fila.Nombre;

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

    // Para empleados que no están en el programa: se crea el funcionario con este número.
    public bool CrearNuevo
    {
        get => _crearNuevo;
        set
        {
            if (!SetProperty(ref _crearNuevo, value))
                return;
            OnPropertyChanged(nameof(NombrePrograma));
            AlCambiar?.Invoke();
        }
    }

    public string Estado
    {
        get => _estado;
        private set => SetProperty(ref _estado, value);
    }

    public string NombrePrograma => CrearNuevo
        ? $"(nuevo) {NombreParaPrograma(Fila.Nombre)}"
        : CatalogoMaestros.Actual.Funcionario(NumeroFuncionario)?.Nombre ?? string.Empty;

    public decimal RetencionFijaAplicada
    {
        get => _retencionFijaAplicada;
        set
        {
            if (!SetProperty(ref _retencionFijaAplicada, value))
                return;
            OnPropertyChanged(nameof(Adelanto));
            OnPropertyChanged(nameof(Retencion));
        }
    }

    // Montos que quedan en Pagos del mes.
    public decimal Adelanto => Fila.Adelantos - RetencionFijaAplicada;
    public decimal Retencion => Fila.RetencionJudicial + RetencionFijaAplicada;
    public decimal Liquido => Fila.Pago;

    public string Problema
    {
        get => _problema;
        private set
        {
            if (SetProperty(ref _problema, value))
                OnPropertyChanged(nameof(TieneError));
        }
    }

    public bool TieneError => Problema.Length > 0;

    internal Action? AlCambiar { get; set; }

    internal void Validar()
    {
        var existe = CatalogoMaestros.Actual.Funcionario(NumeroFuncionario) is not null;
        Problema = NumeroFuncionario.Length == 0 ? "Indique el N° de funcionario o marque 'Crear'."
            : CrearNuevo && existe ? $"El número {NumeroFuncionario} ya existe: escriba otro número para el nuevo o desmarque 'Crear'."
            : !CrearNuevo && !existe ? $"No existe el funcionario {NumeroFuncionario}."
            : !Fila.Cuadra ? "Los montos de la fila no cuadran (revise el PDF)."
            : string.Empty;
    }

    // "SEGREDO MINER, JAVIER" -> "Javier Segredo Miner".
    public static string NombreParaPrograma(string nombreAbaco)
    {
        var partes = nombreAbaco.Split(',', 2);
        var ordenado = partes.Length == 2 ? $"{partes[1].Trim()} {partes[0].Trim()}" : nombreAbaco.Trim();
        return CultureInfo.GetCultureInfo("es-UY").TextInfo.ToTitleCase(ordenado.ToLowerInvariant());
    }
}

public sealed class CargaAbacoViewModel : ViewModelBase
{
    private bool _soloARevisar;

    public CargaAbacoViewModel(string periodo, IReadOnlyList<PlanillaAbacoDto> planillas, IReadOnlyList<EmparejamientoAbacoDto> sugerencias)
    {
        Periodo = periodo;
        Planillas = planillas;
        foreach (var planilla in planillas)
        {
            foreach (var fila in planilla.Filas)
            {
                var sugerencia = sugerencias.First(s => s.TipoObra == planilla.TipoObra && s.Codigo == fila.Codigo);
                Filas.Add(new AbacoFila(planilla.TipoObra, fila, sugerencia) { AlCambiar = Recalcular });
            }
        }

        // Si hay filas para revisar, se abre mostrando solo esas.
        _soloARevisar = Filas.Any(f => f.RequiereRevision);
        Vista = CollectionViewSource.GetDefaultView(Filas);
        Vista.Filter = o => !SoloARevisar || o is AbacoFila { RequiereRevision: true } or AbacoFila { TieneError: true };
        Recalcular();
    }

    public string Periodo { get; }
    public IReadOnlyList<PlanillaAbacoDto> Planillas { get; }
    public ObservableCollection<AbacoFila> Filas { get; } = new();
    public ICollectionView Vista { get; }

    public bool SoloARevisar
    {
        get => _soloARevisar;
        set { if (SetProperty(ref _soloARevisar, value)) Vista.Refresh(); }
    }

    public string ResumenPlanillas => string.Join("\n", Planillas.Select(p =>
        $"{p.Empresa} ({p.Archivo}): {p.Filas.Count} empleados · Nominal {Formato(p.Filas.Sum(f => f.Nominal))} · Aportes {Formato(p.Filas.Sum(f => f.Aportes))} · " +
        $"Adelantos {Formato(p.Filas.Sum(f => f.Adelantos))} · Ret. Jud. {Formato(p.Filas.Sum(f => f.RetencionJudicial))} · Pago {Formato(p.Filas.Sum(f => f.Pago))}  " +
        (p.TotalesVerificados ? "✔ coincide con los totales de la planilla" : "⚠ NO coincide con los totales del pie: revise el PDF")));

    public string ResumenRevision
    {
        get
        {
            var aRevisar = Filas.Count(f => f.RequiereRevision);
            var errores = Filas.Count(f => f.TieneError);
            var nuevos = Filas.Count(f => f.CrearNuevo);
            var fijas = Filas.Where(f => f.RetencionFijaAplicada > 0).ToList();
            return $"{Filas.Count} filas · {Filas.Count - aRevisar} emparejadas solas · {aRevisar} para revisar · {nuevos} funcionario(s) nuevo(s) · {errores} con error" +
                (fijas.Count > 0 ? $"\nRetenciones fijas que se pasan de Adelantos a Retención: {string.Join(", ", fijas.Select(f => $"{f.NombrePrograma} {Formato(f.RetencionFijaAplicada)}"))}" : string.Empty);
        }
    }

    public bool HayErrores => Filas.Any(f => f.TieneError);

    private void Recalcular()
    {
        foreach (var fila in Filas)
            fila.Validar();

        // La retención fija se aplica una sola vez por funcionario, en la primera fila donde entra en Adelantos.
        var aplicadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fila in Filas)
        {
            var fija = fila.CrearNuevo ? 0m : CatalogoMaestros.Actual.Funcionario(fila.NumeroFuncionario)?.RetencionFija ?? 0m;
            var aplica = fija > 0 && fila.Fila.Adelantos >= fija && aplicadas.Add(fila.NumeroFuncionario);
            fila.RetencionFijaAplicada = aplica ? fija : 0m;
        }

        OnPropertyChanged(nameof(ResumenRevision));
        OnPropertyChanged(nameof(HayErrores));
    }

    private static string Formato(decimal valor) => valor.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-UY"));
}
