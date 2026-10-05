using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// O plano financeiro da turma: a tesouraria monta e simula, o Presidente põe em vigor. As parcelas, a gestão
/// consulta pelo <see cref="IConsultaDeParcelasService"/>.
/// </summary>
/// <remarks>
/// A formatura vem da sessão, pelo filtro global. Só <see cref="Simular"/> a recebe, para contar
/// os membros — o vínculo não é isolado pelo filtro.
/// </remarks>
public interface ICobrancaService
{
    /// <summary>Os planos da turma.</summary>
    Task<Result<IReadOnlyList<PlanoDeCobrancaResumo>>> Listar(CancellationToken ct = default);

    /// <summary>Um plano, com os itens.</summary>
    /// <param name="planoId">Plano.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> Obter(Guid planoId, CancellationToken ct = default);

    /// <summary>Cria um plano em montagem, sem itens.</summary>
    /// <param name="dados">Nome e regras de atraso.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> Criar(DadosDoPlano dados, CancellationToken ct = default);

    /// <summary>Altera nome e regras de atraso.</summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="dados">Nome e regras de atraso.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> Atualizar(Guid planoId, DadosDoPlano dados, CancellationToken ct = default);

    /// <summary>
    /// Inclui um pacote no catálogo. Com <paramref name="rateio"/>, inclui o item da assembleia, que alcança também
    /// quem já aderiu — e aí só o item de <paramref name="dados"/> conta.
    /// </summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="dados">Pacote: o item, o grupo de faixas, os benefícios e o último vencimento.</param>
    /// <param name="rateio">Rateio extraordinário; ausente, o item vale só para quem aderir depois.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> AdicionarItem(
        Guid planoId,
        DadosDoPacote dados,
        RateioExtraordinario? rateio = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// Altera um item. Com parcela gerada, só o valor muda — no catálogo, para quem aderir depois; e, com
    /// <paramref name="aplicarAosAtuais"/>, também nas parcelas que ainda não venceram de quem já aderiu (Sprint 48, D21).
    /// </summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="itemId">Item.</param>
    /// <param name="dados">Dados novos.</param>
    /// <param name="autorId">Quem alterou — vai na trilha de auditoria com o antes, o depois e a escolha.</param>
    /// <param name="aplicarAosAtuais">Repactua quem já aderiu. O padrão é não: travar o contrato é a regra.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> AlterarItem(
        Guid planoId,
        Guid itemId,
        DadosDoPacote dados,
        Guid autorId,
        bool aplicarAosAtuais = false,
        CancellationToken ct = default
    );

    /// <summary>
    /// Quantos de quem já aderiu o preço novo alcançaria, e quanto a soma do que devem mudaria — a pergunta "aplicar
    /// também a quem já aderiu?" (D21), sem gravar nada.
    /// </summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="itemId">Item.</param>
    /// <param name="valorEmCentavos">Preço novo.</param>
    Task<Result<Alcance>> SimularPreco(Guid planoId, Guid itemId, long valorEmCentavos, CancellationToken ct = default);

    /// <summary>Quantos formandos o rateio alcançaria hoje, e o total — a conta antes de confirmar (D19).</summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="alvo">Pacotes de quem paga; vazio é todos os que já aderiram.</param>
    /// <param name="valorEmCentavos">Valor por formando.</param>
    Task<Result<Alcance>> SimularRateio(Guid planoId, IReadOnlyList<Guid> alvo, long valorEmCentavos, CancellationToken ct = default);

    /// <summary>Remove um item que nunca gerou parcela.</summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="itemId">Item.</param>
    /// <param name="autorId">Quem removeu — a trilha guarda o retrato do item que deixou de existir.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> RemoverItem(Guid planoId, Guid itemId, Guid autorId, CancellationToken ct = default);

    /// <summary>Encerra um item: deixa de cobrar e cancela as parcelas que ainda não venceram.</summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="itemId">Item.</param>
    /// <param name="autorId">Quem encerrou.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> EncerrarItem(Guid planoId, Guid itemId, Guid autorId, CancellationToken ct = default);

    /// <summary>A grade de um formando e o total da turma, sem gravar nada.</summary>
    /// <param name="formaturaId">Formatura da sessão, para contar os membros.</param>
    /// <param name="planoId">Plano.</param>
    /// <param name="pedido">Itens a simular; ausentes, os gravados.</param>
    Task<Result<SimulacaoDoPlano>> Simular(Guid formaturaId, Guid planoId, SimularPlano pedido, CancellationToken ct = default);

    /// <summary>Coloca o plano em vigor. Só um por turma.</summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="autorId">Quem pôs em vigor.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> Vigorar(Guid planoId, Guid autorId, CancellationToken ct = default);
}
