using System.Windows;

namespace Barraca.RRHH.App.Helpers;

public static class VentanaHelper
{
    // Evita que una ventana sea más grande que la pantalla (según resolución y escala de Windows):
    // si no entra, su barra de título queda afuera y no se puede mover, minimizar ni cerrar.
    public static void AjustarAPantalla(Window ventana)
    {
        var area = SystemParameters.WorkArea;
        ventana.Width = Math.Min(ventana.Width, area.Width);
        ventana.Height = Math.Min(ventana.Height, area.Height);
    }

    // Una ventana que arranca maximizada ignora CenterScreen al restaurarse: se fija la posición a mano.
    public static void CentrarEnPantalla(Window ventana)
    {
        var area = SystemParameters.WorkArea;
        ventana.WindowStartupLocation = WindowStartupLocation.Manual;
        ventana.Left = area.Left + ((area.Width - ventana.Width) / 2);
        ventana.Top = area.Top + ((area.Height - ventana.Height) / 2);
    }
}
