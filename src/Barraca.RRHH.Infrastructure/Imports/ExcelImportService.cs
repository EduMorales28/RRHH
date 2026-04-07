using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Data;
using Barraca.RRHH.Infrastructure.Helpers;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Imports;

public class ExcelImportService : IExcelImportService
{
    private readonly BarracaDbContext _db;

    public ExcelImportService(BarracaDbContext db)
    {
        _db = db;
    }

    public async Task<string> ImportarFuncionariosAsync(string filePath, string usuario = "sistema")
    {
        using var excelStream = AbrirArchivoExcelParaLectura(filePath);
        using var workbook = new XLWorkbook(excelStream);
        var ws = workbook.Worksheet("FUNCIONARIOS");
        var procesados = 0;

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var numero = row.Cell(1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(numero))
                continue;

            var funcionario = await _db.Funcionarios
                .Include(x => x.CuentasPago)
                .FirstOrDefaultAsync(x => x.NumeroFuncionario == numero);

            if (funcionario is null)
            {
                funcionario = new Funcionario
                {
                    NumeroFuncionario = numero
                };
                _db.Funcionarios.Add(funcionario);
            }

            funcionario.Nombre = row.Cell(2).GetString().Trim();
            funcionario.Categoria = row.Cell(3).GetString().Trim();
            funcionario.TieneRetencion = !string.IsNullOrWhiteSpace(row.Cell(8).GetString().Trim());
            funcionario.ResponsableRetencion = row.Cell(8).GetString().Trim();
            funcionario.BancoRetencion = row.Cell(9).GetString().Trim();
            funcionario.CuentaRetencion = row.Cell(10).GetString().Trim();
            funcionario.Activo = row.Cell(11).GetString().Trim().Equals("SI", StringComparison.OrdinalIgnoreCase);

            var banco = row.Cell(5).GetString().Trim();
            var cuentaNueva = row.Cell(6).GetString().Trim();
            var cuentaVieja = row.Cell(7).GetString().Trim();
            var tipoPago = row.Cell(4).GetString().Trim();

            // Layout alternativo: si no hay columnas bancarias, E se toma como observación general.
            if (string.IsNullOrWhiteSpace(cuentaNueva) && string.IsNullOrWhiteSpace(cuentaVieja) && !string.IsNullOrWhiteSpace(banco))
            {
                _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                {
                    TipoArchivo = "FUNCIONARIOS",
                    CodigoReferencia = numero,
                    Descripcion = $"Observación general: {funcionario.Nombre} - {banco}"
                });
                banco = string.Empty;
            }

            var cuentaExistente = funcionario.CuentasPago
                .FirstOrDefault(x => x.Banco == banco && x.CuentaNueva == cuentaNueva && x.CuentaVieja == cuentaVieja);

            if (cuentaExistente is null && (!string.IsNullOrWhiteSpace(banco) || !string.IsNullOrWhiteSpace(cuentaNueva) || !string.IsNullOrWhiteSpace(cuentaVieja)))
            {
                funcionario.CuentasPago.Add(new CuentaPagoFuncionario
                {
                    TipoPago = tipoPago,
                    Banco = banco,
                    CuentaNueva = cuentaNueva,
                    CuentaVieja = cuentaVieja,
                    Activa = true
                });
            }

            procesados++;
        }

        _db.AuditoriaEventos.Add(new AuditoriaEvento
        {
            Usuario = usuario,
            Modulo = "Importaciones",
            Accion = "Importar Funcionarios",
            Entidad = "FUNCIONARIOS",
            EntidadClave = Path.GetFileName(filePath),
            Detalle = $"Registros procesados: {procesados}"
        });

        await _db.SaveChangesAsync();
        return $"Funcionarios importados: {procesados}";
    }

    public async Task<string> ImportarObrasAsync(string filePath, string usuario = "sistema")
    {
        using var excelStream = AbrirArchivoExcelParaLectura(filePath);
        using var workbook = new XLWorkbook(excelStream);
        var ws = workbook.Worksheet("OBRAS");
        var procesados = 0;

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var numero = row.Cell(1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(numero))
                continue;

            var obra = await _db.Obras.FirstOrDefaultAsync(x => x.NumeroObra == numero);
            if (obra is null)
            {
                obra = new Obra
                {
                    NumeroObra = numero
                };
                _db.Obras.Add(obra);
            }

            var tipoOriginal = row.Cell(3).GetString().Trim();
            obra.Nombre = row.Cell(2).GetString().Trim();
            obra.TipoObraOriginal = tipoOriginal;
            obra.TipoObra = TipoObraParser.Parse(tipoOriginal);
            obra.Cliente = row.Cell(4).GetString().Trim();
            obra.Activa = row.Cell(5).GetString().Trim().Equals("SI", StringComparison.OrdinalIgnoreCase);

            procesados++;
        }

        _db.AuditoriaEventos.Add(new AuditoriaEvento
        {
            Usuario = usuario,
            Modulo = "Importaciones",
            Accion = "Importar Obras",
            Entidad = "OBRAS",
            EntidadClave = Path.GetFileName(filePath),
            Detalle = $"Registros procesados: {procesados}"
        });

        await _db.SaveChangesAsync();
        return $"Obras importadas: {procesados}";
    }

    public async Task<string> ImportarHorasAsync(string filePath, string periodo, string usuario = "sistema")
    {
        if (string.IsNullOrWhiteSpace(periodo))
            throw new ArgumentException("El periodo es requerido.", nameof(periodo));

        using var excelStream = AbrirArchivoExcelParaLectura(filePath);
        using var workbook = new XLWorkbook(excelStream);
        var ws = workbook.Worksheet("HORAS");
        var periodoCodigo = periodo.Trim();
        var periodoEntity = await ObtenerOCrearPeriodo(periodoCodigo);
        var procesados = 0;
        var creadosAutomaticos = 0;
        var incidencias = 0;

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var existentes = _db.HorasMensuales.Where(x => x.PeriodoId == periodoEntity.Id);
            _db.HorasMensuales.RemoveRange(existentes);

            foreach (var row in FilasDatosHoras(ws))
            {
                row.Cell(1).TryGetValue<int>(out var idRegistro);

                var numeroFuncionario = row.Cell(2).GetString().Trim();
                if (string.IsNullOrWhiteSpace(numeroFuncionario))
                    continue;

                var nombreFuncionario = row.Cell(3).GetString().Trim();
                var numeroObra = row.Cell(5).GetString().Trim();
                if (string.IsNullOrWhiteSpace(numeroObra))
                {
                    _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                    {
                        TipoArchivo = "HORAS",
                        PeriodoCodigo = periodoCodigo,
                        FilaOrigen = row.RowNumber(),
                        CodigoReferencia = numeroFuncionario,
                        Descripcion = "Fila ignorada por numero de obra vacio."
                    });
                    incidencias++;
                    continue;
                }

                var nombreObra = row.Cell(6).GetString().Trim();
                var tipoOriginal = row.Cell(7).GetString().Trim();
                var tipoObra = TipoObraParser.Parse(tipoOriginal);

                var periodoFila = row.Cell(4).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(periodoFila) && !string.Equals(periodoFila, periodoCodigo, StringComparison.OrdinalIgnoreCase))
                {
                    _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                    {
                        TipoArchivo = "HORAS",
                        PeriodoCodigo = periodoCodigo,
                        FilaOrigen = row.RowNumber(),
                        CodigoReferencia = numeroFuncionario,
                        Descripcion = $"Período en fila ({periodoFila}) distinto al período activo ({periodoCodigo})."
                    });
                    incidencias++;
                    continue;
                }

                var resultadoFuncionario = await ObtenerOCrearFuncionarioAsync(numeroFuncionario, nombreFuncionario);
                var funcionario = resultadoFuncionario.entidad;
                if (resultadoFuncionario.creado)
                {
                    _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                    {
                        TipoArchivo = "HORAS",
                        PeriodoCodigo = periodoCodigo,
                        FilaOrigen = row.RowNumber(),
                        CodigoReferencia = numeroFuncionario,
                        Descripcion = $"Funcionario creado automaticamente desde horas: {nombreFuncionario}"
                    });
                    creadosAutomaticos++;
                    incidencias++;
                }

                // Soporta dos layouts de HORAS:
                // v1: H comunes, I extras, J totales, K cliente
                // v2: H cliente, I comunes, J extras, K totales
                var celda8 = row.Cell(8);
                var layoutConClienteEn8 = !celda8.IsEmpty() && !celda8.TryGetValue<decimal>(out _);

                var cliente = layoutConClienteEn8
                    ? row.Cell(8).GetString().Trim()
                    : row.Cell(11).GetString().Trim();

                var horasComunes = layoutConClienteEn8 ? LeerDecimal(row.Cell(9)) : LeerDecimal(row.Cell(8));
                var horasExtras = layoutConClienteEn8 ? LeerDecimal(row.Cell(10)) : LeerDecimal(row.Cell(9));
                var horasEq = horasComunes + (horasExtras * 2m);

                if (tipoObra is TipoObra.Construccion or TipoObra.IndustriaYComercio or TipoObra.NA)
                    cliente = "Almirtaun";

                var resultadoObra = await ObtenerOCrearObraAsync(numeroObra, nombreObra, tipoOriginal, cliente);
                var obra = resultadoObra.entidad;
                if (resultadoObra.creado)
                {
                    _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                    {
                        TipoArchivo = "HORAS",
                        PeriodoCodigo = periodoCodigo,
                        FilaOrigen = row.RowNumber(),
                        CodigoReferencia = numeroObra,
                        Descripcion = $"Obra creada automaticamente desde horas: {nombreObra}"
                    });
                    creadosAutomaticos++;
                    incidencias++;
                }

                _db.HorasMensuales.Add(new HoraMensual
                {
                    PeriodoId = periodoEntity.Id,
                    FuncionarioId = funcionario.Id,
                    ObraId = obra.Id,
                    RegistroOrigen = idRegistro,
                    NombreFuncionarioExcel = nombreFuncionario,
                    NombreObraExcel = nombreObra,
                    Categoria = string.IsNullOrWhiteSpace(funcionario.Categoria) ? "SIN CATEGORIA" : funcionario.Categoria,
                    Cliente = cliente,
                    HorasComunes = horasComunes,
                    HorasExtras = horasExtras,
                    HorasEquivalentes = horasEq
                });

                procesados++;
            }

            _db.AuditoriaEventos.Add(new AuditoriaEvento
            {
                Usuario = usuario,
                Modulo = "Importaciones",
                Accion = "Importar Horas",
                Entidad = "HORAS",
                EntidadClave = periodoCodigo,
                Detalle = $"Registros procesados: {procesados}. Altas automaticas: {creadosAutomaticos}. Incidencias: {incidencias}"
            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return $"Horas importadas: {procesados}. Altas automaticas: {creadosAutomaticos}. Incidencias: {incidencias}";
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<string> ImportarPagosAsync(string filePath, string periodo, string usuario = "sistema")
    {
        if (string.IsNullOrWhiteSpace(periodo))
            throw new ArgumentException("El periodo es requerido.", nameof(periodo));

        using var excelStream = AbrirArchivoExcelParaLectura(filePath);
        using var workbook = new XLWorkbook(excelStream);
        var ws = workbook.Worksheet("PAGOS");
        var periodoCodigo = periodo.Trim();
        var periodoEntity = await ObtenerOCrearPeriodo(periodoCodigo);
        var procesados = 0;
        var creadosAutomaticos = 0;
        var incidencias = 0;

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var existentes = _db.PagosMensuales.Where(x => x.PeriodoId == periodoEntity.Id);
            _db.PagosMensuales.RemoveRange(existentes);

            foreach (var row in FilasDatosPagos(ws))
            {
                // Layout real PAGOS:
                // A ID, B Num Func, C Nombre, D Periodo, E Tipo Obra, F Cliente,
                // G Adelanto, H Liquido, I Retencion, J TipoPago, K Total, L Obs
                var numeroFuncionario = row.Cell(2).GetString().Trim();
                if (string.IsNullOrWhiteSpace(numeroFuncionario))
                    continue;

                var nombreFuncionario = row.Cell(3).GetString().Trim();
                var periodoFila = row.Cell(4).GetString().Trim();
                var tipoOriginal = row.Cell(5).GetString().Trim();
                var tipoObra = TipoObraParser.Parse(tipoOriginal);

                if (!string.IsNullOrWhiteSpace(periodoFila) && !string.Equals(periodoFila, periodoCodigo, StringComparison.OrdinalIgnoreCase))
                {
                    _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                    {
                        TipoArchivo = "PAGOS",
                        PeriodoCodigo = periodoCodigo,
                        FilaOrigen = row.RowNumber(),
                        CodigoReferencia = numeroFuncionario,
                        Descripcion = $"Período en fila ({periodoFila}) distinto al período activo ({periodoCodigo})."
                    });
                    incidencias++;
                    continue;
                }

                var resultadoFuncionario = await ResolverFuncionarioDesdePagoAsync(numeroFuncionario, nombreFuncionario);
                var funcionario = resultadoFuncionario.entidad;
                if (resultadoFuncionario.creado)
                {
                    _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                    {
                        TipoArchivo = "PAGOS",
                        PeriodoCodigo = periodoCodigo,
                        FilaOrigen = row.RowNumber(),
                        CodigoReferencia = numeroFuncionario,
                        Descripcion = $"Funcionario creado automaticamente desde pagos: {nombreFuncionario}"
                    });
                    creadosAutomaticos++;
                    incidencias++;
                }

                var adelanto = LeerDecimal(row.Cell(7));
                var liquido = LeerDecimal(row.Cell(8));
                var retencion = LeerDecimal(row.Cell(9));
                var totalColumna = LeerDecimal(row.Cell(11));

                if (adelanto < 0 || liquido < 0 || retencion < 0)
                {
                    _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                    {
                        TipoArchivo = "PAGOS",
                        PeriodoCodigo = periodoCodigo,
                        FilaOrigen = row.RowNumber(),
                        CodigoReferencia = numeroFuncionario,
                        Descripcion = "Fila ignorada por valores negativos en montos de pago."
                    });
                    incidencias++;
                    continue;
                }

                var totalCalculado = adelanto + liquido + retencion;
                var totalGenerado = totalCalculado;
                if (totalColumna > 0m && totalColumna != totalCalculado)
                {
                    _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                    {
                        TipoArchivo = "PAGOS",
                        PeriodoCodigo = periodoCodigo,
                        FilaOrigen = row.RowNumber(),
                        CodigoReferencia = numeroFuncionario,
                        Descripcion = $"Total columna K ({totalColumna:N2}) difiere de adelanto+liquido+retencion ({totalCalculado:N2}). Se usa adelanto+liquido+retencion como total válido."
                    });
                    incidencias++;
                }

                var cliente = row.Cell(6).GetString().Trim();
                if (tipoObra is TipoObra.Construccion or TipoObra.IndustriaYComercio or TipoObra.NA)
                    cliente = "Almirtaun";

                var tipoPagoRaw = row.Cell(10).GetString().Trim();
                var tipoPago = tipoObra == TipoObra.NA ? "Efectivo" : "RedPagos";
                if (!string.IsNullOrWhiteSpace(tipoPagoRaw))
                {
                    var normalized = tipoPagoRaw.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
                    if (tipoObra == TipoObra.NA && normalized != "EFECTIVO")
                    {
                        _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                        {
                            TipoArchivo = "PAGOS",
                            PeriodoCodigo = periodoCodigo,
                            FilaOrigen = row.RowNumber(),
                            CodigoReferencia = numeroFuncionario,
                            Descripcion = $"Tipo de pago '{tipoPagoRaw}' ajustado a 'Efectivo' para tipo de obra N-A."
                        });
                        incidencias++;
                    }
                    else if (tipoObra != TipoObra.NA && normalized == "EFECTIVO")
                    {
                        _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                        {
                            TipoArchivo = "PAGOS",
                            PeriodoCodigo = periodoCodigo,
                            FilaOrigen = row.RowNumber(),
                            CodigoReferencia = numeroFuncionario,
                            Descripcion = $"Tipo de pago '{tipoPagoRaw}' ajustado a 'RedPagos' (solo N-A puede ser efectivo)."
                        });
                        incidencias++;
                    }
                }

                _db.PagosMensuales.Add(new PagoMensual
                {
                    PeriodoId = periodoEntity.Id,
                    FuncionarioId = funcionario.Id,
                    NombreFuncionarioExcel = nombreFuncionario,
                    TipoObra = tipoObra,
                    TipoObraOriginal = tipoOriginal,
                    Cliente = cliente,
                    Adelanto = adelanto,
                    Liquido = liquido,
                    Retencion = retencion,
                    TotalGenerado = totalGenerado,
                    TipoPago = tipoPago,
                    Observacion = row.Cell(12).GetString().Trim()
                });

                procesados++;
            }

            _db.AuditoriaEventos.Add(new AuditoriaEvento
            {
                Usuario = usuario,
                Modulo = "Importaciones",
                Accion = "Importar Pagos",
                Entidad = "PAGOS",
                EntidadClave = periodoCodigo,
                Detalle = $"Registros procesados: {procesados}. Altas automaticas: {creadosAutomaticos}. Incidencias: {incidencias}"
            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return $"Pagos importados: {procesados}. Altas automaticas: {creadosAutomaticos}. Incidencias: {incidencias}";
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<Periodo> ObtenerOCrearPeriodo(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ArgumentException("El codigo de periodo es requerido.", nameof(codigo));

        var codigoNormalizado = codigo.Trim();
        var periodo = await _db.Periodos.FirstOrDefaultAsync(x => x.Codigo == codigo);
        if (periodo is not null)
            return periodo;

        periodo = new Periodo { Codigo = codigoNormalizado };
        _db.Periodos.Add(periodo);

        try
        {
            await _db.SaveChangesAsync();
            return periodo;
        }
        catch (DbUpdateException)
        {
            _db.Entry(periodo).State = EntityState.Detached;
            var existente = await _db.Periodos.FirstOrDefaultAsync(x => x.Codigo == codigoNormalizado);
            if (existente is not null)
                return existente;

            throw;
        }
    }

    private async Task<(Funcionario entidad, bool creado)> ObtenerOCrearFuncionarioAsync(string numeroFuncionario, string nombreFuncionario)
    {
        var numero = numeroFuncionario.Trim();
        var existente = await _db.Funcionarios.FirstOrDefaultAsync(x => x.NumeroFuncionario == numero);
        if (existente is not null)
            return (existente, false);

        var nuevo = new Funcionario
        {
            NumeroFuncionario = numero,
            Nombre = string.IsNullOrWhiteSpace(nombreFuncionario) ? "SIN NOMBRE" : nombreFuncionario.Trim(),
            Categoria = "SIN CATEGORIA",
            Activo = true
        };

        _db.Funcionarios.Add(nuevo);
        try
        {
            await _db.SaveChangesAsync();
            return (nuevo, true);
        }
        catch (DbUpdateException)
        {
            _db.Entry(nuevo).State = EntityState.Detached;
            var recuperado = await _db.Funcionarios.FirstAsync(x => x.NumeroFuncionario == numero);
            return (recuperado, false);
        }
    }

    private async Task<(Funcionario entidad, bool creado)> ResolverFuncionarioDesdePagoAsync(string numeroFuncionario, string nombreFuncionario)
    {
        var numero = (numeroFuncionario ?? string.Empty).Trim();

        var porNumero = await _db.Funcionarios.FirstOrDefaultAsync(x => x.NumeroFuncionario == numero);
        if (porNumero is not null)
            return (porNumero, false);

        var creado = await ObtenerOCrearFuncionarioAsync(numero, nombreFuncionario);
        return (creado.entidad, creado.creado);
    }

    private async Task<(Obra entidad, bool creado)> ObtenerOCrearObraAsync(string numeroObra, string nombreObra, string tipoOriginal, string cliente)
    {
        var numero = numeroObra.Trim();
        var existente = await _db.Obras.FirstOrDefaultAsync(x => x.NumeroObra == numero);
        if (existente is not null)
        {
            var tipoLimpio = (tipoOriginal ?? string.Empty).Trim();
            var nombreLimpio = string.IsNullOrWhiteSpace(nombreObra) ? existente.Nombre : nombreObra.Trim();
            var clienteLimpio = string.IsNullOrWhiteSpace(cliente) ? existente.Cliente : cliente.Trim();

            var huboCambios = false;

            if (!string.Equals(existente.Nombre, nombreLimpio, StringComparison.Ordinal))
            {
                existente.Nombre = nombreLimpio;
                huboCambios = true;
            }

            if (!string.Equals(existente.TipoObraOriginal, tipoLimpio, StringComparison.Ordinal))
            {
                existente.TipoObraOriginal = tipoLimpio;
                existente.TipoObra = TipoObraParser.Parse(tipoLimpio);
                huboCambios = true;
            }

            if (!string.Equals(existente.Cliente, clienteLimpio, StringComparison.Ordinal))
            {
                existente.Cliente = clienteLimpio;
                huboCambios = true;
            }

            if (!existente.Activa)
            {
                existente.Activa = true;
                huboCambios = true;
            }

            if (huboCambios)
                await _db.SaveChangesAsync();

            return (existente, false);
        }

        var nuevo = new Obra
        {
            NumeroObra = numero,
            Nombre = string.IsNullOrWhiteSpace(nombreObra) ? "SIN NOMBRE" : nombreObra.Trim(),
            TipoObraOriginal = tipoOriginal.Trim(),
            TipoObra = TipoObraParser.Parse(tipoOriginal),
            Cliente = cliente.Trim(),
            Activa = true
        };

        _db.Obras.Add(nuevo);
        try
        {
            await _db.SaveChangesAsync();
            return (nuevo, true);
        }
        catch (DbUpdateException)
        {
            _db.Entry(nuevo).State = EntityState.Detached;
            var recuperada = await _db.Obras.FirstAsync(x => x.NumeroObra == numero);
            return (recuperada, false);
        }
    }

    private static decimal LeerDecimal(IXLCell cell)
    {
        if (cell.IsEmpty())
            return 0m;

        if (cell.TryGetValue<decimal>(out var value))
            return value;

        return decimal.TryParse(cell.GetString().Trim(), out value) ? value : 0m;
    }

    private static IEnumerable<IXLRow> FilasDatosHoras(IXLWorksheet ws)
    {
        foreach (var row in ws.RowsUsed())
        {
            // Fila de datos válida: ID numérico + número de funcionario informado.
            if (!row.Cell(1).TryGetValue<int>(out _))
                continue;

            var numeroFuncionario = row.Cell(2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(numeroFuncionario))
                continue;

            yield return row;
        }
    }

    private static IEnumerable<IXLRow> FilasDatosPagos(IXLWorksheet ws)
    {
        foreach (var row in ws.RowsUsed())
        {
            // Fila de datos válida: ID numérico + número de funcionario informado.
            if (!row.Cell(1).TryGetValue<int>(out _))
                continue;

            var numeroFuncionario = row.Cell(2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(numeroFuncionario))
                continue;

            yield return row;
        }
    }

    private static FileStream AbrirArchivoExcelParaLectura(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("La ruta del archivo es requerida.", nameof(filePath));

        try
        {
            return new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
        }
        catch (IOException ex)
        {
            throw new IOException(
                $"No se pudo leer el archivo '{filePath}'. Cierre el archivo en Excel u otros programas e intente nuevamente.",
                ex);
        }
    }
}
