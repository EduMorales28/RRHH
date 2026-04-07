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

            foreach (var row in ws.RowsUsed().Skip(3))
            {
                if (!row.Cell(1).TryGetValue<int>(out var idRegistro))
                {
                    _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                    {
                        TipoArchivo = "HORAS",
                        PeriodoCodigo = periodoCodigo,
                        FilaOrigen = row.RowNumber(),
                        CodigoReferencia = string.Empty,
                        Descripcion = "Fila ignorada por id de registro invalido."
                    });
                    incidencias++;
                    continue;
                }

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

                var cliente = row.Cell(11).GetString().Trim();
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

                var horasComunes = LeerDecimal(row.Cell(8));
                var horasExtras = LeerDecimal(row.Cell(9));
                var horasEq = horasComunes + (horasExtras * 2m);

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

            foreach (var row in ws.RowsUsed().Skip(3))
            {
                var numeroFuncionario = row.Cell(1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(numeroFuncionario))
                    continue;

                var nombreFuncionario = row.Cell(2).GetString().Trim();
                var tipoOriginal = row.Cell(4).GetString().Trim();

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
                else if (resultadoFuncionario.vinculadoPorNombre)
                {
                    _db.IncidenciasImportacion.Add(new IncidenciaImportacion
                    {
                        TipoArchivo = "PAGOS",
                        PeriodoCodigo = periodoCodigo,
                        FilaOrigen = row.RowNumber(),
                        CodigoReferencia = numeroFuncionario,
                        Descripcion = $"Numero de funcionario '{numeroFuncionario}' no encontrado. Pago asociado por nombre a '{funcionario.NumeroFuncionario} - {funcionario.Nombre}'."
                    });
                    incidencias++;
                }

                var adelanto = LeerDecimal(row.Cell(6));
                var liquido = LeerDecimal(row.Cell(7));
                var retencion = LeerDecimal(row.Cell(8));

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

                _db.PagosMensuales.Add(new PagoMensual
                {
                    PeriodoId = periodoEntity.Id,
                    FuncionarioId = funcionario.Id,
                    NombreFuncionarioExcel = nombreFuncionario,
                    TipoObra = TipoObraParser.Parse(tipoOriginal),
                    TipoObraOriginal = tipoOriginal,
                    Cliente = row.Cell(5).GetString().Trim(),
                    Adelanto = adelanto,
                    Liquido = liquido,
                    Retencion = retencion,
                    TotalGenerado = adelanto + liquido + retencion,
                    TipoPago = row.Cell(9).GetString().Trim(),
                    Observacion = row.Cell(10).GetString().Trim()
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

    private async Task<(Funcionario entidad, bool creado, bool vinculadoPorNombre)> ResolverFuncionarioDesdePagoAsync(string numeroFuncionario, string nombreFuncionario)
    {
        var numero = (numeroFuncionario ?? string.Empty).Trim();
        var nombre = (nombreFuncionario ?? string.Empty).Trim();

        var porNumero = await _db.Funcionarios.FirstOrDefaultAsync(x => x.NumeroFuncionario == numero);
        if (porNumero is not null)
            return (porNumero, false, false);

        if (!string.IsNullOrWhiteSpace(nombre))
        {
            var candidatos = await _db.Funcionarios
                .Where(x => x.Nombre == nombre || x.Nombre.ToUpper() == nombre.ToUpper())
                .OrderByDescending(x => x.Activo)
                .ThenBy(x => x.NumeroFuncionario)
                .Take(2)
                .ToListAsync();

            if (candidatos.Count == 1)
                return (candidatos[0], false, true);
        }

        var creado = await ObtenerOCrearFuncionarioAsync(numero, nombre);
        return (creado.entidad, creado.creado, false);
    }

    private async Task<(Obra entidad, bool creado)> ObtenerOCrearObraAsync(string numeroObra, string nombreObra, string tipoOriginal, string cliente)
    {
        var numero = numeroObra.Trim();
        var existente = await _db.Obras.FirstOrDefaultAsync(x => x.NumeroObra == numero);
        if (existente is not null)
            return (existente, false);

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
