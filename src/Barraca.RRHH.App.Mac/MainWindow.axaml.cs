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

    private async void SeleccionarCarpetaEImportar_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
            return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleccionar carpeta con plantillas"
        });

        var selected = folders.FirstOrDefault();
        if (selected is null)
            return;

        if (DataContext is MainWindowViewModel vm)
            await vm.ImportarDesdeCarpetaAsync(selected.Path.LocalPath);
    }
}
