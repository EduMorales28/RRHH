using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Barraca.RRHH.App.Mac.ViewModels;
using Barraca.RRHH.App.Mac.Windows;
using System.Linq;

namespace Barraca.RRHH.App.Mac;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async Task<string?> SeleccionarArchivoExcelAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
            return null;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar plantilla Excel",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Excel")
                {
                    Patterns = new[] { "*.xlsx" }
                }
            }
        });

        var selected = files.FirstOrDefault();
        if (selected is null)
            return null;

        return selected.Path.LocalPath;
    }

    private async Task<string?> SeleccionarCarpetaAsync(string titulo)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
            return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = titulo,
            AllowMultiple = false
        });

        var selected = folders.FirstOrDefault();
        return selected?.Path.LocalPath;
    }

    private async void SeleccionarFuncionarios_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.RutaFuncionarios = await SeleccionarArchivoExcelAsync() ?? vm.RutaFuncionarios;
    }

    private async void SeleccionarObras_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.RutaObras = await SeleccionarArchivoExcelAsync() ?? vm.RutaObras;
    }

    private async void SeleccionarHoras_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.RutaHoras = await SeleccionarArchivoExcelAsync() ?? vm.RutaHoras;
    }

    private async void SeleccionarPagos_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.RutaPagos = await SeleccionarArchivoExcelAsync() ?? vm.RutaPagos;
    }

    private async void SeleccionarDestinoReportes_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var selected = await SeleccionarCarpetaAsync("Seleccionar carpeta destino de reportes");
        if (!string.IsNullOrWhiteSpace(selected))
            vm.CarpetaReportes = selected;
    }

    private async void SeleccionarPeriodo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var modal = new SeleccionPeriodoWindow(vm.Periodo);
        await modal.ShowDialog(this);

        if (!string.IsNullOrWhiteSpace(modal.PeriodoSeleccionado))
        {
            vm.Periodo = modal.PeriodoSeleccionado;
            vm.CambiarPeriodoCommand.Execute(null);
        }
    }

    private async Task<bool> TieneInconsistenciasAsync(MainWindowViewModel vm, string operacion)
    {
        var errores = await vm.ValidarConsistenciaAsync();
        if (errores.Count == 0)
            return false;

        vm.Status = $"No se puede {operacion}: hay inconsistencias. Revísalas y corrige antes de continuar.";

        var ventana = new InconsistenciasWindow(vm.Periodo, vm.ValidarConsistenciaAsync, vm.ObtenerDetalleConsistenciaAsync);
        await ventana.ShowDialog(this);

        return true;
    }

    private async void CalcularDistribucionConValidacion_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (await TieneInconsistenciasAsync(vm, "calcular distribución"))
            return;

        await vm.CalcularDistribucionUiAsync();
    }

    private async void GenerarReportesConValidacion_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (await TieneInconsistenciasAsync(vm, "generar reportes"))
            return;

        await vm.GenerarReportesUiAsync();
    }
}
