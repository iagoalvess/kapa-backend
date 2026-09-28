using Backend.Business.Abstractions;

namespace Backend.Business.Usuarios.Models;

/// <summary>
/// As falhas do usuário que mais de um service devolve.
/// </summary>
public static class ErrosDeUsuario
{
    /// <summary>Não existe conta com este id.</summary>
    public static readonly Erro UsuarioNaoEncontrado = Erro.NaoEncontrado("usuario.nao_encontrado", "Usuário não encontrado.");
}
