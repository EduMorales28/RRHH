using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Infrastructure.Data;

namespace Barraca.RRHH.Infrastructure.Services;

public class AuditoriaService
{
    private readonly BarracaDbContext _db;

    public AuditoriaService(BarracaDbContext db)
    {
        _db = db;
    }

    public async Task RegistrarAsync(string usuario, string modulo, string accion, string entidad, string entidadClave, string detalle)
    {
        _db.AuditoriaEventos.Add(new AuditoriaEvento
        {
            Usuario = usuario,
            Modulo = modulo,
            Accion = accion,
            Entidad = entidad,
            EntidadClave = entidadClave,
            Detalle = detalle
        });

        await _db.SaveChangesAsync();
    }
}
