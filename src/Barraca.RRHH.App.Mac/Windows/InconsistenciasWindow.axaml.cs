using System.Collections.ObjectModel;
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
                Detalles.Add(new DetalleEditableRow
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
                    NumeroFuncionarioDestino = item.NumeroFuncionarioDestino
                });
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
            foreach (var fila in Detalles)
                await _guardarDetalle(fila.ToDto());

            await RefrescarAsync();
            TxtEstado.Text = "Se guardaron todas las filas editadas.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text = $"Error guardando todas las filas: {ex.Message}";
        }
    }
}

public sealed class DetalleEditableRow
{
    public string TipoError { get; set; } = string.Empty;
    public string NumeroFuncionario { get; set; } = string.Empty;
    public string NombreFuncionario { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public int RegistroId { get; set; }
    public int FilaOrigen { get; set; }
    public string Referencia { get; set; } = string.Empty;
    public string CategoriaOTipo { get; set; } = string.Empty;
    public decimal MontoOHoras { get; set; }
    public string Observacion { get; set; } = string.Empty;
    public string NumeroFuncionarioDestino { get; set; } = string.Empty;

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
