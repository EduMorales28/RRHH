using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Barraca.RRHH.App.Mac.ViewModels;
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
}
