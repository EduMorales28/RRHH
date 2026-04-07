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

        var horasPorFuncionario = await _db.HorasMensuales
            .Where(x => x.PeriodoId == periodoEntity.Id)
            .GroupBy(x => x.FuncionarioId)
            .Select(g => new
            {
                FuncionarioId = g.Key,
                RegistrosHoras = g.Count(),
                TotalHoras = g.Sum(x => x.HorasEquivalentes)
            })
            .ToListAsync();

        var pagosPorFuncionario = await _db.PagosMensuales
            .Where(x => x.PeriodoId == periodoEntity.Id)
            .GroupBy(x => x.FuncionarioId)
            .Select(g => new
            {
                FuncionarioId = g.Key,
                RegistrosPagos = g.Count(),
                TotalPagos = g.Sum(x => x.TotalGenerado)
            })
            .ToListAsync();

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
        if (tipoError is not ("HORAS_SIN_PAGOS" or "PAGOS_SIN_HORAS"))
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
}
