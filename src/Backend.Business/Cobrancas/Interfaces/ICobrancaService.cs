using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// O plano financeiro da turma: a tesouraria monta e simula, o Presidente põe em vigor, a gestão
/// consulta as parcelas.
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

    /// <summary>Inclui um item no plano.</summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="dados">Item.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> AdicionarItem(Guid planoId, DadosDoItem dados, CancellationToken ct = default);

    /// <summary>Altera um item. Com parcela gerada, só o valor muda — e só nas que ainda não venceram.</summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="itemId">Item.</param>
    /// <param name="dados">Dados novos.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> AlterarItem(Guid planoId, Guid itemId, DadosDoItem dados, CancellationToken ct = default);

    /// <summary>Remove um item que nunca gerou parcela.</summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="itemId">Item.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> RemoverItem(Guid planoId, Guid itemId, CancellationToken ct = default);

    /// <summary>Encerra um item: deixa de cobrar e cancela as parcelas que ainda não venceram.</summary>
    /// <param name="planoId">Plano.</param>
    /// <param name="itemId">Item.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> EncerrarItem(Guid planoId, Guid itemId, CancellationToken ct = default);

    /// <summary>A grade de um formando e o total da turma, sem gravar nada.</summary>
    /// <param name="formaturaId">Formatura da sessão, para contar os membros.</param>
    /// <param name="planoId">Plano.</param>
    /// <param name="pedido">Itens a simular; ausentes, os gravados.</param>
    Task<Result<SimulacaoDoPlano>> Simular(Guid formaturaId, Guid planoId, SimularPlano pedido, CancellationToken ct = default);

    /// <summary>Coloca o plano em vigor. Só um por turma.</summary>
    /// <param name="planoId">Plano.</param>
    Task<Result<PlanoDeCobrancaDetalhe>> Vigorar(Guid planoId, CancellationToken ct = default);

    /// <summary>Uma página das parcelas da turma.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Formando, situação e período.</param>
    Task<Result<PaginaDe<ParcelaResumo>>> ListarParcelas(PaginacaoRequest paginacao, FiltroDeParcelas filtro, CancellationToken ct = default);

    /// <summary>Quantas parcelas e quanto somam por situação — a faixa da tela Parcelas.</summary>
    /// <param name="filtro">Formando, período e busca; a situação é ignorada.</param>
    Task<Result<ResumoDeParcelas>> ResumirParcelas(FiltroDeParcelas filtro, CancellationToken ct = default);
}
