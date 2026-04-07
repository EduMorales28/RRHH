namespace Barraca.RRHH.Application.DTOs;

public class ConsistenciaDetalleRegistroDto
{
    public string Origen { get; set; } = string.Empty;
    public int RegistroId { get; set; }
    public int FilaOrigen { get; set; }
    public string Referencia { get; set; } = string.Empty;
    public string CategoriaOTipo { get; set; } = string.Empty;
    public decimal MontoOHoras { get; set; }
    public string Observacion { get; set; } = string.Empty;
    public string NumeroFuncionarioDestino { get; set; } = string.Empty;
}
