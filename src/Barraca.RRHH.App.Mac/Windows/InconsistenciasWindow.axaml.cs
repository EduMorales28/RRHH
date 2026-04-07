using System.Collections.ObjectModel;
using Avalonia.Controls;
using Barraca.RRHH.Application.DTOs;

namespace Barraca.RRHH.App.Mac.Windows;

public partial class InconsistenciasWindow : Window
{
    private readonly Func<Task<IReadOnlyList<ConsistenciaFuncionarioErrorDto>>> _cargarErrores;
    private readonly Func<string, int, Task<IReadOnlyList<ConsistenciaDetalleRegistroDto>>> _cargarDetalle;

    public ObservableCollection<ConsistenciaFuncionarioErrorDto> Errores { get; } = new();
    public ObservableCollection<ConsistenciaDetalleRegistroDto> Detalles { get; } = new();

    public InconsistenciasWindow(
        string periodo,
        Func<Task<IReadOnlyList<ConsistenciaFuncionarioErrorDto>>> cargarErrores,
        Func<string, int, Task<IReadOnlyList<ConsistenciaDetalleRegistroDto>>> cargarDetalle)
    {
        InitializeComponent();
        DataContext = this;

        _cargarErrores = cargarErrores;
        _cargarDetalle = cargarDetalle;

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
}
