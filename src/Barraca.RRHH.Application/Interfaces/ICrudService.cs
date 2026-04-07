using Barraca.RRHH.Domain.Entities;

namespace Barraca.RRHH.Application.Interfaces;

public interface ICrudService
{
    Task<List<Funcionario>> ObtenerFuncionariosAsync(bool incluirInactivos = false);
    Task<List<Obra>> ObtenerObrasAsync(bool incluirInactivas = false);
    Task GuardarFuncionarioAsync(Funcionario funcionario, string usuario);
    Task GuardarObraAsync(Obra obra, string usuario);
    Task CambiarEstadoFuncionarioAsync(int funcionarioId, bool activo, string usuario);
    Task CambiarEstadoObraAsync(int obraId, bool activa, string usuario);

    // Deletion for distribution lines
    Task<int> EliminarDistribucionLineasAsync(int[] ids, string usuario);
}
