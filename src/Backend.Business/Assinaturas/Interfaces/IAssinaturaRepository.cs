using Backend.Business.Assinaturas.Models;

namespace Backend.Business.Assinaturas.Interfaces;

/// <summary>
/// Planos, assinaturas e eventos de cobrança.
/// </summary>
/// <remarks>
/// As consultas sem sufixo passam pelo filtro global: enxergam só a formatura da sessão. As com
/// sufixo <c>DeTodasAsFormaturas</c> são do webhook e do worker, que não têm sessão — e o nome diz
/// isso de propósito.
/// </remarks>
public interface IAssinaturaRepository
{
    /// <summary>Planos contratáveis, do mais barato ao mais caro.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    Task<IReadOnlyList<PlanoResumo>> ListarPlanosAtivos(CancellationToken ct = default);

    /// <summary>Plano contratável pelo código, ou nulo.</summary>
    /// <param name="codigo">Código do plano.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Plano?> ObterPlanoAtivo(string codigo, CancellationToken ct = default);

    /// <summary>Plano pelo id, contratável ou não — assinatura antiga continua valendo num plano retirado.</summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Plano?> ObterPlano(Guid planoId, CancellationToken ct = default);

    /// <summary>A assinatura mais recente da formatura da sessão, com o plano.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    Task<AssinaturaDetalhe?> ObterDetalheDaMaisRecente(CancellationToken ct = default);

    /// <summary>A assinatura mais recente da formatura da sessão, rastreada para alteração.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Assinatura?> ObterMaisRecenteParaEdicao(CancellationToken ct = default);

    /// <summary>Marca uma assinatura nova para inclusão.</summary>
    /// <param name="assinatura">Assinatura a persistir.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task Adicionar(Assinatura assinatura, CancellationToken ct = default);

    /// <summary>Assinatura de qualquer formatura, rastreada. É o webhook que chama: não há sessão.</summary>
    /// <param name="assinaturaId">Assinatura.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Assinatura?> ObterParaEdicaoDeTodasAsFormaturas(Guid assinaturaId, CancellationToken ct = default);

    /// <summary>
    /// Assinaturas pendentes, de todas as formaturas, paradas dentro da janela informada.
    /// </summary>
    /// <remarks>
    /// Consulta várias formaturas de propósito — e o nome diz isso. A janela tem piso para o checkout
    /// abandonado não ser consultado no provedor para sempre.
    /// </remarks>
    /// <param name="atualizadasAntesDe">Parada há pelo menos este tempo.</param>
    /// <param name="atualizadasDepoisDe">Mas não há mais que este.</param>
    /// <param name="limite">Máximo de linhas.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<IReadOnlyList<Assinatura>> ListarPendentesDeTodasAsFormaturas(
        DateTime atualizadasAntesDe,
        DateTime atualizadasDepoisDe,
        int limite,
        CancellationToken ct = default
    );

    /// <summary>Assinaturas ativas ou canceladas, de todas as formaturas, com vigência até a data. Rastreadas.</summary>
    /// <remarks>Alimenta o vencimento e os avisos: quem vence até a data é candidato aos dois.</remarks>
    /// <param name="vigentesAte">Limite superior da vigência.</param>
    /// <param name="limite">Máximo de linhas.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<IReadOnlyList<Assinatura>> ListarVencendoDeTodasAsFormaturas(DateTime vigentesAte, int limite, CancellationToken ct = default);

    /// <summary>
    /// Grava o evento se o id ainda não existir, e diz se gravou.
    /// </summary>
    /// <remarks>
    /// <c>INSERT … ON CONFLICT DO NOTHING</c>, e por isso <b>precisa rodar dentro de
    /// <c>IUnitOfWork.EmTransacaoAsync</c></b>: a linha fica presa à transação de quem aplica o efeito.
    /// Duas entregas simultâneas do mesmo evento esbarram no índice único — a segunda espera a
    /// primeira terminar e recebe <c>false</c>, sem exceção e sem reprocessar. É a mesma exceção
    /// documentada de <c>ReservarLote</c>: SQL explícito dentro da transação do service.
    /// </remarks>
    /// <param name="evento">Evento recebido.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<bool> RegistrarSeNovo(EventoDeCobranca evento, CancellationToken ct = default);
}
