using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Helpers;

public static class PeriodoGuard
{
    // Impide modificar horas, pagos o distribución de un período cerrado.
    public static async Task VerificarAbiertoAsync(BarracaDbContext db, string periodo)
    {
        var codigo = (periodo ?? string.Empty).Trim();
        var estado = await db.Periodos
            .Where(x => x.Codigo == codigo)
            .Select(x => (EstadoPeriodo?)x.Estado)
            .FirstOrDefaultAsync();

        if (estado == EstadoPeriodo.Cerrado)
            throw new InvalidOperationException(
                $"El período {codigo} está cerrado y no se puede modificar. Si necesita corregirlo, reábralo desde la pestaña Períodos.");
    }
}
