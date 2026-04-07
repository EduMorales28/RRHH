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
    public ObservableCollection<ConsistenciaDetalleRegistroDto> Detalles { get; } = new();

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
        TxtSubtitulo.Text = "Debes corregir las inconsistencias antes de calcular distribución o generar reportes. Corrige en las planillas y vuelve a importar.";

        Opened += async (_, _) => await RefrescarAsync();
    }

    private async Task RefrescarAsync()
    {
        var errores = await _cargarErrores();

        Errores.Clear();
        Detalles.Clear();
        foreach (var error in errores)
            Errores.Add(error);

        if (Errores.Count > 0)
        {
            LbErrores.SelectedIndex = 0;
            await CargarDetalleSeleccionadoAsync();
            TxtEstado.Text = $"Errores pendientes: {Errores.Count}";
        }
        else
        {
            TxtEstado.Text = "Sin inconsistencias.";
        }
    }

    private async Task CargarDetalleSeleccionadoAsync()
    {
        Detalles.Clear();

        if (LbErrores.SelectedItem is not ConsistenciaFuncionarioErrorDto error)
            return;

        var detalle = await _cargarDetalle(error.Tipo, error.FuncionarioId);
        foreach (var item in detalle)
            Detalles.Add(item);
    }

    private async void Refrescar_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await RefrescarAsync();
    }

    private async void Errores_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        await CargarDetalleSeleccionadoAsync();
    }

    private void Cerrar_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private async void GuardarFila_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not ConsistenciaDetalleRegistroDto fila)
            return;

        try
        {
            await _guardarDetalle(fila);
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
                await _guardarDetalle(fila);

            await RefrescarAsync();
            TxtEstado.Text = "Se guardaron todas las filas editadas.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text = $"Error guardando todas las filas: {ex.Message}";
        }
    }
}
