using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Barraca.RRHH.Application.DTOs;

namespace Barraca.RRHH.App.Mac.Windows;

public partial class InconsistenciasWindow : Window
{
    private readonly Func<Task<IReadOnlyList<ConsistenciaFuncionarioErrorDto>>> _cargarErrores;
    private readonly Func<string, int, Task<IReadOnlyList<ConsistenciaDetalleRegistroDto>>> _cargarDetalle;
    private readonly Func<ConsistenciaDetalleRegistroDto, Task> _guardarDetalle;

    public ObservableCollection<ConsistenciaFuncionarioErrorDto> Errores { get; } = new();
    public ObservableCollection<DetalleEditableRow> Detalles { get; } = new();

    public InconsistenciasWindow()
        : this(
            DateTime.Now.ToString("yyyy-MM"),
            () => Task.FromResult<IReadOnlyList<ConsistenciaFuncionarioErrorDto>>(Array.Empty<ConsistenciaFuncionarioErrorDto>()),
            (_, _) => Task.FromResult<IReadOnlyList<ConsistenciaDetalleRegistroDto>>(Array.Empty<ConsistenciaDetalleRegistroDto>()),
            _ => Task.CompletedTask)
    {
    }

    public InconsistenciasWindow(
        string periodo,
        Func<Task<IReadOnlyList<ConsistenciaFuncionarioErrorDto>>> cargarErrores,
        Func<string, int, Task<IReadOnlyList<ConsistenciaDetalleRegistroDto>>> cargarDetalle,
        Func<ConsistenciaDetalleRegistroDto, Task> guardarDetalle)
    {
        InitializeComponent();
        DataContext = this;

        _cargarErrores = cargarErrores;
        _cargarDetalle = cargarDetalle;
        _guardarDetalle = guardarDetalle;

        TxtTitulo.Text = $"Periodo {periodo}: inconsistencias detectadas";
        TxtSubtitulo.Text = "Corrige aquí mismo en formato planilla y guarda. No necesitas volver a editar Excel ni reimportar.";

        Opened += async (_, _) => await RefrescarAsync();
    }

    private async Task RefrescarAsync()
    {
        var errores = await _cargarErrores();

        Errores.Clear();
        Detalles.Clear();
        foreach (var error in errores)
        {
            Errores.Add(error);
            var detalleError = await _cargarDetalle(error.Tipo, error.FuncionarioId);
            foreach (var item in detalleError)
            {
                var row = new DetalleEditableRow
                {
                    TipoError = error.Tipo,
                    NumeroFuncionario = error.NumeroFuncionario,
                    NombreFuncionario = error.NombreFuncionario,
                    Origen = item.Origen,
                    RegistroId = item.RegistroId,
                    FilaOrigen = item.FilaOrigen,
                    Referencia = item.Referencia,
                    CategoriaOTipo = item.CategoriaOTipo,
                    MontoOHoras = item.MontoOHoras,
                    Observacion = item.Observacion,
                    NumeroFuncionarioDestino = item.NumeroFuncionarioDestino,
                    BaseRowBackground = Detalles.Count % 2 == 0 ? "#FFFFFF" : "#F8FAFC"
                };

                row.MarcarComoGuardado();
                Detalles.Add(row);
            }
        }

        if (Errores.Count > 0)
            TxtEstado.Text = $"Errores pendientes: {Errores.Count}. Filas cargadas en planilla: {Detalles.Count}.";
        else
            TxtEstado.Text = "Sin inconsistencias.";
    }

    private async void Refrescar_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await RefrescarAsync();
    }

    private void Cerrar_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private async void GuardarFila_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not DetalleEditableRow fila)
            return;

        try
        {
            await _guardarDetalle(fila.ToDto());
            fila.MarcarComoGuardado();
            await RefrescarAsync();
            TxtEstado.Text = "Fila guardada correctamente.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text = $"Error guardando fila: {ex.Message}";
        }
    }

    private async void GuardarTodos_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var pendientes = Detalles.Where(x => x.IsDirty).ToList();
            foreach (var fila in pendientes)
                await _guardarDetalle(fila.ToDto());

            await RefrescarAsync();
            TxtEstado.Text = pendientes.Count == 0
                ? "No había cambios pendientes por guardar."
                : $"Se guardaron {pendientes.Count} filas editadas.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text = $"Error guardando todas las filas: {ex.Message}";
        }
    }

    private void BodyScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (HeaderScroll is null)
            return;

        HeaderScroll.Offset = new Vector(e.Offset.X, 0);
    }
}

public sealed class DetalleEditableRow
    : INotifyPropertyChanged
{
    private string _categoriaOTipo = string.Empty;
    private decimal _montoOHoras;
    private string _observacion = string.Empty;
    private string _numeroFuncionarioDestino = string.Empty;
    private string _baseRowBackground = "#FFFFFF";
    private bool _isDirty;

    private string _originalCategoriaOTipo = string.Empty;
    private decimal _originalMontoOHoras;
    private string _originalObservacion = string.Empty;
    private string _originalNumeroFuncionarioDestino = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TipoError { get; set; } = string.Empty;
    public string NumeroFuncionario { get; set; } = string.Empty;
    public string NombreFuncionario { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public int RegistroId { get; set; }
    public int FilaOrigen { get; set; }
    public string Referencia { get; set; } = string.Empty;

    public string CategoriaOTipo
    {
        get => _categoriaOTipo;
        set
        {
            if (SetField(ref _categoriaOTipo, value))
                EvaluarDirty();
        }
    }

    public decimal MontoOHoras
    {
        get => _montoOHoras;
        set
        {
            if (SetField(ref _montoOHoras, value))
                EvaluarDirty();
        }
    }

    public string Observacion
    {
        get => _observacion;
        set
        {
            if (SetField(ref _observacion, value))
                EvaluarDirty();
        }
    }

    public string NumeroFuncionarioDestino
    {
        get => _numeroFuncionarioDestino;
        set
        {
            if (SetField(ref _numeroFuncionarioDestino, value))
                EvaluarDirty();
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetField(ref _isDirty, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowBackground)));
        }
    }

    public string BaseRowBackground
    {
        get => _baseRowBackground;
        set
        {
            if (SetField(ref _baseRowBackground, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowBackground)));
        }
    }

    public string RowBackground => IsDirty ? "#FEF3C7" : BaseRowBackground;

    public void MarcarComoGuardado()
    {
        _originalCategoriaOTipo = _categoriaOTipo;
        _originalMontoOHoras = _montoOHoras;
        _originalObservacion = _observacion;
        _originalNumeroFuncionarioDestino = _numeroFuncionarioDestino;
        IsDirty = false;
    }

    private void EvaluarDirty()
    {
        IsDirty =
            !string.Equals(_categoriaOTipo, _originalCategoriaOTipo, StringComparison.Ordinal) ||
            _montoOHoras != _originalMontoOHoras ||
            !string.Equals(_observacion, _originalObservacion, StringComparison.Ordinal) ||
            !string.Equals(_numeroFuncionarioDestino, _originalNumeroFuncionarioDestino, StringComparison.Ordinal);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public ConsistenciaDetalleRegistroDto ToDto() => new()
    {
        Origen = Origen,
        RegistroId = RegistroId,
        FilaOrigen = FilaOrigen,
        Referencia = Referencia,
        CategoriaOTipo = CategoriaOTipo,
        MontoOHoras = MontoOHoras,
        Observacion = Observacion,
        NumeroFuncionarioDestino = NumeroFuncionarioDestino
    };
}
