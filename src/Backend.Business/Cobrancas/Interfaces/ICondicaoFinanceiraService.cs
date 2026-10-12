using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// O desconto ou a isenção individual que a tesouraria autoriza (Sprint 54).
/// </summary>
/// <remarks>
/// A pessoa é identificada pelo usuário, se já entrou, ou pelo e-mail do convite pessoal, se ainda não. Ler é da
/// Gestão; conceder, alterar e revogar, da Tesouraria (P9/P14).
/// </remarks>
public interface ICondicaoFinanceiraService
{
    /// <summary>A condição da pessoa, com o que cada benefício desconta no catálogo de hoje.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="pessoa">Usuário ou e-mail do convite.</param>
    Task<Result<CondicaoDaPessoa>> Obter(Guid formaturaId, PessoaDaCondicao pessoa, CancellationToken ct = default);

    /// <summary>Concede ou altera a condição da pessoa. Não muda o que ela já aceitou.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="pessoa">Usuário ou e-mail do convite.</param>
    /// <param name="dados">Os benefícios.</param>
    /// <param name="autorId">Quem concede.</param>
    Task<Result<CondicaoDaPessoa>> Salvar(
        Guid formaturaId,
        PessoaDaCondicao pessoa,
        DadosDaCondicao dados,
        Guid autorId,
        CancellationToken ct = default
    );

    /// <summary>Revoga a condição. O que já foi aceito continua valendo.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="pessoa">Usuário ou e-mail do convite.</param>
    /// <param name="autorId">Quem revoga.</param>
    Task<Result> Remover(Guid formaturaId, PessoaDaCondicao pessoa, Guid autorId, CancellationToken ct = default);
}
