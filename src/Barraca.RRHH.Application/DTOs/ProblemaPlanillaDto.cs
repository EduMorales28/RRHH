namespace Barraca.RRHH.Application.DTOs;

public class ProblemaPlanillaDto
{
    public const string Error = "Error";
    public const string Aviso = "Aviso";

    public int? Fila { get; set; }
    public string Severidad { get; set; } = Aviso;
    public string Funcionario { get; set; } = string.Empty;
    public string Problema { get; set; } = string.Empty;
    public string Sugerencia { get; set; } = string.Empty;

    public bool EsError => Severidad == Error;
}
