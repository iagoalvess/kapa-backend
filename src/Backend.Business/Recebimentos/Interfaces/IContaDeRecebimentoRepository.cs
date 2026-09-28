using Backend.Business.Recebimentos.Models;

namespace Backend.Business.Recebimentos.Interfaces;

/// <summary>
/// A conta de recebimento da formatura selecionada — uma só, isolada pelo filtro global.
/// </summary>
public interface IContaDeRecebimentoRepository
{
    /// <summary>A conta, com o nome de quem a conferiu, ou nulo se a turma ainda não cadastrou.</summary>
    Task<ContaDeRecebimentoDetalhe?> ObterDetalhe(CancellationToken ct = default);

    /// <summary>
    /// Os meios como estavam num instante do passado, lidos da trilha de auditoria; nulo se ela não
    /// tiver nenhum cadastro ou troca até lá.
    /// </summary>
    /// <remarks>
    /// É o "titular da conta que recebeu" do recibo (Sprint 22, P2): a conta de hoje pode não ser a do
    /// dia do pagamento, e o recibo não pode mudar de conteúdo com uma troca de chave. O evento de
    /// cadastro e o de troca carregam o <c>depois</c> inteiro, e a trilha é append-only — o mesmo
    /// instante devolve sempre os mesmos meios. <c>ponytail:</c> a auditoria vive 5 anos
    /// (<c>Eventos:DiasDeRetencaoAuditoria</c>); passado isso, quem chama cai na conta atual.
    /// </remarks>
    /// <param name="formaturaId">Turma — a tabela de eventos não tem filtro global.</param>
    /// <param name="instanteUtc">Instante de referência.</param>
    Task<MeiosDaConta?> ObterMeiosVigentesEm(Guid formaturaId, DateTime instanteUtc, CancellationToken ct = default);

    /// <summary>A conta, rastreada para alteração, ou nulo.</summary>
    Task<ContaDeRecebimento?> ObterParaEdicao(CancellationToken ct = default);

    /// <summary>Registra a primeira conta da turma.</summary>
    /// <param name="conta">Conta a persistir.</param>
    Task Adicionar(ContaDeRecebimento conta, CancellationToken ct = default);
}
