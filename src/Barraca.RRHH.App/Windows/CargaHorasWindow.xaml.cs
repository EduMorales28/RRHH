using System.Windows;
using System.Windows.Controls;
using Barraca.RRHH.App.Helpers;
using Barraca.RRHH.App.ViewModels.Carga;

namespace Barraca.RRHH.App.Windows;

// Muestra cómo se emparejan funcionarios y obras del archivo de horas antes de cargarlas en Horas del mes.
public partial class CargaHorasWindow : Window
{
    private readonly CargaHorasViewModel _vm;

    public CargaHorasWindow(CargaHorasViewModel vm)
    {
        InitializeComponent();
        VentanaHelper.AjustarAPantalla(this);
        _vm = vm;
        DataContext = vm;
    }

    private void Cargar_Click(object sender, RoutedEventArgs e)
    {
        dgPersonas.CommitEdit(DataGridEditingUnit.Row, true);
        dgObras.CommitEdit(DataGridEditingUnit.Row, true);

        if (_vm.HayErrores)
        {
            MessageBox.Show("Hay funcionarios u obras con problema (en rojo). Corríjalos antes de cargar.",
                "Cargar horas", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_vm.SiguenSinHoras.Count > 0 && MessageBox.Show(
                $"{_vm.SiguenSinHoras.Count} funcionario(s) cobran y quedan sin horas (ver la pestaña 'Con pagos y sin horas'). " +
                "Su costo no se va a poder repartir hasta que tengan horas.\n\n¿Cargar igual?",
                "Cargar horas", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
