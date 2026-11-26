using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Barraca.RRHH.App.Helpers;

// Permite pegar (Ctrl+V) un bloque copiado de Excel en una tabla editable, a partir de la celda seleccionada.
// Si el bloque tiene más filas que la tabla, se agregan filas nuevas.
public static class DataGridPegado
{
    public static void Habilitar(DataGrid grid, Func<object> agregarFila, Func<bool> puedeEditar)
    {
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control)
                return;

            // Si se está escribiendo dentro de una celda, se deja el pegado normal del texto.
            if (e.OriginalSource is TextBox { IsReadOnly: false } && EstaEditando(grid))
                return;

            e.Handled = true;
            if (!puedeEditar())
            {
                MessageBox.Show("El período está cerrado: no se puede modificar.", "Pegar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Pegar(grid, agregarFila);
        };
    }

    private static bool EstaEditando(DataGrid grid) =>
        grid.CurrentCell.Column?.GetCellContent(grid.CurrentCell.Item) is TextBox;

    private static void Pegar(DataGrid grid, Func<object> agregarFila)
    {
        var texto = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        var lineas = texto.Replace("\r\n", "\n").Split('\n').ToList();
        while (lineas.Count > 0 && lineas[^1].Length == 0)
            lineas.RemoveAt(lineas.Count - 1);
        if (lineas.Count == 0)
            return;

        grid.CommitEdit(DataGridEditingUnit.Row, true);

        var columnas = grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).ToList();
        var columnaInicial = grid.CurrentCell.Column is { } actual ? columnas.IndexOf(actual) : 0;
        if (columnaInicial < 0)
            columnaInicial = 0;

        var filasExistentes = grid.Items.Cast<object>().Where(i => i != CollectionView.NewItemPlaceholder).ToList();
        var filaInicial = grid.CurrentCell.Item is { } item && item != CollectionView.NewItemPlaceholder
            ? filasExistentes.IndexOf(item)
            : filasExistentes.Count;
        if (filaInicial < 0)
            filaInicial = filasExistentes.Count;

        int celdasPegadas = 0, celdasInvalidas = 0, filasAgregadas = 0;
        for (var i = 0; i < lineas.Count; i++)
        {
            var indiceFila = filaInicial + i;
            object fila;
            if (indiceFila < filasExistentes.Count)
            {
                fila = filasExistentes[indiceFila];
            }
            else
            {
                fila = agregarFila();
                filasAgregadas++;
            }

            var valores = lineas[i].Split('\t');
            for (var j = 0; j < valores.Length && columnaInicial + j < columnas.Count; j++)
            {
                var columna = columnas[columnaInicial + j];
                if (columna.IsReadOnly)
                    continue;

                var propiedad = PropiedadDe(columna, fila);
                if (propiedad is null || !propiedad.CanWrite)
                    continue;

                if (TryConvertir(valores[j].Trim(), propiedad.PropertyType, out var valor))
                {
                    propiedad.SetValue(fila, valor);
                    celdasPegadas++;
                }
                else
                {
                    celdasInvalidas++;
                }
            }
        }

        if (celdasInvalidas > 0)
            MessageBox.Show(
                $"Se pegaron {celdasPegadas} celdas ({filasAgregadas} filas nuevas). {celdasInvalidas} celda(s) no tenían un número válido y se dejaron como estaban.",
                "Pegar", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static PropertyInfo? PropiedadDe(DataGridColumn columna, object fila)
    {
        var ruta = columna switch
        {
            DataGridBoundColumn bound => (bound.Binding as Binding)?.Path?.Path,
            DataGridComboBoxColumn combo => (combo.SelectedItemBinding as Binding)?.Path?.Path,
            _ => null
        };

        return string.IsNullOrEmpty(ruta) ? null : fila.GetType().GetProperty(ruta);
    }

    private static bool TryConvertir(string texto, Type tipo, out object? valor)
    {
        valor = null;
        if (tipo == typeof(string))
        {
            valor = texto;
            return true;
        }

        if (tipo == typeof(decimal))
        {
            if (!NumeroParser.TryParse(texto, out var d))
                return false;
            valor = d;
            return true;
        }

        if (tipo == typeof(bool))
        {
            var t = texto.Trim().ToUpperInvariant();
            valor = t is "SI" or "SÍ" or "S" or "X" or "TRUE" or "1" or "VERDADERO";
            return true;
        }

        return false;
    }
}
