using Backend.Business.Abstractions;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Financeiro.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Festa.Services;

/// <summary>
/// As candidatas de um item "a contratar": os orçamentos que a comissão compara.
/// </summary>
/// <remarks>
/// Decisão 16: o mapeamento não é um estado novo — "a contratar" já era ele. O que faltava era as
/// candidatas terem onde morar, em vez de caberem só na prosa do item.
/// <para>
/// A janela é o estado do item, e ela também é lida, nunca gravada: proposta só muda enquanto não há
/// despesa vinculada. Depois dela a escolha já aconteceu.
/// </para>
/// <para>
/// O voto da turma nas propostas (decisão 17) saiu em 07/10/2026: quem contrata é a comissão.
/// </para>
/// </remarks>
/// <param name="propostaRepository">Propostas.</param>
/// <param name="itemRepository">Itens, para achar o dono da proposta.</param>
/// <param name="despesaRepository">Despesas, para saber se o item ainda está "a contratar".</param>
/// <param name="validator">Forma da proposta.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class PropostaService(
    IPropostaRepository propostaRepository,
    IItemDaFestaRepository itemRepository,
    IDespesaRepository despesaRepository,
    IValidator<DadosDaProposta> validator,
    IUnitOfWork unitOfWork,
    ILogger<PropostaService> logger
) : IPropostaService
{
    private static readonly Erro NaoEncontrada = Erro.NaoEncontrado("festa.proposta_nao_encontrada", "Proposta não encontrada.");

    private static readonly Erro ItemNaoEncontrado = Erro.NaoEncontrado("festa.item_nao_encontrado", "Item da festa não encontrado.");

    private static readonly Erro DisputaEncerrada = Erro.Conflito(
        "festa.disputa_encerrada",
        "Este item já foi contratado ou cancelado: a escolha já aconteceu."
    );

    /// <inheritdoc />
    public async Task<Result<PropostaResumo>> Criar(Guid itemId, DadosDaProposta dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PropostaResumo>(validacao.Erros);

        var aberta = await DisputaAberta(itemId, ct);
        if (aberta.Falhou)
            return Result.Falha<PropostaResumo>(aberta.Erros);

        var proposta = PropostaDoItem.Nova(itemId, dados);

        await propostaRepository.Adicionar(proposta, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Proposta {PropostaId} criada para o item {ItemId}.", proposta.Id, itemId);

        return new PropostaResumo(proposta.Id, proposta.Titulo, proposta.ValorEmCentavos, proposta.OQueInclui);
    }

    /// <inheritdoc />
    public async Task<Result<PropostaResumo>> Atualizar(Guid id, DadosDaProposta dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PropostaResumo>(validacao.Erros);

        var proposta = await propostaRepository.ObterParaEdicao(id, ct);
        if (proposta is null)
            return NaoEncontrada;

        var aberta = await DisputaAberta(proposta.ItemDaFestaId, ct);
        if (aberta.Falhou)
            return Result.Falha<PropostaResumo>(aberta.Erros);

        proposta.Aplicar(dados);
        await unitOfWork.SalvarAsync(ct);

        return await Devolver(id, ct);
    }

    /// <inheritdoc />
    public async Task<Result> Excluir(Guid id, CancellationToken ct = default)
    {
        var proposta = await propostaRepository.ObterParaEdicao(id, ct);
        if (proposta is null)
            return Result.Falha(NaoEncontrada);

        var aberta = await DisputaAberta(proposta.ItemDaFestaId, ct);
        if (aberta.Falhou)
            return aberta;

        propostaRepository.Remover(proposta);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Proposta {PropostaId} excluída.", id);

        return Result.Ok();
    }

    /// <summary>
    /// Se o item existe aqui e ainda está "a contratar".
    /// </summary>
    /// <remarks>
    /// O estado não é lido de coluna nenhuma (decisão 2): "tem despesa" é o que encerra a disputa, e
    /// é a mesma consulta que a exclusão do item já usa.
    /// </remarks>
    /// <param name="itemId">Item.</param>
    private async Task<Result> DisputaAberta(Guid itemId, CancellationToken ct)
    {
        var item = await itemRepository.ObterParaEdicao(itemId, ct);
        if (item is null)
            return Result.Falha(ItemNaoEncontrado);

        if (item.Cancelado || await despesaRepository.ExisteDoItemDaFesta(itemId, ct))
            return Result.Falha(DisputaEncerrada);

        return Result.Ok();
    }

    /// <summary>A proposta como a tela a mostra, depois da gravação.</summary>
    /// <param name="id">Proposta.</param>
    private async Task<Result<PropostaResumo>> Devolver(Guid id, CancellationToken ct) =>
        await propostaRepository.Obter(id, ct) is { } resumo ? resumo : NaoEncontrada;
}
