using Backend.Business.Abstractions;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using FluentValidation;

namespace Backend.Business.Formaturas.Services;

/// <summary>
/// Gestão dos membros da formatura selecionada.
/// </summary>
/// <param name="vinculoRepository">Vínculos entre usuário e formatura.</param>
/// <param name="papelValidator">Validador da troca de papel.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class MembroService(IVinculoRepository vinculoRepository, IValidator<AlterarPapel> papelValidator, IUnitOfWork unitOfWork)
    : IMembroService
{
    private static readonly Erro MembroNaoEncontrado = Erro.NaoEncontrado("membro.nao_encontrado", "Membro não encontrado nesta formatura.");

    private static readonly Erro UltimoPresidente = Erro.Conflito(
        "formatura.ultimo_presidente",
        "A formatura precisa de ao menos um presidente ativo. Promova outra pessoa antes."
    );

    /// <inheritdoc />
    public async Task<Result<PaginaDe<MembroDaFormatura>>> Listar(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeMembros filtro,
        CancellationToken ct = default
    ) => Result.Ok(await vinculoRepository.ListarMembros(formaturaId, paginacao.Normalizar(), filtro, ct));

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContagemDeMembros>>> Contar(Guid formaturaId, CancellationToken ct = default) =>
        Result.Ok(await vinculoRepository.ContarMembros(formaturaId, ct));

    /// <inheritdoc />
    public async Task<Result> AlterarPapel(Guid formaturaId, Guid usuarioId, AlterarPapel dados, CancellationToken ct = default)
    {
        var validacao = papelValidator.Validar(dados);
        if (validacao.Falhou)
            return validacao;

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var presidentes = await vinculoRepository.TravarPresidentesAtivos(formaturaId, token);
                var vinculo = await vinculoRepository.ObterAtivoParaEdicao(usuarioId, formaturaId, token);

                if (vinculo is null)
                    return Result.Falha(MembroNaoEncontrado);

                if (vinculo.Papel == dados.Papel)
                    return Result.Ok();

                if (DeixariaSemPresidente(vinculo, presidentes))
                    return Result.Falha(UltimoPresidente);

                vinculo.Papel = dados.Papel;

                return Result.Ok();
            },
            ct
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Desativar, nunca apagar: parcelas, pagamentos e adesão continuam apontando para o vínculo.
    /// Prestação de contas de formatura é consultada meses depois da festa.
    /// </remarks>
    public Task<Result> Remover(Guid formaturaId, Guid usuarioId, CancellationToken ct = default) =>
        unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var presidentes = await vinculoRepository.TravarPresidentesAtivos(formaturaId, token);
                var vinculo = await vinculoRepository.ObterAtivoParaEdicao(usuarioId, formaturaId, token);

                if (vinculo is null)
                    return Result.Falha(MembroNaoEncontrado);

                if (DeixariaSemPresidente(vinculo, presidentes))
                    return Result.Falha(UltimoPresidente);

                vinculo.Ativo = false;

                return Result.Ok();
            },
            ct
        );

    /// <summary>
    /// Diz se tirar este vínculo da presidência deixaria a turma sem presidente ativo.
    /// </summary>
    /// <remarks>
    /// Turma sem presidente é turma que ninguém consegue administrar — nem o suporte, que não
    /// tem vínculo com ela.
    /// <para>
    /// A contagem vem de <see cref="IVinculoRepository.TravarPresidentesAtivos"/>, feita com os
    /// vínculos de presidente travados até o commit: dois presidentes rebaixando um ao outro ao
    /// mesmo tempo não conseguem, os dois, passar por esta checagem.
    /// </para>
    /// </remarks>
    /// <param name="vinculo">Vínculo que perderia a presidência.</param>
    /// <param name="presidentesAtivos">Presidentes ativos, contados sob trava.</param>
    private static bool DeixariaSemPresidente(VinculoDeFormatura vinculo, int presidentesAtivos) =>
        vinculo.Papel == PapelNaFormatura.Presidente && presidentesAtivos <= 1;
}
