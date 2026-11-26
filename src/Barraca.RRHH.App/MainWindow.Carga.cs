using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Barraca.RRHH.App.Helpers;
using Barraca.RRHH.App.ViewModels;

namespace Barraca.RRHH.App;

// Pestañas de carga del mes: pagos, horas, funcionarios y obras.
public partial class MainWindow
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    private void HabilitarCarga()
    {
        Loaded += (_, _) =>
        {
            if (Vm is not { } vm)
                return;

            DataGridPegado.Habilitar(dgPagos, () => vm.Pagos.AgregarFila(), () => vm.Pagos.PuedeEditar);
            DataGridPegado.Habilitar(dgHoras, () => vm.Horas.AgregarFila(), () => vm.Horas.PuedeEditar);
            DataGridPegado.Habilitar(dgFuncionarios, () => vm.FuncionariosEdicion.AgregarFila(), () => true);
            DataGridPegado.Habilitar(dgObras, () => vm.ObrasEdicion.AgregarFila(), () => true);
        };
    }

    // Confirma la celda que se está editando antes de guardar.
    private static void TerminarEdicion(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private async void GuardarPagos_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        TerminarEdicion(dgPagos);
        await vm.GuardarPagosAsync();
    }

    private async void GuardarHoras_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        TerminarEdicion(dgHoras);
        await vm.GuardarHorasAsync();
    }

    private async void GuardarFuncionarios_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        TerminarEdicion(dgFuncionarios);
        await vm.GuardarFuncionariosAsync();
    }

    private async void GuardarObras_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        TerminarEdicion(dgObras);
        await vm.GuardarObrasAsync();
    }

    private void AgregarPago_Click(object sender, RoutedEventArgs e) => AgregarYEditar(dgPagos, () => Vm!.Pagos.AgregarFila());
    private void AgregarHora_Click(object sender, RoutedEventArgs e) => AgregarYEditar(dgHoras, () => Vm!.Horas.AgregarFila());
    private void AgregarFuncionario_Click(object sender, RoutedEventArgs e) => AgregarYEditar(dgFuncionarios, () => Vm!.FuncionariosEdicion.AgregarFila());
    private void AgregarObra_Click(object sender, RoutedEventArgs e) => AgregarYEditar(dgObras, () => Vm!.ObrasEdicion.AgregarFila());

    private static void AgregarYEditar(DataGrid grid, Func<object> agregar)
    {
        TerminarEdicion(grid);
        var fila = agregar();
        grid.SelectedItem = fila;
        grid.ScrollIntoView(fila);
        grid.CurrentCell = new DataGridCellInfo(fila, grid.Columns[0]);
        grid.Focus();
        grid.BeginEdit();
    }

    private void EliminarPagos_Click(object sender, RoutedEventArgs e) => Eliminar(dgPagos, items => Vm!.Pagos.EliminarFilas(items));
    private void EliminarHoras_Click(object sender, RoutedEventArgs e) => Eliminar(dgHoras, items => Vm!.Horas.EliminarFilas(items));
    private void EliminarFuncionarios_Click(object sender, RoutedEventArgs e) => Eliminar(dgFuncionarios, items => Vm!.FuncionariosEdicion.EliminarFilas(items));
    private void EliminarObras_Click(object sender, RoutedEventArgs e) => Eliminar(dgObras, items => Vm!.ObrasEdicion.EliminarFilas(items));

    private static void Eliminar(DataGrid grid, Action<IEnumerable<object>> eliminar)
    {
        grid.CancelEdit();
        var seleccion = grid.SelectedItems.Cast<object>().Where(x => x != System.Windows.Data.CollectionView.NewItemPlaceholder).ToList();
        if (seleccion.Count == 0)
        {
            MessageBox.Show("Seleccione las filas a quitar (clic en el borde izquierdo de la fila; Ctrl o Shift para varias).",
                "Eliminar filas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        eliminar(seleccion);
    }

    private async void Descartar_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (!vm.HayCambiosSinGuardar)
            return;

        if (MessageBox.Show("¿Descartar todos los cambios sin guardar y volver a lo guardado?", "Descartar cambios",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        foreach (var grid in new[] { dgPagos, dgHoras, dgFuncionarios, dgObras })
            grid.CancelEdit();
        await vm.DescartarCambiosAsync();
    }

    private void AbrirCarpetaReportes_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        Directory.CreateDirectory(vm.CarpetaReportes);
        Process.Start(new ProcessStartInfo(vm.CarpetaReportes) { UseShellExecute = true });
    }

    private void AbrirReporte_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (dgReportes.SelectedItem is SimpleRowViewModel { Descripcion: var ruta } && File.Exists(ruta))
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        foreach (var grid in new[] { dgPagos, dgHoras, dgFuncionarios, dgObras })
            TerminarEdicion(grid);

        if (Vm is { HayCambiosSinGuardar: true }
            && MessageBox.Show("Hay cambios sin guardar. Si sale ahora se pierden.\n\n¿Salir igual?", "Cambios sin guardar",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }
}
