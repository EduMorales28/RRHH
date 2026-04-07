using System.Windows;
using System.Linq;
using System.IO;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Collections.Generic;
using Barraca.RRHH.App.ViewModels;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using System.Windows.Media;
using Barraca.RRHH.Infrastructure.Data;
using Barraca.RRHH.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void CerrarApp_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    public async void ClearAuditIncidents_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show("Borrar TODOS los registros de Auditoría e Incidencias de importación? Esta acción es irreversible.", "Confirmar borrado", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var sp = ((App)System.Windows.Application.Current).Services;
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BarracaDbContext>();

            // Export current data to CSV as a backup before deleting (safe practice)
            MainViewModel vm = DataContext as MainViewModel;
            if (vm != null)
            {
                try
                {
                    var folder = vm.CarpetaReportes ?? AppContext.BaseDirectory;
                    System.IO.Directory.CreateDirectory(folder);

                    var audits = await db.AuditoriaEventos.OrderBy(x => x.FechaHora).ToListAsync();
                    var auditPath = System.IO.Path.Combine(folder, $"audit-backup-{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    if (audits.Any())
                    {
                        using var sw = new System.IO.StreamWriter(auditPath, false);
                        sw.WriteLine("Id,FechaHora,Usuario,Modulo,Accion,Entidad,EntidadClave,Detalle");
                        foreach (var a in audits)
                        {
                            sw.WriteLine($"{a.Id},\"{a.FechaHora:O}\",\"{a.Usuario}\",\"{a.Modulo}\",\"{a.Accion}\",\"{a.Entidad}\",\"{a.EntidadClave}\",\"{a.Detalle.Replace("\"","''")}\"");
                        }
                    }

                    var incs = await db.IncidenciasImportacion.OrderBy(x => x.FechaHora).ToListAsync();
                    var incPath = System.IO.Path.Combine(folder, $"incidencias-backup-{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    if (incs.Any())
                    {
                        using var sw2 = new System.IO.StreamWriter(incPath, false);
                        sw2.WriteLine("Id,FechaHora,TipoArchivo,PeriodoCodigo,FilaOrigen,CodigoReferencia,Descripcion,Resuelta,Resolucion");
                        foreach (var i in incs)
                        {
                            sw2.WriteLine($"{i.Id},\"{i.FechaHora:O}\",\"{i.TipoArchivo}\",\"{i.PeriodoCodigo}\",{i.FilaOrigen},\"{i.CodigoReferencia}\",\"{i.Descripcion.Replace("\"","''")}\",{(i.Resuelta ? 1 : 0)},\"{i.Resolucion.Replace("\"","''")}\"");
                        }
                    }
                }
                catch (Exception exExport)
                {
                    // If export fails, continue but inform the user
                    MessageBox.Show($"No se pudo crear backup CSV: {exExport.Message}", "Advertencia", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            // Use ExecuteSqlRaw to perform fast unconditional deletes and avoid tracking issues
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM AuditoriaEventos");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM IncidenciasImportacion");
                await tx.CommitAsync();
            }
            catch (Exception exSql)
            {
                await tx.RollbackAsync();
                throw; // will be caught by outer catch and shown to user
            }

            if (vm != null)
                await vm.CargarTablasAsyncPublic();

            MessageBox.Show("Auditoría e Incidencias vaciadas.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error borrando registros", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async void EliminarAuditoria_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var dg = this.FindName("dgAuditoria") as DataGrid;
        if (dg == null)
            dg = FindVisualChildren<DataGrid>(this).FirstOrDefault(d => d.ItemsSource == vm.Auditoria);

        if (dg == null)
        {
            MessageBox.Show("No se encontró el grid de auditoría.", "Eliminar", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var selected = dg.SelectedItems.Cast<SimpleRowViewModel>().ToList();
        if (!selected.Any())
        {
            MessageBox.Show("No hay elementos seleccionados.", "Eliminar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"Eliminar {selected.Count} registros de auditoría? Esta acción es irreversible.", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var sp = ((App)System.Windows.Application.Current).Services;
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BarracaDbContext>();

            foreach (var item in selected)
            {
                if (int.TryParse(item.Id?.ToString() ?? string.Empty, out var id))
                {
                    var ev = await db.AuditoriaEventos.FindAsync(id);
                    if (ev != null) db.AuditoriaEventos.Remove(ev);
                }
            }

            await db.SaveChangesAsync();
            await vm.CargarTablasAsyncPublic();
            MessageBox.Show("Eliminación de auditoría completada.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error eliminando auditoría", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async void EliminarIncidencias_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var dg = this.FindName("dgIncidencias") as DataGrid;
        if (dg == null)
            dg = FindVisualChildren<DataGrid>(this).FirstOrDefault(d => d.ItemsSource == vm.Incidencias);

        if (dg == null)
        {
            MessageBox.Show("No se encontró el grid de incidencias.", "Eliminar", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var selected = dg.SelectedItems.Cast<SimpleRowViewModel>().ToList();
        if (!selected.Any())
        {
            MessageBox.Show("No hay elementos seleccionados.", "Eliminar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"Eliminar {selected.Count} incidencias? Esta acción es irreversible.", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var sp = ((App)System.Windows.Application.Current).Services;
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BarracaDbContext>();

            foreach (var item in selected)
            {
                if (int.TryParse(item.Id?.ToString() ?? string.Empty, out var id))
                {
                    var ev = await db.IncidenciasImportacion.FindAsync(id);
                    if (ev != null) db.IncidenciasImportacion.Remove(ev);
                }
            }

            await db.SaveChangesAsync();
            await vm.CargarTablasAsyncPublic();
            MessageBox.Show("Eliminación de incidencias completada.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error eliminando incidencias", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async void EliminarDistribucion_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var dg = this.FindName("DistribucionDataGrid") as DataGrid;
        if (dg == null)
        {
            // fallback: find first DataGrid bound to DistribucionLineas
            dg = FindVisualChildren<DataGrid>(this).FirstOrDefault(d => d.ItemsSource == vm.DistribucionLineas);
        }

        if (dg == null)
        {
            MessageBox.Show("No se encontró el grid de distribución.", "Eliminar", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var selected = dg.SelectedItems.Cast<DistribucionLineaDto>().ToList();
        if (!selected.Any())
        {
            MessageBox.Show("No hay elementos seleccionados.", "Eliminar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"Eliminar {selected.Count} líneas de distribución? Esta acción eliminará los registros en la base de datos.", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        var ids = selected.Select(s => s.Id).ToArray();

        try
        {
            // get service from App services
            var crud = (ICrudService)((App)System.Windows.Application.Current).Services.GetService(typeof(ICrudService));
            if (crud == null)
            {
                MessageBox.Show("Servicio de CRUD no disponible.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var deleted = await crud.EliminarDistribucionLineasAsync(ids, "admin");
            MessageBox.Show($"Eliminadas {deleted} líneas.", "Eliminar", MessageBoxButton.OK, MessageBoxImage.Information);
            await vm.CargarTablasAsyncPublic();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error eliminando", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // helper to find DataGrid controls
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
            if (child is T t)
                yield return t;

            foreach (T childOfChild in FindVisualChildren<T>(child))
                yield return childOfChild;
        }
    }

    public async void EliminarReportes_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var selected = dgReportes.SelectedItems.Cast<SimpleRowViewModel>().ToList();
        if (!selected.Any())
        {
            MessageBox.Show("No hay elementos seleccionados.", "Eliminar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"Eliminar {selected.Count} archivos seleccionados? Esto borrará los PDFs del disco y no podrá deshacerse.", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        int deleted = 0;
        var errors = new List<string>();

        foreach (var item in selected)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(item.Descripcion) && File.Exists(item.Descripcion))
                {
                    File.Delete(item.Descripcion);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{item.Codigo}: {ex.Message}");
            }
        }

        // Refresh VM list
        // call internal reload method
        await vm.CargarTablasAsyncPublic();

        var msg = $"Eliminados: {deleted}.";
        if (errors.Any())
            msg += "\nErrores:\n" + string.Join("\n", errors);

        MessageBox.Show(msg, "Resultado eliminación", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public async void ResetearTodosDatos_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "⚠️ ATENCIÓN: Esto eliminará TODOS los datos de la aplicación:\n\n" +
            "• Funcionarios y cuentas de pago\n" +
            "• Obras\n" +
            "• Horas y pagos mensuales\n" +
            "• Distribuciones de costo\n" +
            "• Corridas de proceso\n" +
            "• Períodos\n" +
            "• Auditoría e incidencias\n\n" +
            "Esta acción es IRREVERSIBLE. ¿Desea continuar?",
            "Confirmar borrado total",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        // Double confirmation for safety
        var confirm2 = MessageBox.Show(
            "¿Está SEGURO? Se perderán TODOS los registros permanentemente.",
            "Segunda confirmación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Stop);

        if (confirm2 != MessageBoxResult.Yes)
            return;

        try
        {
            var sp = ((App)System.Windows.Application.Current).Services;
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BarracaDbContext>();

            // Delete in FK-safe order: children first, then parents
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM DistribucionesCosto");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM HorasMensuales");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM PagosMensuales");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM CuentasPagoFuncionario");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM CorridasProceso");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM Funcionarios");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM Obras");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM AuditoriaEventos");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM IncidenciasImportacion");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM Periodos");
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            if (DataContext is MainViewModel vm)
                await vm.CargarTablasAsyncPublic();

            MessageBox.Show(
                "Todos los datos han sido eliminados.\nPuede comenzar a cargar información nueva.",
                "Datos eliminados",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error borrando datos", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
