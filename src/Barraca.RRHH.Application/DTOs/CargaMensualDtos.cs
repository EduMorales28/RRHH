using Barraca.RRHH.Domain.Enums;

namespace Barraca.RRHH.Application.DTOs;

// Filas que se cargan y editan directamente en el programa (en lugar de importar planillas).

public class FuncionarioEdicionDto
{
    public int Id { get; set; }
    public string NumeroFuncionario { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public string TipoPago { get; set; } = string.Empty;
    public string Banco { get; set; } = string.Empty;
    public string CuentaNueva { get; set; } = string.Empty;
    public string CuentaVieja { get; set; } = string.Empty;
    public string ResponsableRetencion { get; set; } = string.Empty;
    public string BancoRetencion { get; set; } = string.Empty;
    public string CuentaRetencion { get; set; } = string.Empty;
    public decimal RetencionFija { get; set; }
    public bool HoraFijaSede { get; set; }
    public bool Activo { get; set; } = true;
}

public class ObraEdicionDto
{
    public int Id { get; set; }
    public string NumeroObra { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public TipoObra TipoObra { get; set; } = TipoObra.Construccion;
    public string Cliente { get; set; } = string.Empty;
    public bool Activa { get; set; } = true;
}

public class PagoEdicionDto
{
    public int Id { get; set; }
    public string NumeroFuncionario { get; set; } = string.Empty;
    public TipoObra TipoObra { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public decimal Adelanto { get; set; }
    public decimal Liquido { get; set; }
    public decimal Retencion { get; set; }
    public string Observacion { get; set; } = string.Empty;
}

public class HoraEdicionDto
{
    public int Id { get; set; }
    public string NumeroFuncionario { get; set; } = string.Empty;
    public string NumeroObra { get; set; } = string.Empty;
    public decimal HorasComunes { get; set; }
    public decimal HorasExtras { get; set; }
    public decimal HorasLluvia { get; set; }
}

public class PeriodoInfoDto
{
    public string Codigo { get; set; } = string.Empty;
    public bool Cerrado { get; set; }
    public int Pagos { get; set; }
    public int Horas { get; set; }
}
