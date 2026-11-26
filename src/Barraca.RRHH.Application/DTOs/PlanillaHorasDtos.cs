using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Application.DTOs;

// Archivo de horas del mes ("DATOS SUELDO <MES>.xlsm"): una fila por funcionario y obra.
public class PlanillaHorasDto
{
    public string Archivo { get; set; } = string.Empty;
    public List<FilaHorasDto> Filas { get; set; } = new();
    // Filas con 0 horas (por ejemplo "Faltas" o "Administración") que no se cargan.
    public int FilasSinHoras { get; set; }
    // Tickets de alimentación (columna "Tck Alimentacion") por persona y tipo de obra.
    public List<TicketsHorasDto> Tickets { get; set; } = new();
}

public class TicketsHorasDto
{
    public TipoObra TipoObra { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
}

public class FilaHorasDto
{
    public TipoObra TipoObra { get; set; }
    public string Codigo { get; set; } = string.Empty;
    // Nombre con la categoría entre paréntesis, tal como viene: "Eduardo Figueira(XI-Maquinista)".
    public string Nombre { get; set; } = string.Empty;
    // Nombre de la obra sin el número de BPS.
    public string Obra { get; set; } = string.Empty;
    public decimal Comunes { get; set; }
    public decimal Extras { get; set; }
    public decimal Lluvia { get; set; }
}

public class EmparejamientoPersonaHorasDto
{
    public TipoObra TipoObra { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string NumeroFuncionario { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public bool RequiereRevision { get; set; }
}

public class EmparejamientoObraHorasDto
{
    public TipoObra TipoObra { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string NumeroObra { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public bool RequiereRevision { get; set; }
    public bool CrearNueva { get; set; }
}

public class EmparejamientoHorasDto
{
    public List<EmparejamientoPersonaHorasDto> Personas { get; set; } = new();
    public List<EmparejamientoObraHorasDto> Obras { get; set; } = new();
}

public static class EstadosHoras
{
    public const string Recordado = "OK (confirmado antes)";
    public const string ConPago = "OK (nombre y pago del mes)";
    public const string MismoNombre = "OK (mismo nombre)";
    public const string VariosNumeros = "REVISAR: la persona tiene varios números";
    public const string SinPago = "REVISAR: no tiene pagos de este tipo en el mes";
    public const string NoExiste = "REVISAR: no se encontró en el programa";
    public const string ObraRecordada = "OK (confirmada antes)";
    public const string ObraPorNombre = "OK (mismo nombre)";
    public const string ObraDudosa = "REVISAR: varias obras parecidas";
    public const string ObraNueva = "NUEVA: no existe en el programa";
}
