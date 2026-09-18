using Backend.Business.Abstractions;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Festa.Services;

/// <summary>
/// O que a turma está comprando, e quanto falta para pagar por isso.
/// </summary>
/// <remarks>
/// Nenhum número desta tela é digitado duas vezes: o estado de cada item e o custo da festa saem das
/// despesas da Sprint 10 (decisões 2 e 3), e o arrecadado vem do mesmo repositório que alimenta o
/// Caixa. Duas telas discordando sobre quanto a turma arrecadou seria o pior defeito possível aqui.
/// </remarks>
/// <param name="itemRepository">Itens da turma.</param>
/// <param name="despesaRepository">Despesas, para saber se o item está em uso.</param>
/// <param name="documentoRepository">Acervo, para recusar um contrato que a turma não pode abrir.</param>
/// <param name="caixaRepository">O arrecadado da turma — o mesmo número da tela do Caixa.</param>
/// <param name="validator">Forma do item.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ItemDaFestaService(
    IItemDaFestaRepository itemRepository,
    IDespesaRepository despesaRepository,
    IDocumentoRepository documentoRepository,
    ICaixaRepository caixaRepository,
    IValidator<DadosDoItemDaFesta> validator,
    IUnitOfWork unitOfWork,
    ILogger<ItemDaFestaService> logger
) : IItemDaFestaService
{
    /// <summary>
    /// Os seis itens com que toda turma começa (decisão 12).
    /// </summary>
    /// <remarks>
    /// É a lista de <see cref="CategoriaDeDespesa"/> menos Taxas e Outros, que não são coisas que a
    /// turma "vai ter". Decoração fica de fora por ser a que mais turma resolve sozinha.
    /// <para>
    /// ponytail: constante no service, não tabela de catálogo. Catálogo configurável quando a segunda
    /// turma pedir um item que não é categoria de despesa.
    /// </para>
    /// </remarks>
    private static readonly (string Titulo, CategoriaDeDespesa Categoria)[] Sugeridos =
    [
        ("Buffet", CategoriaDeDespesa.Buffet),
        ("Espaço", CategoriaDeDespesa.Espaco),
        ("Fotografia", CategoriaDeDespesa.Fotografia),
        ("Banda", CategoriaDeDespesa.Banda),
        ("Convites", CategoriaDeDespesa.Convites),
        ("Beca", CategoriaDeDespesa.Beca),
    ];

    private static readonly Erro NaoEncontrado = Erro.NaoEncontrado("festa.item_nao_encontrado", "Item da festa não encontrado.");

    /// <inheritdoc />
    /// <remarks>
    /// A materialização dos sugeridos acontece na primeira leitura, e não na ativação da turma, pelos
    /// dois motivos da régua da Sprint 13 (decisão 5 de lá): as turmas que já existem também precisam
    /// deles, e o caminho que ativa a formatura é o webhook do provedor — que não tem formatura na
    /// sessão e, portanto, não pode gravar linha de turma nenhuma.
    /// <para>
    /// ponytail: apagar os seis faz os seis voltarem na próxima abertura, porque "nunca teve item" e
    /// "não tem item agora" são a mesma consulta. O upgrade é uma coluna de semeadura na formatura,
    /// quando a primeira turma reclamar.
    /// </para>
    /// </remarks>
    public async Task<Result<IReadOnlyList<ItemDaFestaResumo>>> Listar(CancellationToken ct = default)
    {
        if (!await itemRepository.ExisteAlgum(ct))
            await Semear(ct);

        return Result.Ok(await itemRepository.Listar(ct));
    }

    /// <inheritdoc />
    public async Task<Result<ItemDaFestaResumo>> ObterPorId(Guid id, CancellationToken ct = default) =>
        await itemRepository.Obter(id, ct) is { } item ? item : NaoEncontrado;

    /// <inheritdoc />
    /// <remarks>
    /// O custo soma o <see cref="ItemDaFestaResumo.CustoEmCentavos"/> de cada item, que já resolve
    /// sozinho "previsto ou contratado" e zera o cancelado. São dezenas de itens por turma: somar na
    /// memória o que a lista da tela já traz custa menos que uma segunda consulta agregada — e
    /// garante que a barra e os cartões nunca discordem.
    /// </remarks>
    public async Task<Result<MetaDaFesta>> ObterMeta(CancellationToken ct = default)
    {
        var itens = await itemRepository.Listar(ct);
        var dePe = itens.Where(item => !item.Cancelado).ToList();

        return new MetaDaFesta(
            dePe.Sum(item => item.CustoEmCentavos),
            dePe.Sum(item => item.PagoEmCentavos),
            await caixaRepository.Arrecadado(ct),
            dePe.Count,
            dePe.Count(item => item.Estado == EstadoDoItem.AContratar),
            dePe.Count(item => item.Estado == EstadoDoItem.Pago)
        );
    }

    /// <inheritdoc />
    public async Task<Result<ItemDaFestaResumo>> Criar(DadosDoItemDaFesta dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ItemDaFestaResumo>(validacao.Erros);

        if (await DocumentoInvalido(dados, ct))
            return DocumentoNaoEncontrado;

        var item = ItemDaFesta.Novo(dados, await itemRepository.UltimaOrdem(ct) + 1);

        await itemRepository.Adicionar([item], ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Item da festa {ItemId} criado.", item.Id);

        return await ObterPorId(item.Id, ct);
    }

    /// <inheritdoc />
    public async Task<Result<ItemDaFestaResumo>> Atualizar(Guid id, DadosDoItemDaFesta dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ItemDaFestaResumo>(validacao.Erros);

        if (await DocumentoInvalido(dados, ct))
            return DocumentoNaoEncontrado;

        return await Alterar(id, item => item.Aplicar(dados), ct);
    }

    /// <inheritdoc />
    public Task<Result<ItemDaFestaResumo>> Cancelar(Guid id, CancellationToken ct = default) => Alterar(id, item => item.Cancelar(), ct);

    /// <inheritdoc />
    public Task<Result<ItemDaFestaResumo>> Reativar(Guid id, CancellationToken ct = default) => Alterar(id, item => item.Reativar(), ct);

    /// <inheritdoc />
    public async Task<Result> Excluir(Guid id, CancellationToken ct = default)
    {
        var item = await itemRepository.ObterParaEdicao(id, ct);
        if (item is null)
            return Result.Falha(NaoEncontrado);

        if (await despesaRepository.ExisteDoItemDaFesta(id, ct))
            return Result.Falha(Erro.Conflito("festa.item_em_uso", "Este item já tem despesa lançada. Cancele-o em vez de excluí-lo."));

        itemRepository.Remover(item);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Item da festa {ItemId} excluído.", id);

        return Result.Ok();
    }

    /// <summary>Carrega, aplica a transição e devolve o item como a lista o mostra.</summary>
    /// <param name="id">Item.</param>
    /// <param name="transicao">O que fazer com ele.</param>
    private async Task<Result<ItemDaFestaResumo>> Alterar(Guid id, Func<ItemDaFesta, Result> transicao, CancellationToken ct)
    {
        var item = await itemRepository.ObterParaEdicao(id, ct);
        if (item is null)
            return NaoEncontrado;

        var resultado = transicao(item);
        if (resultado.Falhou)
            return Result.Falha<ItemDaFestaResumo>(resultado.Erros);

        await unitOfWork.SalvarAsync(ct);

        return await ObterPorId(id, ct);
    }

    /// <summary>Grava os seis sugeridos de uma vez, na ordem em que a turma contrata.</summary>
    private async Task Semear(CancellationToken ct)
    {
        var itens = Sugeridos.Select((sugerido, indice) => ItemDaFesta.Sugerido(sugerido.Titulo, sugerido.Categoria, indice + 1)).ToList();

        await itemRepository.Adicionar(itens, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Itens sugeridos da festa criados para a turma.");
    }

    /// <summary>
    /// Se o documento informado não serve de contrato deste item.
    /// </summary>
    /// <remarks>
    /// A consulta vai com o papel de <see cref="PapelNaFormatura.Formando"/> de propósito: o que ela
    /// responde não é "existe", e sim "a turma inteira consegue abrir" (decisão 7). Uma ata da
    /// comissão volta nula aqui e é recusada — o cartão do item é lido por todo mundo.
    /// </remarks>
    /// <param name="dados">Dados do item.</param>
    private async Task<bool> DocumentoInvalido(DadosDoItemDaFesta dados, CancellationToken ct) =>
        dados.DocumentoId is { } documentoId && await documentoRepository.Obter(documentoId, PapelNaFormatura.Formando, ct) is null;

    private static Erro DocumentoNaoEncontrado =>
        Erro.Validacao(
            "festa.documento_nao_encontrado",
            "Documento não encontrado no acervo, ou visível apenas para a comissão.",
            campo: nameof(DadosDoItemDaFesta.DocumentoId)
        );
}
