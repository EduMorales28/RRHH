using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Services;

public class ConsistenciaService : IConsistenciaService
{
    private readonly BarracaDbContext _db;

    public ConsistenciaService(BarracaDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ConsistenciaFuncionarioErrorDto>> ValidarConsistenciaFuncionarioAsync(string periodo)
    {
        var periodoCodigo = (periodo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(periodoCodigo))
            throw new ArgumentException("El periodo es requerido.", nameof(periodo));

        var periodoEntity = await _db.Periodos.FirstOrDefaultAsync(x => x.Codigo == periodoCodigo);
        if (periodoEntity is null)
            return Array.Empty<ConsistenciaFuncionarioErrorDto>();

        List<(int FuncionarioId, int RegistrosHoras, decimal TotalHoras)> horasPorFuncionario;
        List<(int FuncionarioId, int RegistrosPagos, decimal TotalPagos)> pagosPorFuncionario;

        if (_db.Database.IsSqlite())
        {
            var horasRaw = await _db.HorasMensuales
                .Where(x => x.PeriodoId == periodoEntity.Id)
                .Select(x => new { x.FuncionarioId, x.HorasEquivalentes })
                .ToListAsync();

            horasPorFuncionario = horasRaw
                .GroupBy(x => x.FuncionarioId)
                .Select(g => (g.Key, g.Count(), g.Sum(x => x.HorasEquivalentes)))
                .ToList();

            var pagosRaw = await _db.PagosMensuales
                .Where(x => x.PeriodoId == periodoEntity.Id)
                .Select(x => new { x.FuncionarioId, x.TotalGenerado })
                .ToListAsync();

            pagosPorFuncionario = pagosRaw
                .GroupBy(x => x.FuncionarioId)
                .Select(g => (g.Key, g.Count(), g.Sum(x => x.TotalGenerado)))
                .ToList();
        }
        else
        {
            horasPorFuncionario = await _db.HorasMensuales
                .Where(x => x.PeriodoId == periodoEntity.Id)
                .GroupBy(x => x.FuncionarioId)
                .Select(g => new ValueTuple<int, int, decimal>(
                    g.Key,
                    g.Count(),
                    g.Sum(x => x.HorasEquivalentes)))
                .ToListAsync();

            pagosPorFuncionario = await _db.PagosMensuales
                .Where(x => x.PeriodoId == periodoEntity.Id)
                .GroupBy(x => x.FuncionarioId)
                .Select(g => new ValueTuple<int, int, decimal>(
                    g.Key,
                    g.Count(),
                    g.Sum(x => x.TotalGenerado)))
                .ToListAsync();
        }

        var funcionarios = await _db.Funcionarios
            .Select(x => new { x.Id, x.NumeroFuncionario, x.Nombre })
            .ToListAsync();

        var funcionariosMap = funcionarios.ToDictionary(x => x.Id, x => x);
        var pagosMap = pagosPorFuncionario.ToDictionary(x => x.FuncionarioId, x => x);
        var horasMap = horasPorFuncionario.ToDictionary(x => x.FuncionarioId, x => x);

        var errores = new List<ConsistenciaFuncionarioErrorDto>();

        foreach (var h in horasPorFuncionario)
        {
            if (pagosMap.ContainsKey(h.FuncionarioId))
                continue;

            if (!funcionariosMap.TryGetValue(h.FuncionarioId, out var func))
                continue;

            errores.Add(new ConsistenciaFuncionarioErrorDto
            {
                Tipo = "HORAS_SIN_PAGOS",
                FuncionarioId = h.FuncionarioId,
                NumeroFuncionario = func.NumeroFuncionario,
                NombreFuncionario = func.Nombre,
                RegistrosHoras = h.RegistrosHoras,
                RegistrosPagos = 0,
                TotalHoras = h.TotalHoras,
                TotalPagos = 0,
                Mensaje = "Tiene horas cargadas pero no tiene pagos asociados en este periodo."
            });
        }

        foreach (var p in pagosPorFuncionario)
        {
            if (horasMap.ContainsKey(p.FuncionarioId))
                continue;

            if (!funcionariosMap.TryGetValue(p.FuncionarioId, out var func))
                continue;

            errores.Add(new ConsistenciaFuncionarioErrorDto
            {
                Tipo = "PAGOS_SIN_HORAS",
                FuncionarioId = p.FuncionarioId,
                NumeroFuncionario = func.NumeroFuncionario,
                NombreFuncionario = func.Nombre,
                RegistrosHoras = 0,
                RegistrosPagos = p.RegistrosPagos,
                TotalHoras = 0,
                TotalPagos = p.TotalPagos,
                Mensaje = "Tiene pagos cargados pero no tiene horas asociadas en este periodo."
            });
        }

        var horasDescuadradasRaw = await _db.HorasMensuales
            .Where(x => x.PeriodoId == periodoEntity.Id)
            .Select(x => new
            {
                x.FuncionarioId,
                x.HorasComunes,
                x.HorasExtras,
                x.HorasEquivalentes
            })
            .ToListAsync();

        var horasDescuadradasPorFuncionario = horasDescuadradasRaw
            .Where(x => Math.Abs((x.HorasComunes + (x.HorasExtras * 2m)) - x.HorasEquivalentes) > 0.01m)
            .GroupBy(x => x.FuncionarioId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var item in horasDescuadradasPorFuncionario)
        {
            if (!funcionariosMap.TryGetValue(item.Key, out var func))
                continue;

            horasMap.TryGetValue(item.Key, out var horasInfo);
            pagosMap.TryGetValue(item.Key, out var pagosInfo);

            errores.Add(new ConsistenciaFuncionarioErrorDto
            {
                Tipo = "HORA_EQUIVALENTE_DESCUADRADA",
                FuncionarioId = item.Key,
                NumeroFuncionario = func.NumeroFuncionario,
                NombreFuncionario = func.Nombre,
                RegistrosHoras = horasInfo.RegistrosHoras,
                RegistrosPagos = pagosInfo.RegistrosPagos,
                TotalHoras = horasInfo.TotalHoras,
                TotalPagos = pagosInfo.TotalPagos,
                Mensaje = $"Hay {item.Value} registro(s) de horas donde HorasEquivalentes no coincide con Comunes + Extras*2."
            });
        }

        var pagosDescuadradosRaw = await _db.PagosMensuales
            .Where(x => x.PeriodoId == periodoEntity.Id)
            .Select(x => new
            {
                x.FuncionarioId,
                x.Adelanto,
                x.Liquido,
                x.Retencion,
                x.TotalGenerado
            })
            .ToListAsync();

        var pagosDescuadradosPorFuncionario = pagosDescuadradosRaw
            .Where(x => Math.Abs((x.Adelanto + x.Liquido + x.Retencion) - x.TotalGenerado) > 0.01m)
            .GroupBy(x => x.FuncionarioId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var item in pagosDescuadradosPorFuncionario)
        {
            if (!funcionariosMap.TryGetValue(item.Key, out var func))
                continue;

            horasMap.TryGetValue(item.Key, out var horasInfo);
            pagosMap.TryGetValue(item.Key, out var pagosInfo);

            errores.Add(new ConsistenciaFuncionarioErrorDto
            {
                Tipo = "PAGO_TOTAL_DESCUADRADO",
                FuncionarioId = item.Key,
                NumeroFuncionario = func.NumeroFuncionario,
                NombreFuncionario = func.Nombre,
                RegistrosHoras = horasInfo.RegistrosHoras,
                RegistrosPagos = pagosInfo.RegistrosPagos,
                TotalHoras = horasInfo.TotalHoras,
                TotalPagos = pagosInfo.TotalPagos,
                Mensaje = $"Hay {item.Value} registro(s) de pagos donde TotalGenerado no coincide con Adelanto + Líquido + Retención."
            });
        }

        var horasTiposRaw = await _db.HorasMensuales
            .Include(x => x.Obra)
            .Where(x => x.PeriodoId == periodoEntity.Id)
            .Select(x => new
            {
                x.FuncionarioId,
                Tipo = x.Obra != null ? x.Obra.TipoObraOriginal : string.Empty
            })
            .ToListAsync();

        var pagosTiposRaw = await _db.PagosMensuales
            .Where(x => x.PeriodoId == periodoEntity.Id)
            .Select(x => new
            {
                x.FuncionarioId,
                Tipo = x.TipoObraOriginal
            })
            .ToListAsync();

        var horasTiposPorFuncionario = horasTiposRaw
            .GroupBy(x => x.FuncionarioId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(y => (y.Tipo ?? string.Empty).Trim().ToUpperInvariant())
                    .Where(y => !string.IsNullOrWhiteSpace(y))
                    .ToHashSet());

        var pagosTiposPorFuncionario = pagosTiposRaw
            .GroupBy(x => x.FuncionarioId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(y => (y.Tipo ?? string.Empty).Trim().ToUpperInvariant())
                    .Where(y => !string.IsNullOrWhiteSpace(y))
                    .ToHashSet());

        foreach (var funcionarioId in horasTiposPorFuncionario.Keys.Intersect(pagosTiposPorFuncionario.Keys))
        {
            var tiposHoras = horasTiposPorFuncionario[funcionarioId];
            var tiposPagos = pagosTiposPorFuncionario[funcionarioId];

            if (tiposHoras.SetEquals(tiposPagos))
                continue;

            if (!funcionariosMap.TryGetValue(funcionarioId, out var func))
                continue;

            horasMap.TryGetValue(funcionarioId, out var horasInfo);
            pagosMap.TryGetValue(funcionarioId, out var pagosInfo);

            errores.Add(new ConsistenciaFuncionarioErrorDto
            {
                Tipo = "TIPO_OBRA_DESALINEADO",
                FuncionarioId = funcionarioId,
                NumeroFuncionario = func.NumeroFuncionario,
                NombreFuncionario = func.Nombre,
                RegistrosHoras = horasInfo.RegistrosHoras,
                RegistrosPagos = pagosInfo.RegistrosPagos,
                TotalHoras = horasInfo.TotalHoras,
                TotalPagos = pagosInfo.TotalPagos,
                Mensaje = $"Tipos en HORAS ({string.Join(", ", tiposHoras.OrderBy(x => x))}) no coinciden con tipos en PAGOS ({string.Join(", ", tiposPagos.OrderBy(x => x))})."
            });
        }

        return errores
            .OrderBy(x => x.Tipo)
            .ThenBy(x => x.NumeroFuncionario)
            .ToList();
    }

    public async Task<IReadOnlyList<ConsistenciaDetalleRegistroDto>> ObtenerDetalleErrorAsync(string periodo, string tipo, int funcionarioId)
    {
        var periodoCodigo = (periodo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(periodoCodigo))
            throw new ArgumentException("El periodo es requerido.", nameof(periodo));

        var tipoError = (tipo ?? string.Empty).Trim().ToUpperInvariant();
        if (tipoError is not ("HORAS_SIN_PAGOS" or "PAGOS_SIN_HORAS" or "HORA_EQUIVALENTE_DESCUADRADA" or "PAGO_TOTAL_DESCUADRADO" or "TIPO_OBRA_DESALINEADO"))
            throw new InvalidOperationException("Tipo de error de consistencia no soportado.");

        var periodoEntity = await _db.Periodos.FirstOrDefaultAsync(x => x.Codigo == periodoCodigo);
        if (periodoEntity is null)
            return Array.Empty<ConsistenciaDetalleRegistroDto>();

        if (tipoError == "HORAS_SIN_PAGOS")
        {
            return await _db.HorasMensuales
                .Include(x => x.Obra)
                .Where(x => x.PeriodoId == periodoEntity.Id && x.FuncionarioId == funcionarioId)
                .OrderBy(x => x.RegistroOrigen)
                .Select(x => new ConsistenciaDetalleRegistroDto
                {
                    Origen = "HORAS",
                    RegistroId = x.Id,
                    FilaOrigen = x.RegistroOrigen,
                    Referencia = x.Obra != null ? $"{x.Obra.NumeroObra} - {x.Obra.Nombre}" : x.NombreObraExcel,
                    CategoriaOTipo = x.Categoria,
                    MontoOHoras = x.HorasEquivalentes,
                    Observacion = $"Comunes: {x.HorasComunes:N2} | Extras: {x.HorasExtras:N2}"
                })
                .ToListAsync();
        }

        if (tipoError == "HORA_EQUIVALENTE_DESCUADRADA")
        {
            var rows = await _db.HorasMensuales
                .Include(x => x.Obra)
                .Where(x => x.PeriodoId == periodoEntity.Id && x.FuncionarioId == funcionarioId)
                .ToListAsync();

            return rows
                .Where(x => Math.Abs((x.HorasComunes + (x.HorasExtras * 2m)) - x.HorasEquivalentes) > 0.01m)
                .OrderBy(x => x.RegistroOrigen)
                .Select(x => new ConsistenciaDetalleRegistroDto
                {
                    Origen = "HORAS",
                    RegistroId = x.Id,
                    FilaOrigen = x.RegistroOrigen,
                    Referencia = x.Obra != null ? $"{x.Obra.NumeroObra} - {x.Obra.Nombre}" : x.NombreObraExcel,
                    CategoriaOTipo = x.Categoria,
                    MontoOHoras = x.HorasEquivalentes,
                    Observacion = $"Comunes: {x.HorasComunes:N2} | Extras: {x.HorasExtras:N2} | Esperado Eq: {(x.HorasComunes + (x.HorasExtras * 2m)):N2}"
                })
                .ToList();
        }

        if (tipoError == "PAGO_TOTAL_DESCUADRADO")
        {
            var rows = await _db.PagosMensuales
                .Where(x => x.PeriodoId == periodoEntity.Id && x.FuncionarioId == funcionarioId)
                .ToListAsync();

            return rows
                .Where(x => Math.Abs((x.Adelanto + x.Liquido + x.Retencion) - x.TotalGenerado) > 0.01m)
                .OrderBy(x => x.Id)
                .Select(x => new ConsistenciaDetalleRegistroDto
                {
                    Origen = "PAGOS",
                    RegistroId = x.Id,
                    FilaOrigen = 0,
                    Referencia = x.Cliente,
                    CategoriaOTipo = x.TipoObraOriginal,
                    MontoOHoras = x.TotalGenerado,
                    Observacion = $"Adelanto: {x.Adelanto:N2} | Líquido: {x.Liquido:N2} | Retención: {x.Retencion:N2} | Esperado Total: {(x.Adelanto + x.Liquido + x.Retencion):N2}"
                })
                .ToList();
        }

        if (tipoError == "TIPO_OBRA_DESALINEADO")
        {
            var horasRows = await _db.HorasMensuales
                .Include(x => x.Obra)
                .Where(x => x.PeriodoId == periodoEntity.Id && x.FuncionarioId == funcionarioId)
                .Select(x => new ConsistenciaDetalleRegistroDto
                {
                    Origen = "HORAS",
                    RegistroId = x.Id,
                    FilaOrigen = x.RegistroOrigen,
                    Referencia = x.Obra != null ? $"{x.Obra.NumeroObra} - {x.Obra.Nombre}" : x.NombreObraExcel,
                    CategoriaOTipo = x.Obra != null ? x.Obra.TipoObraOriginal : string.Empty,
                    MontoOHoras = x.HorasEquivalentes,
                    Observacion = "Tipo de obra reportado desde plantilla de horas."
                })
                .ToListAsync();

            var pagosRows = await _db.PagosMensuales
                .Where(x => x.PeriodoId == periodoEntity.Id && x.FuncionarioId == funcionarioId)
                .Select(x => new ConsistenciaDetalleRegistroDto
                {
                    Origen = "PAGOS",
                    RegistroId = x.Id,
                    FilaOrigen = 0,
                    Referencia = x.Cliente,
                    CategoriaOTipo = x.TipoObraOriginal,
                    MontoOHoras = x.TotalGenerado,
                    Observacion = "Tipo de obra reportado desde plantilla de pagos."
                })
                .ToListAsync();

            return horasRows
                .Concat(pagosRows)
                .OrderBy(x => x.Origen)
                .ThenBy(x => x.RegistroId)
                .ToList();
        }

        return await _db.PagosMensuales
            .Where(x => x.PeriodoId == periodoEntity.Id && x.FuncionarioId == funcionarioId)
            .OrderBy(x => x.Id)
            .Select(x => new ConsistenciaDetalleRegistroDto
            {
                Origen = "PAGOS",
                RegistroId = x.Id,
                FilaOrigen = 0,
                Referencia = x.Cliente,
                CategoriaOTipo = x.TipoObraOriginal,
                MontoOHoras = x.TotalGenerado,
                Observacion = $"Tipo pago: {x.TipoPago}"
            })
            .ToListAsync();
    }

    public async Task CorregirConsistenciaFuncionarioAsync(string periodo, string tipo, int funcionarioIdOrigen, string numeroFuncionarioDestino, string usuario)
    {
        var periodoCodigo = (periodo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(periodoCodigo))
            throw new ArgumentException("El periodo es requerido.", nameof(periodo));

        var tipoError = (tipo ?? string.Empty).Trim().ToUpperInvariant();
        if (tipoError is not ("HORAS_SIN_PAGOS" or "PAGOS_SIN_HORAS"))
            throw new InvalidOperationException("Tipo de error de consistencia no soportado.");

        var numeroDestino = (numeroFuncionarioDestino ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(numeroDestino))
            throw new InvalidOperationException("Debe ingresar un número de funcionario destino para corregir.");

        var periodoEntity = await _db.Periodos.FirstOrDefaultAsync(x => x.Codigo == periodoCodigo)
            ?? throw new InvalidOperationException($"No existe el período '{periodoCodigo}'.");

        var funcionarioDestino = await _db.Funcionarios.FirstOrDefaultAsync(x => x.NumeroFuncionario == numeroDestino)
            ?? throw new InvalidOperationException($"No existe el funcionario destino '{numeroDestino}'.");

        if (funcionarioDestino.Id == funcionarioIdOrigen)
            throw new InvalidOperationException("El funcionario destino debe ser distinto al funcionario con inconsistencia.");

        var funcionarioOrigen = await _db.Funcionarios.FirstOrDefaultAsync(x => x.Id == funcionarioIdOrigen)
            ?? throw new InvalidOperationException("No existe el funcionario origen.");

        var cantidadAfectada = 0;

        if (tipoError == "HORAS_SIN_PAGOS")
        {
            var horas = await _db.HorasMensuales
                .Where(x => x.PeriodoId == periodoEntity.Id && x.FuncionarioId == funcionarioIdOrigen)
                .ToListAsync();

            foreach (var hora in horas)
            {
                hora.FuncionarioId = funcionarioDestino.Id;
                hora.NombreFuncionarioExcel = funcionarioDestino.Nombre;
                cantidadAfectada++;
            }
        }
        else
        {
            var pagos = await _db.PagosMensuales
                .Where(x => x.PeriodoId == periodoEntity.Id && x.FuncionarioId == funcionarioIdOrigen)
                .ToListAsync();

            foreach (var pago in pagos)
            {
                pago.FuncionarioId = funcionarioDestino.Id;
                pago.NombreFuncionarioExcel = funcionarioDestino.Nombre;
                cantidadAfectada++;
            }
        }

        if (cantidadAfectada == 0)
            throw new InvalidOperationException("No se encontraron registros para corregir con ese funcionario en el período indicado.");

        _db.AuditoriaEventos.Add(new AuditoriaEvento
        {
            Usuario = string.IsNullOrWhiteSpace(usuario) ? "sistema" : usuario,
            Modulo = "Consistencia",
            Accion = "Corregir",
            Entidad = "Funcionario",
            EntidadClave = funcionarioOrigen.NumeroFuncionario,
            Detalle = $"{tipoError}: {funcionarioOrigen.NumeroFuncionario} -> {funcionarioDestino.NumeroFuncionario} en periodo {periodoCodigo}. Registros: {cantidadAfectada}"
        });

        await _db.SaveChangesAsync();
    }

    public async Task GuardarDetalleErrorAsync(string periodo, ConsistenciaDetalleRegistroDto detalle, string usuario)
    {
        var periodoCodigo = (periodo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(periodoCodigo))
            throw new ArgumentException("El periodo es requerido.", nameof(periodo));

        if (detalle is null)
            throw new InvalidOperationException("El detalle a guardar es requerido.");

        var periodoEntity = await _db.Periodos.FirstOrDefaultAsync(x => x.Codigo == periodoCodigo)
            ?? throw new InvalidOperationException($"No existe el período '{periodoCodigo}'.");

        var origen = (detalle.Origen ?? string.Empty).Trim().ToUpperInvariant();
        if (origen is "HORAS")
        {
            var hora = await _db.HorasMensuales
                .FirstOrDefaultAsync(x => x.Id == detalle.RegistroId && x.PeriodoId == periodoEntity.Id)
                ?? throw new InvalidOperationException("No se encontró el registro de horas a editar.");

            hora.Categoria = (detalle.CategoriaOTipo ?? string.Empty).Trim();
            hora.HorasEquivalentes = detalle.MontoOHoras;

            if (!string.IsNullOrWhiteSpace(detalle.NumeroFuncionarioDestino))
            {
                var destino = await _db.Funcionarios
                    .FirstOrDefaultAsync(x => x.NumeroFuncionario == detalle.NumeroFuncionarioDestino.Trim())
                    ?? throw new InvalidOperationException($"No existe el funcionario destino '{detalle.NumeroFuncionarioDestino}'.");

                hora.FuncionarioId = destino.Id;
                hora.NombreFuncionarioExcel = destino.Nombre;
            }
        }
        else if (origen is "PAGOS")
        {
            var pago = await _db.PagosMensuales
                .FirstOrDefaultAsync(x => x.Id == detalle.RegistroId && x.PeriodoId == periodoEntity.Id)
                ?? throw new InvalidOperationException("No se encontró el registro de pagos a editar.");

            pago.TipoObraOriginal = (detalle.CategoriaOTipo ?? string.Empty).Trim();
            pago.TotalGenerado = detalle.MontoOHoras;
            pago.Observacion = (detalle.Observacion ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(detalle.NumeroFuncionarioDestino))
            {
                var destino = await _db.Funcionarios
                    .FirstOrDefaultAsync(x => x.NumeroFuncionario == detalle.NumeroFuncionarioDestino.Trim())
                    ?? throw new InvalidOperationException($"No existe el funcionario destino '{detalle.NumeroFuncionarioDestino}'.");

                pago.FuncionarioId = destino.Id;
                pago.NombreFuncionarioExcel = destino.Nombre;
            }
        }
        else
        {
            throw new InvalidOperationException("Origen de detalle no soportado para edición.");
        }

        _db.AuditoriaEventos.Add(new AuditoriaEvento
        {
            Usuario = string.IsNullOrWhiteSpace(usuario) ? "sistema" : usuario,
            Modulo = "Consistencia",
            Accion = "EditarDetalle",
            Entidad = detalle.Origen,
            EntidadClave = detalle.RegistroId.ToString(),
            Detalle = $"Edición directa de inconsistencia en periodo {periodoCodigo}."
        });

        await _db.SaveChangesAsync();
    }
}
