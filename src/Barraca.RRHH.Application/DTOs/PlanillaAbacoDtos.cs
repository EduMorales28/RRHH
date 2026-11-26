using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Application.DTOs;

// "Planilla de Sueldos a Pagar" de ábaco: una por empresa (Construcción o Industria y Comercio).
public class PlanillaAbacoDto
{
    public string Archivo { get; set; } = string.Empty;
    public string Empresa { get; set; } = string.Empty;
    public TipoObra TipoObra { get; set; }
    public string Mes { get; set; } = string.Empty;
    // AAAA-MM, o vacío si no se pudo leer el mes.
    public string Periodo { get; set; } = string.Empty;
    public List<FilaAbacoDto> Filas { get; set; } = new();
    // Números de la línea "Totales" del pie, en el orden en que aparecen.
    public List<decimal> TotalesPie { get; set; } = new();
    public bool TotalesVerificados { get; set; }
}

public class FilaAbacoDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public decimal Nominal { get; set; }
    public decimal Aportes { get; set; }
    public decimal Adelantos { get; set; }
    public decimal RetencionJudicial { get; set; }
    public decimal Pago { get; set; }

    // Nominal − Aportes − Adelantos − Ret. Jud. debe dar el Pago (ábaco redondea cada fila al peso).
    public bool Cuadra => Math.Abs(Nominal - Aportes - Adelantos - RetencionJudicial - Pago) <= 1m;
}

// Funcionario del programa sugerido para una fila de ábaco.
public class EmparejamientoAbacoDto
{
    public TipoObra TipoObra { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string NumeroFuncionario { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public bool RequiereRevision { get; set; }
}

public static class EstadosAbaco
{
    public const string Recordado = "OK (confirmado antes)";
    public const string MismoCodigo = "OK (mismo código y nombre)";
    public const string MismoCodigoNombreParecido = "REVISAR: mismo número, nombre parecido";
    public const string CodigoOtraPersona = "REVISAR: ese número es otra persona";
    public const string SugeridoPorNombre = "REVISAR: sugerido por nombre";
    public const string NoExiste = "NUEVO: no existe en el programa";
}
