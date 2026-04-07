using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Services;

public class CrudService : ICrudService
{
    private readonly BarracaDbContext _db;

    public CrudService(BarracaDbContext db)
    {
        _db = db;
    }

    public Task<List<Funcionario>> ObtenerFuncionariosAsync(bool incluirInactivos = false)
    {
        var q = _db.Funcionarios.Include(x => x.CuentasPago).AsQueryable();
        if (!incluirInactivos) q = q.Where(x => x.Activo);
        return q.OrderBy(x => x.NumeroFuncionario).ToListAsync();
    }

    public Task<List<Obra>> ObtenerObrasAsync(bool incluirInactivas = false)
    {
        var q = _db.Obras.AsQueryable();
        if (!incluirInactivas) q = q.Where(x => x.Activa);
        return q.OrderBy(x => x.NumeroObra).ToListAsync();
    }

    public async Task GuardarFuncionarioAsync(Funcionario funcionario, string usuario)
    {
        if (funcionario.Id == 0) _db.Funcionarios.Add(funcionario); else _db.Funcionarios.Update(funcionario);
        _db.AuditoriaEventos.Add(new AuditoriaEvento { Usuario = usuario, Modulo = "Funcionarios", Accion = funcionario.Id == 0 ? "Alta" : "Edicion", Entidad = "Funcionario", EntidadClave = funcionario.NumeroFuncionario, Detalle = funcionario.Nombre });
        await _db.SaveChangesAsync();
    }

    public async Task GuardarObraAsync(Obra obra, string usuario)
    {
        if (obra.Id == 0) _db.Obras.Add(obra); else _db.Obras.Update(obra);
        _db.AuditoriaEventos.Add(new AuditoriaEvento { Usuario = usuario, Modulo = "Obras", Accion = obra.Id == 0 ? "Alta" : "Edicion", Entidad = "Obra", EntidadClave = obra.NumeroObra, Detalle = obra.Nombre });
        await _db.SaveChangesAsync();
    }

    public async Task CambiarEstadoFuncionarioAsync(int funcionarioId, bool activo, string usuario)
    {
        var entidad = await _db.Funcionarios.FirstAsync(x => x.Id == funcionarioId);
        entidad.Activo = activo;
        _db.AuditoriaEventos.Add(new AuditoriaEvento { Usuario = usuario, Modulo = "Funcionarios", Accion = activo ? "Activar" : "Inactivar", Entidad = "Funcionario", EntidadClave = entidad.NumeroFuncionario, Detalle = entidad.Nombre });
        await _db.SaveChangesAsync();
    }

    public async Task CambiarEstadoObraAsync(int obraId, bool activa, string usuario)
    {
        var entidad = await _db.Obras.FirstAsync(x => x.Id == obraId);
        entidad.Activa = activa;
        _db.AuditoriaEventos.Add(new AuditoriaEvento { Usuario = usuario, Modulo = "Obras", Accion = activa ? "Activar" : "Inactivar", Entidad = "Obra", EntidadClave = entidad.NumeroObra, Detalle = entidad.Nombre });
        await _db.SaveChangesAsync();
    }

    public async Task<int> EliminarDistribucionLineasAsync(int[] ids, string usuario)
    {
        var entidades = await _db.DistribucionesCosto.Where(x => ids.Contains(x.Id)).ToListAsync();
        if (entidades.Count == 0) return 0;

        _db.DistribucionesCosto.RemoveRange(entidades);

        foreach (var e in entidades)
        {
            _db.AuditoriaEventos.Add(new AuditoriaEvento { Usuario = usuario, Modulo = "Distribucion", Accion = "Eliminar", Entidad = "DistribucionCosto", EntidadClave = e.Id.ToString(), Detalle = $"ObraId={e.ObraId}; Categoria={e.Categoria}" });
        }

        var res = await _db.SaveChangesAsync();
        return entidades.Count;
    }
}
