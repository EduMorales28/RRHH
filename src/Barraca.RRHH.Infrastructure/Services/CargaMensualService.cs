using System.Text.RegularExpressions;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Data;
using Barraca.RRHH.Infrastructure.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Services;

public class CargaMensualService : ICargaMensualService
{
    private const string ClienteEmpresa = "Almirtaun";
    private readonly BarracaDbContext _db;

    public CargaMensualService(BarracaDbContext db)
    {
        _db = db;
    }

    // ---------------------------------------------------------------- Funcionarios

    public async Task<List<FuncionarioEdicionDto>> ObtenerFuncionariosAsync()
    {
        var funcionarios = await _db.Funcionarios.AsNoTracking()
            .Include(x => x.CuentasPago)
            .ToListAsync();

        return funcionarios
            .OrderBy(x => OrdenNumerico(x.NumeroFuncionario))
            .ThenBy(x => x.NumeroFuncionario)
            .Select(f =>
            {
                var cuenta = f.CuentasPago.Where(c => c.Activa).MaxBy(c => c.Id);
                return new FuncionarioEdicionDto
                {
                    Id = f.Id,
                    NumeroFuncionario = f.NumeroFuncionario,
                    Nombre = f.Nombre,
                    Categoria = f.Categoria,
                    TipoPago = cuenta?.TipoPago ?? string.Empty,
                    Banco = cuenta?.Banco ?? string.Empty,
                    CuentaNueva = cuenta?.CuentaNueva ?? string.Empty,
                    CuentaVieja = cuenta?.CuentaVieja ?? string.Empty,
                    ResponsableRetencion = f.ResponsableRetencion,
                    BancoRetencion = f.BancoRetencion,
                    CuentaRetencion = f.CuentaRetencion,
                    RetencionFija = f.RetencionFija,
                    HoraFijaSede = f.HoraFijaSede,
                    Activo = f.Activo
                };
            })
            .ToList();
    }

    public async Task<string> GuardarFuncionariosAsync(IReadOnlyList<FuncionarioEdicionDto> modificados, string usuario)
    {
        if (modificados.Count == 0)
            return "No hay cambios en funcionarios.";

        var errores = new List<string>();
        foreach (var f in modificados)
        {
            if (string.IsNullOrWhiteSpace(f.NumeroFuncionario))
                errores.Add($"Hay un funcionario sin número ({f.Nombre}).");
            if (string.IsNullOrWhiteSpace(f.Nombre))
                errores.Add($"El funcionario {f.NumeroFuncionario} no tiene nombre.");
        }

        var existentes = await _db.Funcionarios.Include(x => x.CuentasPago).ToListAsync();
        foreach (var f in modificados.Where(x => x.Id == 0 && !string.IsNullOrWhiteSpace(x.NumeroFuncionario)))
        {
            var numero = f.NumeroFuncionario.Trim();
            var usadoEnBase = existentes.Any(x => string.Equals(x.NumeroFuncionario.Trim(), numero, StringComparison.OrdinalIgnoreCase));
            var repetidoEnAltas = modificados.Count(x => x.Id == 0 && string.Equals(x.NumeroFuncionario.Trim(), numero, StringComparison.OrdinalIgnoreCase)) > 1;
            if (usadoEnBase || repetidoEnAltas)
                errores.Add($"El número {numero} ya está usado por otro funcionario.");
        }

        LanzarSiHayErrores("No se guardaron los funcionarios", errores.Distinct().ToList());

        var periodosAbiertos = await PeriodosNoCerradosAsync();

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            int altas = 0, cambios = 0;
            foreach (var dto in modificados)
            {
                Funcionario entidad;
                if (dto.Id == 0)
                {
                    entidad = new Funcionario { NumeroFuncionario = dto.NumeroFuncionario.Trim() };
                    _db.Funcionarios.Add(entidad);
                    altas++;
                }
                else
                {
                    entidad = existentes.FirstOrDefault(x => x.Id == dto.Id)
                        ?? throw new InvalidOperationException($"No se encontró el funcionario {dto.NumeroFuncionario}.");
                    cambios++;
                }

                var categoriaAnterior = entidad.Categoria;
                entidad.Nombre = dto.Nombre.Trim();
                entidad.Categoria = dto.Categoria.Trim();
                entidad.ResponsableRetencion = dto.ResponsableRetencion.Trim();
                entidad.BancoRetencion = dto.BancoRetencion.Trim();
                entidad.CuentaRetencion = dto.CuentaRetencion.Trim();
                entidad.RetencionFija = dto.RetencionFija;
                entidad.HoraFijaSede = dto.HoraFijaSede;
                entidad.TieneRetencion = entidad.ResponsableRetencion.Length > 0;
                entidad.Activo = dto.Activo;
                AsignarCuentaVigente(entidad, dto);

                // Las horas de períodos abiertos guardan la categoría: se actualiza para que la distribución use la nueva.
                if (entidad.Id != 0 && !string.Equals(categoriaAnterior, entidad.Categoria, StringComparison.Ordinal))
                {
                    var horas = await _db.HorasMensuales
                        .Where(h => h.FuncionarioId == entidad.Id && periodosAbiertos.Contains(h.PeriodoId))
                        .ToListAsync();
                    foreach (var h in horas)
                        h.Categoria = CategoriaParaHoras(entidad);
                }
            }

            Auditar(usuario, "Funcionarios", "Editar funcionarios", "Funcionario", "", $"Altas: {altas}. Modificados: {cambios}.");
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return $"Funcionarios guardados. Altas: {altas}. Modificados: {cambios}.";
        }
        catch
        {
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private static void AsignarCuentaVigente(Funcionario funcionario, FuncionarioEdicionDto dto)
    {
        var banco = dto.Banco.Trim();
        var nueva = dto.CuentaNueva.Trim();
        var vieja = dto.CuentaVieja.Trim();
        var tipoPago = dto.TipoPago.Trim();

        // Queda una sola cuenta activa: la indicada en la ficha.
        if (banco.Length == 0 && nueva.Length == 0 && vieja.Length == 0)
        {
            foreach (var c in funcionario.CuentasPago)
                c.Activa = false;
            return;
        }

        var cuenta = funcionario.CuentasPago.FirstOrDefault(c => c.Banco == banco && c.CuentaNueva == nueva && c.CuentaVieja == vieja);
        if (cuenta is null)
        {
            cuenta = new CuentaPagoFuncionario { Banco = banco, CuentaNueva = nueva, CuentaVieja = vieja };
            funcionario.CuentasPago.Add(cuenta);
        }

        cuenta.TipoPago = tipoPago;
        cuenta.Activa = true;
        foreach (var otra in funcionario.CuentasPago.Where(c => !ReferenceEquals(c, cuenta)))
            otra.Activa = false;
    }

    // ---------------------------------------------------------------- Obras

    public async Task<List<ObraEdicionDto>> ObtenerObrasAsync()
    {
        var obras = await _db.Obras.AsNoTracking().ToListAsync();
        return obras
            .OrderBy(x => OrdenNumerico(x.NumeroObra))
            .ThenBy(x => x.NumeroObra)
            .Select(o => new ObraEdicionDto
            {
                Id = o.Id,
                NumeroObra = o.NumeroObra,
                Nombre = o.Nombre,
                TipoObra = o.TipoObra,
                Cliente = o.Cliente,
                Activa = o.Activa
            })
            .ToList();
    }

    public async Task<string> GuardarObrasAsync(IReadOnlyList<ObraEdicionDto> modificadas, string usuario)
    {
        if (modificadas.Count == 0)
            return "No hay cambios en obras.";

        var errores = new List<string>();
        foreach (var o in modificadas)
        {
            if (string.IsNullOrWhiteSpace(o.NumeroObra))
                errores.Add($"Hay una obra sin número ({o.Nombre}).");
            if (string.IsNullOrWhiteSpace(o.Nombre))
                errores.Add($"La obra {o.NumeroObra} no tiene nombre.");
        }

        var existentes = await _db.Obras.ToListAsync();
        foreach (var o in modificadas.Where(x => x.Id == 0 && !string.IsNullOrWhiteSpace(x.NumeroObra)))
        {
            var numero = o.NumeroObra.Trim();
            var otraEnBase = existentes.Any(x => string.Equals(x.NumeroObra.Trim(), numero, StringComparison.OrdinalIgnoreCase));
            var otraEnCambios = modificadas.Count(x => x.Id == 0 && string.Equals(x.NumeroObra.Trim(), numero, StringComparison.OrdinalIgnoreCase)) > 1;
            if (otraEnBase || otraEnCambios)
                errores.Add($"El número de obra {numero} ya está usado por otra obra.");
        }

        LanzarSiHayErrores("No se guardaron las obras", errores.Distinct().ToList());

        int altas = 0, cambios = 0;
        foreach (var dto in modificadas)
        {
            Obra entidad;
            if (dto.Id == 0)
            {
                entidad = new Obra { NumeroObra = dto.NumeroObra.Trim() };
                _db.Obras.Add(entidad);
                altas++;
            }
            else
            {
                entidad = existentes.FirstOrDefault(x => x.Id == dto.Id)
                    ?? throw new InvalidOperationException($"No se encontró la obra {dto.NumeroObra}.");
                cambios++;
            }

            entidad.Nombre = dto.Nombre.Trim();
            entidad.TipoObra = dto.TipoObra;
            entidad.TipoObraOriginal = TipoObraParser.Texto(dto.TipoObra);
            entidad.Cliente = dto.Cliente.Trim();
            entidad.Activa = dto.Activa;
        }

        Auditar(usuario, "Obras", "Editar obras", "Obra", "", $"Altas: {altas}. Modificadas: {cambios}.");
        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }

        return $"Obras guardadas. Altas: {altas}. Modificadas: {cambios}.";
    }

    // ---------------------------------------------------------------- Pagos

    public async Task<List<PagoEdicionDto>> ObtenerPagosAsync(string periodo)
    {
        var codigo = (periodo ?? string.Empty).Trim();
        return await _db.PagosMensuales.AsNoTracking()
            .Where(x => x.Periodo!.Codigo == codigo)
            .OrderBy(x => x.Id)
            .Select(x => new PagoEdicionDto
            {
                Id = x.Id,
                NumeroFuncionario = x.Funcionario!.NumeroFuncionario,
                TipoObra = x.TipoObra,
                Cliente = x.Cliente,
                Adelanto = x.Adelanto,
                Liquido = x.Liquido,
                Retencion = x.Retencion,
                Observacion = x.Observacion,
                Nominal = x.Nominal
            })
            .ToListAsync();
    }

    public async Task<string> GuardarPagosAsync(string periodo, IReadOnlyList<PagoEdicionDto> filas, string usuario)
    {
        var codigo = (periodo ?? string.Empty).Trim();
        await PeriodoGuard.VerificarAbiertoAsync(_db, codigo);
        var periodoEntity = await ObtenerPeriodoExistenteAsync(codigo);

        var funcionarios = await MapaFuncionariosAsync();
        var errores = new List<string>();
        for (var i = 0; i < filas.Count; i++)
        {
            var f = filas[i];
            if (!funcionarios.ContainsKey(f.NumeroFuncionario.Trim()))
                errores.Add($"Fila {i + 1}: el funcionario '{f.NumeroFuncionario}' no existe. Agréguelo en la pestaña Funcionarios.");
            // El líquido puede ser negativo (ábaco): en RedPagos se descuenta del otro pago del funcionario.
            if (f.Adelanto < 0 || f.Retencion < 0)
                errores.Add($"Fila {i + 1} ({f.NumeroFuncionario}): adelanto o retención negativos.");
        }

        LanzarSiHayErrores("No se guardaron los pagos", errores);

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var existentes = await _db.PagosMensuales.Where(x => x.PeriodoId == periodoEntity.Id).ToListAsync();
            var existentesPorId = existentes.ToDictionary(x => x.Id);
            var conservados = new HashSet<int>();

            foreach (var dto in filas)
            {
                if (dto.Id == 0 || !existentesPorId.TryGetValue(dto.Id, out var entidad))
                {
                    entidad = new PagoMensual { PeriodoId = periodoEntity.Id };
                    _db.PagosMensuales.Add(entidad);
                }
                else
                {
                    conservados.Add(entidad.Id);
                }

                var funcionario = funcionarios[dto.NumeroFuncionario.Trim()];
                entidad.FuncionarioId = funcionario.Id;
                entidad.NombreFuncionarioExcel = funcionario.Nombre;
                entidad.TipoObra = dto.TipoObra;
                entidad.TipoObraOriginal = TipoObraParser.Texto(dto.TipoObra);
                entidad.Cliente = dto.TipoObra == TipoObra.Administracion && !string.IsNullOrWhiteSpace(dto.Cliente)
                    ? dto.Cliente.Trim()
                    : ClienteEmpresa;
                entidad.Adelanto = dto.Adelanto;
                entidad.Liquido = dto.Liquido;
                entidad.Retencion = dto.Retencion;
                entidad.TotalGenerado = dto.Adelanto + dto.Liquido + dto.Retencion;
                entidad.Nominal = dto.Nominal;
                entidad.TipoPago = dto.TipoObra == TipoObra.NA ? "Efectivo" : "RedPagos";
                entidad.Observacion = (dto.Observacion ?? string.Empty).Trim();
            }

            var borrados = existentes.Where(x => !conservados.Contains(x.Id)).ToList();
            _db.PagosMensuales.RemoveRange(borrados);
            var distribucionDescartada = await DescartarDistribucionAsync(periodoEntity.Id);

            Auditar(usuario, "Carga mensual", "Guardar pagos", "Periodo", codigo,
                $"Filas: {filas.Count}. Borradas: {borrados.Count}. Total: {filas.Sum(x => x.Adelanto + x.Liquido + x.Retencion):N2}.");

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return $"Pagos de {codigo} guardados: {filas.Count} filas." +
                (distribucionDescartada ? " La distribución anterior se descartó: se recalcula al generar los reportes." : string.Empty);
        }
        catch
        {
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    // ---------------------------------------------------------------- Horas

    public async Task<List<HoraEdicionDto>> ObtenerHorasAsync(string periodo)
    {
        var codigo = (periodo ?? string.Empty).Trim();
        return await _db.HorasMensuales.AsNoTracking()
            .Where(x => x.Periodo!.Codigo == codigo)
            .OrderBy(x => x.RegistroOrigen).ThenBy(x => x.Id)
            .Select(x => new HoraEdicionDto
            {
                Id = x.Id,
                NumeroFuncionario = x.Funcionario!.NumeroFuncionario,
                NumeroObra = x.Obra!.NumeroObra,
                HorasComunes = x.HorasComunes,
                HorasExtras = x.HorasExtras,
                HorasLluvia = x.HorasLluvia
            })
            .ToListAsync();
    }

    public async Task<string> GuardarHorasAsync(string periodo, IReadOnlyList<HoraEdicionDto> filas, string usuario)
    {
        var codigo = (periodo ?? string.Empty).Trim();
        await PeriodoGuard.VerificarAbiertoAsync(_db, codigo);
        var periodoEntity = await ObtenerPeriodoExistenteAsync(codigo);

        var funcionarios = await MapaFuncionariosAsync();
        var obras = (await _db.Obras.ToListAsync())
            .GroupBy(x => x.NumeroObra.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var errores = new List<string>();
        for (var i = 0; i < filas.Count; i++)
        {
            var f = filas[i];
            if (!funcionarios.ContainsKey(f.NumeroFuncionario.Trim()))
                errores.Add($"Fila {i + 1}: el funcionario '{f.NumeroFuncionario}' no existe. Agréguelo en la pestaña Funcionarios.");
            if (!obras.ContainsKey(f.NumeroObra.Trim()))
                errores.Add($"Fila {i + 1} ({f.NumeroFuncionario}): la obra '{f.NumeroObra}' no existe. Agréguela en la pestaña Obras.");
            if (f.HorasComunes < 0 || f.HorasExtras < 0 || f.HorasLluvia < 0)
                errores.Add($"Fila {i + 1} ({f.NumeroFuncionario}): hay horas negativas.");
        }

        LanzarSiHayErrores("No se guardaron las horas", errores);

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var existentes = await _db.HorasMensuales.Where(x => x.PeriodoId == periodoEntity.Id).ToListAsync();
            var existentesPorId = existentes.ToDictionary(x => x.Id);
            var conservados = new HashSet<int>();

            for (var i = 0; i < filas.Count; i++)
            {
                var dto = filas[i];
                if (dto.Id == 0 || !existentesPorId.TryGetValue(dto.Id, out var entidad))
                {
                    entidad = new HoraMensual { PeriodoId = periodoEntity.Id };
                    _db.HorasMensuales.Add(entidad);
                }
                else
                {
                    conservados.Add(entidad.Id);
                }

                var funcionario = funcionarios[dto.NumeroFuncionario.Trim()];
                var obra = obras[dto.NumeroObra.Trim()];
                entidad.FuncionarioId = funcionario.Id;
                entidad.ObraId = obra.Id;
                entidad.RegistroOrigen = i + 1;
                entidad.NombreFuncionarioExcel = funcionario.Nombre;
                entidad.NombreObraExcel = obra.Nombre;
                entidad.Categoria = CategoriaParaHoras(funcionario);
                entidad.Cliente = ClienteDeObra(obra);
                entidad.HorasComunes = dto.HorasComunes;
                entidad.HorasExtras = dto.HorasExtras;
                entidad.HorasLluvia = dto.HorasLluvia;
                entidad.HorasEquivalentes = HoraMensual.CalcularEquivalentes(dto.HorasComunes, dto.HorasExtras, dto.HorasLluvia);
            }

            var borrados = existentes.Where(x => !conservados.Contains(x.Id)).ToList();
            _db.HorasMensuales.RemoveRange(borrados);
            var distribucionDescartada = await DescartarDistribucionAsync(periodoEntity.Id);

            Auditar(usuario, "Carga mensual", "Guardar horas", "Periodo", codigo,
                $"Filas: {filas.Count}. Borradas: {borrados.Count}. Horas equivalentes: {filas.Sum(x => HoraMensual.CalcularEquivalentes(x.HorasComunes, x.HorasExtras, x.HorasLluvia)):N2}.");

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return $"Horas de {codigo} guardadas: {filas.Count} filas." +
                (distribucionDescartada ? " La distribución anterior se descartó: se recalcula al generar los reportes." : string.Empty);
        }
        catch
        {
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    // ---------------------------------------------------------------- Períodos

    public async Task<PeriodoInfoDto?> ObtenerPeriodoAsync(string periodo)
    {
        var codigo = (periodo ?? string.Empty).Trim();
        return await _db.Periodos.AsNoTracking()
            .Where(x => x.Codigo == codigo)
            .Select(x => new PeriodoInfoDto
            {
                Codigo = x.Codigo,
                Cerrado = x.Estado == EstadoPeriodo.Cerrado,
                Pagos = _db.PagosMensuales.Count(p => p.PeriodoId == x.Id),
                Horas = _db.HorasMensuales.Count(h => h.PeriodoId == x.Id)
            })
            .FirstOrDefaultAsync();
    }

    public async Task<string> CrearPeriodoAsync(string periodo, string? copiarDe, string usuario)
    {
        var codigo = (periodo ?? string.Empty).Trim();
        if (!Regex.IsMatch(codigo, "^\\d{4}-(0[1-9]|1[0-2])$"))
            throw new InvalidOperationException("Formato de período inválido. Use AAAA-MM (por ejemplo, 2026-10).");

        var origen = string.IsNullOrWhiteSpace(copiarDe) ? null : copiarDe.Trim();
        if (string.Equals(origen, codigo, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El período a copiar debe ser distinto al nuevo.");

        var periodoEntity = await _db.Periodos.FirstOrDefaultAsync(x => x.Codigo == codigo);
        if (periodoEntity is not null)
        {
            if (periodoEntity.Estado == EstadoPeriodo.Cerrado)
                throw new InvalidOperationException($"El período {codigo} ya existe y está cerrado.");

            var tieneDatos = await _db.PagosMensuales.AnyAsync(x => x.PeriodoId == periodoEntity.Id)
                || await _db.HorasMensuales.AnyAsync(x => x.PeriodoId == periodoEntity.Id);
            if (tieneDatos && origen is not null)
                throw new InvalidOperationException($"El período {codigo} ya existe y tiene datos. No se copió nada para no pisarlos.");
        }

        Periodo? periodoOrigen = null;
        if (origen is not null)
        {
            periodoOrigen = await _db.Periodos.AsNoTracking().FirstOrDefaultAsync(x => x.Codigo == origen)
                ?? throw new InvalidOperationException($"No existe el período {origen} para copiar.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            if (periodoEntity is null)
            {
                periodoEntity = new Periodo { Codigo = codigo, Estado = EstadoPeriodo.Abierto };
                _db.Periodos.Add(periodoEntity);
                await _db.SaveChangesAsync();
            }

            int pagosCopiados = 0, horasCopiadas = 0;
            if (periodoOrigen is not null)
            {
                var funcionarios = await _db.Funcionarios.ToDictionaryAsync(x => x.Id);

                // Pagos: mismas filas (funcionario, tipo de obra, observación) con montos en 0.
                var pagosOrigen = await _db.PagosMensuales.AsNoTracking()
                    .Where(x => x.PeriodoId == periodoOrigen.Id)
                    .OrderBy(x => x.Id)
                    .ToListAsync();
                foreach (var p in pagosOrigen)
                {
                    _db.PagosMensuales.Add(new PagoMensual
                    {
                        PeriodoId = periodoEntity.Id,
                        FuncionarioId = p.FuncionarioId,
                        NombreFuncionarioExcel = funcionarios.TryGetValue(p.FuncionarioId, out var f) ? f.Nombre : p.NombreFuncionarioExcel,
                        TipoObra = p.TipoObra,
                        TipoObraOriginal = TipoObraParser.Texto(p.TipoObra),
                        Cliente = p.Cliente,
                        TipoPago = p.TipoObra == TipoObra.NA ? "Efectivo" : "RedPagos",
                        Observacion = p.Observacion
                    });
                    pagosCopiados++;
                }

                // Horas: mismas combinaciones funcionario/obra con horas en 0.
                var horasOrigen = await _db.HorasMensuales.AsNoTracking()
                    .Include(x => x.Obra)
                    .Where(x => x.PeriodoId == periodoOrigen.Id)
                    .OrderBy(x => x.RegistroOrigen).ThenBy(x => x.Id)
                    .ToListAsync();
                var vistos = new HashSet<(int, int)>();
                foreach (var h in horasOrigen.Where(h => vistos.Add((h.FuncionarioId, h.ObraId))))
                {
                    funcionarios.TryGetValue(h.FuncionarioId, out var func);
                    _db.HorasMensuales.Add(new HoraMensual
                    {
                        PeriodoId = periodoEntity.Id,
                        FuncionarioId = h.FuncionarioId,
                        ObraId = h.ObraId,
                        RegistroOrigen = ++horasCopiadas,
                        NombreFuncionarioExcel = func?.Nombre ?? h.NombreFuncionarioExcel,
                        NombreObraExcel = h.Obra?.Nombre ?? h.NombreObraExcel,
                        Categoria = func is null ? h.Categoria : CategoriaParaHoras(func),
                        Cliente = h.Obra is null ? h.Cliente : ClienteDeObra(h.Obra)
                    });
                }
            }

            Auditar(usuario, "Periodos", "Crear", "Periodo", codigo,
                periodoOrigen is null
                    ? "Período creado vacío."
                    : $"Período creado copiando {pagosCopiados} filas de pagos y {horasCopiadas} de horas desde {origen}.");

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return periodoOrigen is null
                ? $"Período {codigo} creado."
                : $"Período {codigo} creado con {pagosCopiados} filas de pagos y {horasCopiadas} de horas copiadas de {origen} (montos y horas en 0).";
        }
        catch
        {
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    // ---------------------------------------------------------------- Auxiliares

    private async Task<Periodo> ObtenerPeriodoExistenteAsync(string codigo) =>
        await _db.Periodos.FirstOrDefaultAsync(x => x.Codigo == codigo)
        ?? throw new InvalidOperationException($"No existe el período {codigo}. Créelo con 'Nuevo mes'.");

    private async Task<Dictionary<string, Funcionario>> MapaFuncionariosAsync() =>
        (await _db.Funcionarios.ToListAsync())
            .GroupBy(x => x.NumeroFuncionario.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private async Task<List<int>> PeriodosNoCerradosAsync() =>
        await _db.Periodos.Where(x => x.Estado != EstadoPeriodo.Cerrado).Select(x => x.Id).ToListAsync();

    // La distribución guardada deja de ser válida cuando cambian horas o pagos.
    private async Task<bool> DescartarDistribucionAsync(int periodoId)
    {
        var lineas = await _db.DistribucionesCosto.Where(x => x.PeriodoId == periodoId).ToListAsync();
        _db.DistribucionesCosto.RemoveRange(lineas);
        return lineas.Count > 0;
    }

    private static string CategoriaParaHoras(Funcionario funcionario) =>
        string.IsNullOrWhiteSpace(funcionario.Categoria) ? "SIN CATEGORIA" : funcionario.Categoria;

    private static string ClienteDeObra(Obra obra) =>
        obra.TipoObra == TipoObra.Administracion && !string.IsNullOrWhiteSpace(obra.Cliente) ? obra.Cliente : ClienteEmpresa;

    private static long OrdenNumerico(string numero) =>
        long.TryParse(numero, out var n) ? n : long.MaxValue;

    private void Auditar(string usuario, string modulo, string accion, string entidad, string clave, string detalle) =>
        _db.AuditoriaEventos.Add(new AuditoriaEvento
        {
            Usuario = usuario,
            Modulo = modulo,
            Accion = accion,
            Entidad = entidad,
            EntidadClave = clave,
            Detalle = detalle
        });

    private static void LanzarSiHayErrores(string titulo, List<string> errores)
    {
        if (errores.Count == 0)
            return;

        var detalle = string.Join("\n", errores.Take(15));
        if (errores.Count > 15)
            detalle += $"\n... y {errores.Count - 15} más.";
        throw new InvalidOperationException($"{titulo}:\n{detalle}");
    }
}
