using System.Text.RegularExpressions;
using System.Windows;

namespace Barraca.RRHH.App.Windows;

public partial class NuevoMesWindow : Window
{
    private const string SinCopiar = "(no copiar: empezar vacío)";

    public string Periodo { get; private set; } = string.Empty;
    public string? CopiarDe { get; private set; }

    public NuevoMesWindow(string periodoSugerido, IEnumerable<string> periodosConDatos, string? origenSugerido)
    {
        InitializeComponent();
        txtPeriodo.Text = periodoSugerido;

        var opciones = periodosConDatos.ToList();
        foreach (var p in opciones)
            cmbOrigen.Items.Add(p);
        cmbOrigen.Items.Add(SinCopiar);
        cmbOrigen.SelectedItem = origenSugerido is not null && opciones.Contains(origenSugerido) ? origenSugerido : cmbOrigen.Items[0];

        Loaded += (_, _) => { txtPeriodo.Focus(); txtPeriodo.SelectAll(); };
    }

    private void Crear_Click(object sender, RoutedEventArgs e)
    {
        var periodo = txtPeriodo.Text.Trim();
        if (!Regex.IsMatch(periodo, "^\\d{4}-(0[1-9]|1[0-2])$"))
        {
            MessageBox.Show("Escriba el período como AAAA-MM, por ejemplo 2026-10.", "Nuevo mes", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Periodo = periodo;
        CopiarDe = cmbOrigen.SelectedItem as string is { } origen && origen != SinCopiar ? origen : null;
        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
