using Backend.Business.Abstractions;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;

namespace Backend.Business.Formaturas.Services;

/// <summary>
/// Listagem e seleção da formatura da sessão.
/// </summary>
/// <param name="vinculoRepository">Consulta dos vínculos do usuário.</param>
/// <param name="authService">Emissão da sessão com a formatura escolhida.</param>
public sealed class FormaturaService(IVinculoRepository vinculoRepository, IAuthService authService) : IFormaturaService
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FormaturaDoUsuario>>> ListarMinhas(Guid usuarioId, CancellationToken ct = default) =>
        Result.Ok(await vinculoRepository.ListarDoUsuario(usuarioId, ct));

    /// <inheritdoc />
    /// <remarks>
    /// O vínculo é conferido <b>aqui</b>, antes de qualquer token existir. É o que garante que a
    /// API só assine uma formatura que o usuário comprovadamente acessa — e por isso a claim
    /// pode ser confiada dali para a frente.
    /// </remarks>
    public async Task<Result<ParDeTokens>> Selecionar(
        Guid usuarioId,
        Guid formaturaId,
        string refreshTokenAtual,
        string? ipDeOrigem,
        CancellationToken ct = default
    )
    {
        var papel = await vinculoRepository.ObterPapelAtivo(usuarioId, formaturaId, ct);

        if (papel is null)
            return Erro.Proibido("formatura.sem_vinculo", "Você não participa desta formatura.");

        return await authService.EmitirSessaoDeFormatura(usuarioId, formaturaId, papel, refreshTokenAtual, ipDeOrigem, ct);
    }
}
