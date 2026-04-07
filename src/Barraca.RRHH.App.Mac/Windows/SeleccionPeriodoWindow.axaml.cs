using System.Globalization;
using Avalonia.Controls;

namespace Barraca.RRHH.App.Mac.Windows;

public partial class SeleccionPeriodoWindow : Window
{
    private sealed class MesOption
    {
        public int Numero { get; init; }
        public string Nombre { get; init; } = string.Empty;
        public override string ToString() => Nombre;
    }

    public string? PeriodoSeleccionado { get; private set; }

    public SeleccionPeriodoWindow()
        : this(DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture))
    {
    }

    public SeleccionPeriodoWindow(string periodoActual)
    {
        InitializeComponent();

        var anioActual = DateTime.Now.Year;
        var anios = Enumerable.Range(anioActual - 5, 11).ToList();
        CbAnio.ItemsSource = anios;

        var meses = Enumerable.Range(1, 12)
            .Select(m => new MesOption
            {
                Numero = m,
                Nombre = CultureInfo.GetCultureInfo("es-UY").DateTimeFormat.GetMonthName(m)
            })
            .ToList();
        CbMes.ItemsSource = meses;

        var basePeriodo = System.Text.RegularExpressions.Regex.IsMatch(periodoActual ?? string.Empty, "^\\d{4}-\\d{2}$")
            ? periodoActual!
            : DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var anio = int.Parse(basePeriodo.Substring(0, 4), CultureInfo.InvariantCulture);
        var mes = int.Parse(basePeriodo.Substring(5, 2), CultureInfo.InvariantCulture);

        CbAnio.SelectedItem = anio;
        CbMes.SelectedItem = meses.FirstOrDefault(x => x.Numero == mes);
    }

    private void Aceptar_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (CbAnio.SelectedItem is not int anio || CbMes.SelectedItem is not MesOption mes)
            return;

        PeriodoSeleccionado = $"{anio}-{mes.Numero:00}";
        Close();
    }

    private void Cancelar_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
