using System.Text;
using System.Windows;
using Barraca.RRHH.Application.DTOs;

namespace Barraca.RRHH.App.Windows;

// Muestra los problemas encontrados en una planilla antes de importarla.
// DialogResult = true si el usuario decide importar igual.
public partial class RevisionPlanillaWindow : Window
{
    private readonly IReadOnlyList<ProblemaPlanillaDto> _problemas;

    public RevisionPlanillaWindow(string tipoPlanilla, string archivo, IReadOnlyList<ProblemaPlanillaDto> problemas)
    {
        InitializeComponent();
        Barraca.RRHH.App.Helpers.VentanaHelper.AjustarAPantalla(this);
        _problemas = problemas;

        var errores = problemas.Count(x => x.EsError);
        var avisos = problemas.Count - errores;

        txtTitulo.Text = $"Planilla {tipoPlanilla}: {errores} error(es) y {avisos} aviso(s)";
        txtSubtitulo.Text =
            $"Archivo: {archivo}\n" +
            "En rojo, errores: datos que se van a ignorar o cargar mal si importa así. " +
            "En amarillo, avisos: conviene revisarlos, pero se puede importar. " +
            "Lo recomendable es corregir la planilla en Excel y volver a importarla.";

        dgProblemas.ItemsSource = problemas;
        if (errores > 0)
            btnImportar.Content = "Importar igual (con errores)";
    }

    private void Copiar_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tipo\tFila\tFuncionario / Obra\tProblema\tQué hacer");
        foreach (var p in _problemas)
            sb.AppendLine($"{p.Severidad}\t{p.Fila}\t{p.Funcionario}\t{p.Problema}\t{p.Sugerencia}");

        Clipboard.SetText(sb.ToString());
        MessageBox.Show("Lista copiada. Puede pegarla en Excel.", "Revisión de planilla", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Importar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
