using Backend.Business.Abstractions;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Formandos.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Festa.Services;

/// <summary>
/// As candidatas de um item "a contratar", e em qual delas a turma votou.
/// </summary>
/// <remarks>
/// Decisão 16: o mapeamento não é um estado novo — "a contratar" já era ele. O que faltava era as
/// candidatas terem onde morar, em vez de caberem só na prosa do item.
/// <para>
/// Decisão 17: um voto por formando por item. Quem garante a unicidade é o índice do banco, não este
/// service: ele lê o voto que existe e o atualiza, e dois cliques simultâneos em propostas
/// diferentes esbarram na constraint em vez de virarem dois votos.
/// </para>
/// <para>
/// A janela é o estado do item, e ela também é lida, nunca gravada: proposta e voto só entram
/// enquanto não há despesa vinculada. Depois dela a escolha já aconteceu — reabrir a votação sobre
/// um contrato assinado é discussão de assembleia, e não de tela.
/// </para>
/// </remarks>
/// <param name="propostaRepository">Propostas e votos.</param>
/// <param name="itemRepository">Itens, para achar o dono da proposta.</param>
/// <param name="despesaRepository">Despesas, para saber se a disputa ainda está aberta.</param>
/// <param name="perfilRepository">Vínculo de quem vota.</param>
/// <param name="validator">Forma da proposta.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class PropostaService(
    IPropostaRepository propostaRepository,
    IItemDaFestaRepository itemRepository,
    IDespesaRepository despesaRepository,
    IPerfilRepository perfilRepository,
    IValidator<DadosDaProposta> validator,
    IUnitOfWork unitOfWork,
    ILogger<PropostaService> logger
) : IPropostaService
{
    private static readonly Erro NaoEncontrada = Erro.NaoEncontrado("festa.proposta_nao_encontrada", "Proposta não encontrada.");

    private static readonly Erro ItemNaoEncontrado = Erro.NaoEncontrado("festa.item_nao_encontrado", "Item da festa não encontrado.");

    private static readonly Erro DisputaEncerrada = Erro.Conflito(
        "festa.disputa_encerrada",
        "Este item já foi contratado ou cancelado: a escolha da turma já aconteceu."
    );

    /// <inheritdoc />
    /// <remarks>
    /// A resposta é montada aqui, e não relida do banco: proposta recém-criada tem zero voto, e isso
    /// é fato — reler para descobrir um zero seria uma consulta a mais em cada "Nova proposta".
    /// </remarks>
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

        return new PropostaResumo(proposta.Id, proposta.Titulo, proposta.ValorEmCentavos, proposta.OQueInclui, 0, false);
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

    /// <inheritdoc />
    /// <remarks>
    /// Votar de novo na mesma proposta é o mesmo estado, e não um erro: a tela pode repetir o clique,
    /// e devolver 409 para "você já votou nesta" faria a interface ter de tratar um caso que não
    /// significa nada.
    /// </remarks>
    public async Task<Result> Votar(Guid propostaId, Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var proposta = await propostaRepository.ObterParaEdicao(propostaId, ct);
        if (proposta is null)
            return Result.Falha(NaoEncontrada);

        var aberta = await DisputaAberta(proposta.ItemDaFestaId, ct);
        if (aberta.Falhou)
            return aberta;

        var vinculo = await VinculoDe(formaturaId, usuarioId, ct);
        if (vinculo.Falhou)
            return vinculo;

        if (await propostaRepository.ObterVoto(vinculo.Valor, proposta.ItemDaFestaId, ct) is { } voto)
        {
            var troca = voto.Trocar(proposta);
            if (troca.Falhou)
                return troca;
        }
        else
        {
            await propostaRepository.AdicionarVoto(VotoNaProposta.Novo(vinculo.Valor, proposta), ct);
        }

        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    /// <remarks>Sem voto, não há o que tirar — e responder 204 de novo é a resposta honesta.</remarks>
    public async Task<Result> Desvotar(Guid itemId, Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var aberta = await DisputaAberta(itemId, ct);
        if (aberta.Falhou)
            return aberta;

        var vinculo = await VinculoDe(formaturaId, usuarioId, ct);
        if (vinculo.Falhou)
            return vinculo;

        if (await propostaRepository.ObterVoto(vinculo.Valor, itemId, ct) is { } voto)
        {
            propostaRepository.RemoverVoto(voto);
            await unitOfWork.SalvarAsync(ct);
        }

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

    /// <summary>O vínculo de quem vota nesta turma.</summary>
    /// <param name="formaturaId">Turma da sessão.</param>
    /// <param name="usuarioId">Quem vota.</param>
    private async Task<Result<Guid>> VinculoDe(Guid formaturaId, Guid usuarioId, CancellationToken ct) =>
        await perfilRepository.ObterTitular(formaturaId, usuarioId, ct) is { } membro
            ? membro.VinculoId
            : Erro.NaoEncontrado("membro.nao_encontrado", "Membro não encontrado nesta formatura.");

    /// <summary>A proposta como a tela a mostra, depois da gravação.</summary>
    /// <param name="id">Proposta.</param>
    private async Task<Result<PropostaResumo>> Devolver(Guid id, CancellationToken ct) =>
        await propostaRepository.Obter(id, null, ct) is { } resumo ? resumo : NaoEncontrada;
}
