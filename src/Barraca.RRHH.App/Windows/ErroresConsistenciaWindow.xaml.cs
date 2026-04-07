using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using Barraca.RRHH.Application.Interfaces;

namespace Barraca.RRHH.App.Windows;

public partial class ErroresConsistenciaWindow : Window
{
    private readonly IConsistenciaService _consistenciaService;
    private readonly string _periodo;
    private readonly string _usuario;
    private readonly string _operacion;
    private readonly SemaphoreSlim _serializacion = new(1, 1);
    private bool _actualizandoErrores;

    public ObservableCollection<ErrorConsistenciaRowViewModel> Errores { get; } = new();
    public ObservableCollection<DetalleConsistenciaRowViewModel> Detalles { get; } = new();

    public ErroresConsistenciaWindow(IConsistenciaService consistenciaService, string periodo, string usuario, string operacion)
    {
        InitializeComponent();
        DataContext = this;

        _consistenciaService = consistenciaService;
        _periodo = periodo;
        _usuario = string.IsNullOrWhiteSpace(usuario) ? "admin" : usuario;
        _operacion = string.IsNullOrWhiteSpace(operacion) ? "continuar" : operacion;

        txtTitulo.Text = $"Periodo {_periodo}: inconsistencias detectadas";
        txtSubtitulo.Text = "Para continuar debe corregir cada error. En 'Nro Func. destino' ingrese el numero de funcionario correcto y aplique la correccion.";

        Loaded += async (_, _) => await EjecutarEnSerieAsync(CargarErroresCoreAsync);
    }

    private async Task CargarErroresCoreAsync()
    {
        var errores = await _consistenciaService.ValidarConsistenciaFuncionarioAsync(_periodo);

        _actualizandoErrores = true;
        try
        {
            Errores.Clear();
            Detalles.Clear();

            foreach (var error in errores)
            {
                Errores.Add(new ErrorConsistenciaRowViewModel
                {
                    Tipo = error.Tipo,
                    FuncionarioId = error.FuncionarioId,
                    NumeroFuncionario = error.NumeroFuncionario,
                    NombreFuncionario = error.NombreFuncionario,
                    RegistrosHoras = error.RegistrosHoras,
                    RegistrosPagos = error.RegistrosPagos,
                    TotalHoras = error.TotalHoras,
                    TotalPagos = error.TotalPagos,
                    Mensaje = error.Mensaje
                });
            }

            txtEstado.Text = Errores.Count == 0
                ? $"Sin errores. Puede {_operacion}."
                : $"Errores pendientes: {Errores.Count}. Deben corregirse para {_operacion}.";

            if (Errores.Count > 0)
            {
                dgErrores.SelectedIndex = 0;
                await CargarDetalleErrorSeleccionadoCoreAsync();
            }
        }
        finally
        {
            _actualizandoErrores = false;
        }
    }

    private async Task CargarDetalleErrorSeleccionadoCoreAsync()
    {
        Detalles.Clear();

        if (dgErrores.SelectedItem is not ErrorConsistenciaRowViewModel error)
            return;

        var detalle = await _consistenciaService.ObtenerDetalleErrorAsync(_periodo, error.Tipo, error.FuncionarioId);
        foreach (var item in detalle)
        {
            Detalles.Add(new DetalleConsistenciaRowViewModel
            {
                Origen = item.Origen,
                RegistroId = item.RegistroId,
                FilaOrigen = item.FilaOrigen,
                Referencia = item.Referencia,
                CategoriaOTipo = item.CategoriaOTipo,
                MontoOHoras = item.MontoOHoras,
                Observacion = item.Observacion
            });
        }
    }

    private async void Refrescar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EjecutarEnSerieAsync(CargarErroresCoreAsync);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error refrescando", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CorregirSeleccionado_Click(object sender, RoutedEventArgs e)
    {
        if (dgErrores.SelectedItem is not ErrorConsistenciaRowViewModel error)
        {
            MessageBox.Show("Seleccione un error para corregir.", "Correccion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(error.NumeroFuncionarioDestino))
        {
            MessageBox.Show("Ingrese un numero de funcionario destino para corregir.", "Correccion", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await EjecutarEnSerieAsync(async () =>
            {
                await _consistenciaService.CorregirConsistenciaFuncionarioAsync(
                    _periodo,
                    error.Tipo,
                    error.FuncionarioId,
                    error.NumeroFuncionarioDestino,
                    _usuario);

                await CargarErroresCoreAsync();
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error corrigiendo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CorregirTodos_Click(object sender, RoutedEventArgs e)
    {
        var candidatos = Errores.Where(x => !string.IsNullOrWhiteSpace(x.NumeroFuncionarioDestino)).ToList();
        if (!candidatos.Any())
        {
            MessageBox.Show("No hay errores con destino cargado para corregir.", "Correccion masiva", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var erroresCorreccion = new List<string>();
        var totalOk = 0;

        try
        {
            await EjecutarEnSerieAsync(async () =>
            {
                foreach (var error in candidatos)
                {
                    try
                    {
                        await _consistenciaService.CorregirConsistenciaFuncionarioAsync(
                            _periodo,
                            error.Tipo,
                            error.FuncionarioId,
                            error.NumeroFuncionarioDestino,
                            _usuario);
                        totalOk++;
                    }
                    catch (Exception ex)
                    {
                        erroresCorreccion.Add($"{error.NumeroFuncionario}: {ex.Message}");
                    }
                }

                await CargarErroresCoreAsync();
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error corrigiendo", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var mensaje = $"Correcciones aplicadas: {totalOk}.";
        if (erroresCorreccion.Any())
            mensaje += "\nErrores:\n" + string.Join("\n", erroresCorreccion);

        MessageBox.Show(mensaje, "Correccion masiva", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Errores_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_actualizandoErrores)
            return;

        try
        {
            await EjecutarEnSerieAsync(CargarDetalleErrorSeleccionadoCoreAsync);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error cargando detalle", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Continuar_Click(object sender, RoutedEventArgs e)
    {
        if (Errores.Count > 0)
        {
            MessageBox.Show("Aun existen inconsistencias. Corrija todos los errores antes de continuar.", "Validacion pendiente", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async Task EjecutarEnSerieAsync(Func<Task> accion)
    {
        await _serializacion.WaitAsync();
        try
        {
            await accion();
        }
        finally
        {
            _serializacion.Release();
        }
    }
}

public class ErrorConsistenciaRowViewModel
{
    public string Tipo { get; set; } = string.Empty;
    public int FuncionarioId { get; set; }
    public string NumeroFuncionario { get; set; } = string.Empty;
    public string NombreFuncionario { get; set; } = string.Empty;
    public int RegistrosHoras { get; set; }
    public int RegistrosPagos { get; set; }
    public decimal TotalHoras { get; set; }
    public decimal TotalPagos { get; set; }
    public string NumeroFuncionarioDestino { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
}

public class DetalleConsistenciaRowViewModel
{
    public string Origen { get; set; } = string.Empty;
    public int RegistroId { get; set; }
    public int FilaOrigen { get; set; }
    public string Referencia { get; set; } = string.Empty;
    public string CategoriaOTipo { get; set; } = string.Empty;
    public decimal MontoOHoras { get; set; }
    public string Observacion { get; set; } = string.Empty;
}
