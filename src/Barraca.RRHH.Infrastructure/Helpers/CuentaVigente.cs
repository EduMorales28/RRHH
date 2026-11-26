using Barraca.RRHH.Domain.Entities;

namespace Barraca.RRHH.Infrastructure.Helpers;

public static class CuentaVigente
{
    // Cuenta a usar en los listados de pago: la activa más reciente, priorizando la del mismo tipo de pago.
    public static CuentaPagoFuncionario? Seleccionar(IEnumerable<CuentaPagoFuncionario>? cuentas, string? tipoPago)
    {
        var lista = (cuentas ?? []).OrderByDescending(c => c.Id).ToList();
        var activas = lista.Where(c => c.Activa).ToList();
        var tipo = (tipoPago ?? string.Empty).Trim();

        return activas.FirstOrDefault(c => MismoTipo(c.TipoPago, tipo))
            ?? activas.FirstOrDefault()
            ?? lista.FirstOrDefault();
    }

    private static bool MismoTipo(string? cuentaTipo, string pagoTipo)
    {
        var a = Compactar(cuentaTipo);
        var b = Compactar(pagoTipo);
        return a.Length > 0 && b.Length > 0 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal));
    }

    // "RED PAGOS" y "RedPagos" se consideran iguales.
    private static string Compactar(string? valor) =>
        new string((valor ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
