using System.Windows;
using System.Windows.Controls;
using Barraca.RRHH.App.Helpers;
using Barraca.RRHH.App.ViewModels.Carga;

namespace Barraca.RRHH.App.Windows;

// Muestra cómo se empareja cada fila de ábaco con un funcionario antes de cargarla en Pagos del mes.
public partial class CargaAbacoWindow : Window
{
    private readonly CargaAbacoViewModel _vm;

    public CargaAbacoWindow(CargaAbacoViewModel vm)
    {
        InitializeComponent();
        VentanaHelper.AjustarAPantalla(this);
        _vm = vm;
        DataContext = vm;
    }

    private void Cargar_Click(object sender, RoutedEventArgs e)
    {
        dgAbaco.CommitEdit(DataGridEditingUnit.Row, true);

        if (_vm.HayErrores)
        {
            _vm.SoloARevisar = true;
            MessageBox.Show("Hay filas con problema (en rojo). Corrija el N° de funcionario o marque 'Crear' antes de cargar.",
                "Cargar planilla", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
