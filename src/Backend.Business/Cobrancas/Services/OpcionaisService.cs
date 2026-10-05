using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Festa.Services;
using Backend.Business.Recebimentos.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Cobrancas.Services;

/// <summary>
/// Os opcionais da turma: o que o formando pode pedir só para ele.
/// </summary>
/// <remarks>
/// Não nasce entidade nova: o item opcional é o mesmo <see cref="ItemDeCobranca"/> do plano, com
/// a marca <c>Opcional</c> (decisão 1). O ganho é o que <b>não</b> muda — a parcela do pedido
/// continua sendo uma parcela igual a todas, e extrato, PIX, baixa, régua e balancete seguem
/// funcionando sem uma linha nova.
/// <para>
/// O vínculo com o item da festa é conferido por consulta direta ao repositório de lá (decisão 11),
/// e nunca chamando <c>IItemDaFestaService</c>: a dependência é só de leitura, como a que o
/// <c>ItemDaFestaService</c> já tem com o acervo.
/// </para>
/// </remarks>
/// <param name="planoRepository">Planos e itens.</param>
/// <param name="pedidoRepository">Pedidos, para saber se o item pode ser excluído.</param>
/// <param name="parcelaRepository">Parcelas, para saber se a grade ainda pode mudar.</param>
/// <param name="itemDaFestaRepository">Itens da festa, para validar o vínculo da decisão 11.</param>
/// <param name="cobrancaService">O encerramento do item, que é o mesmo do plano.</param>
/// <param name="mercadoPago">Se a turma conectou o Mercado Pago — a loja pública exige (Sprint 26).</param>
/// <param name="validator">Forma do item opcional.</param>
/// <param name="eventos">Trilha de auditoria.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class OpcionaisService(
    IPlanoDeCobrancaRepository planoRepository,
    IPedidoRepository pedidoRepository,
    IParcelaRepository parcelaRepository,
    IItemDaFestaRepository itemDaFestaRepository,
    ICobrancaService cobrancaService,
    EmissaoNoMercadoPago mercadoPago,
    IValidator<DadosDoOpcional> validator,
    IEventoRepository eventos,
    IUnitOfWork unitOfWork,
    ILogger<OpcionaisService> logger
) : IOpcionaisService
{
    private static readonly Erro SemPlanoVigente = Erro.Conflito(
        "cobranca.sem_plano_vigente",
        "A turma ainda não tem plano de cobrança em vigor. Os opcionais entram nele."
    );

    private static readonly Erro NaoEncontrado = Erro.NaoEncontrado("cobranca.item_nao_encontrado", "Item opcional não encontrado.");

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Opcional>>> Listar(CancellationToken ct = default)
    {
        var plano = await planoRepository.ObterVigente(ct);
        if (plano is null)
            return Result.Ok<IReadOnlyList<Opcional>>([]);

        var agora = DateTime.UtcNow;
        var hoje = DataUtils.Hoje();

        return Result.Ok<IReadOnlyList<Opcional>>([
            .. plano
                .ItensOpcionais.Where(item => !item.NaLoja && (item.PedidosAteDia is not { } prazo || hoje <= prazo))
                .OrderBy(item => item.CriadoEm)
                .ThenBy(item => item.Id)
                .Select(item => Vitrine(item, agora)),
        ]);
    }

    /// <inheritdoc />
    public async Task<Result<ItemDeCobrancaDetalhe>> Criar(DadosDoOpcional dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ItemDeCobrancaDetalhe>(validacao.Erros);

        var plano = await planoRepository.ObterVigenteParaEdicao(ct);
        if (plano is null)
            return SemPlanoVigente;

        var vinculo = await FestaInvalida(plano, dados.ItemDaFestaId, itemId: null, ct);
        if (vinculo is not null)
            return Result.Falha<ItemDeCobrancaDetalhe>(vinculo);

        if (ItemDeCobranca.PassaDoLimite(ItemDeCobranca.UltimaParcelaDoTeto(dados.Item), dados.UltimoVencimento) is { } tarde)
            return Result.Falha<ItemDeCobrancaDetalhe>(tarde);

        if (await LojaSemMercadoPago(dados, ct) is { } semMercadoPago)
            return semMercadoPago;

        var item = ItemDeCobranca.NovoOpcional(plano.Id, dados);
        plano.Itens.Add(item);

        await planoRepository.AdicionarItem(item, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Item opcional {ItemId} criado no plano {PlanoId}.", item.Id, plano.Id);

        return ItemDeCobrancaDetalhe.De(item, emUso: false);
    }

    /// <inheritdoc />
    public async Task<Result<ItemDeCobrancaDetalhe>> Atualizar(Guid itemId, DadosDoOpcional dados, Guid autorId, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ItemDeCobrancaDetalhe>(validacao.Erros);

        var (plano, item) = await Localizar(itemId, ct);
        if (item is null || plano is null)
            return NaoEncontrado;

        if (item.EncerradoEm is not null)
            return Erro.Conflito("cobranca.item_encerrado", "Este item foi encerrado e não muda mais.");

        var vinculo = await FestaInvalida(plano, dados.ItemDaFestaId, itemId, ct);
        if (vinculo is not null)
            return Result.Falha<ItemDeCobrancaDetalhe>(vinculo);

        if (ItemDeCobranca.PassaDoLimite(ItemDeCobranca.UltimaParcelaDoTeto(dados.Item), dados.UltimoVencimento) is { } tarde)
            return Result.Falha<ItemDeCobrancaDetalhe>(tarde);

        if (item.ModoDeVenda != dados.ModoDeVenda && await LojaSemMercadoPago(dados, ct) is { } semMercadoPago)
            return semMercadoPago;

        var emUso = await parcelaRepository.ExisteDoItem(item.Id, ct);
        if (emUso && item.MudaAGrade(dados.Item))
            return Erro.Conflito(
                "cobranca.item_em_uso",
                "Este item já gerou parcelas: só o preço e a descrição podem mudar. Para mudar o resto, encerre-o e cadastre outro."
            );

        var antes = Retrato(item);

        var aplicar = item.AplicarDadosDoOpcional(dados);
        if (aplicar.Falhou)
            return Result.Falha<ItemDeCobrancaDetalhe>(aplicar.Erros);

        await eventos.Auditar(
            NomesDeAuditoria.ItemAlterado,
            autorId,
            new
            {
                formaturaId = plano.FormaturaId,
                itemId,
                antes,
                depois = Retrato(item),
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        return ItemDeCobrancaDetalhe.De(item, emUso);
    }

    /// <summary>
    /// Recusa abrir a loja sem o Mercado Pago da turma conectado (Sprint 26): sem baixa automática não há
    /// como confirmar o pagamento de quem não tem conta, e a reserva ficaria presa com desconhecido.
    /// </summary>
    /// <param name="dados">O item pretendido.</param>
    private async Task<Erro?> LojaSemMercadoPago(DadosDoOpcional dados, CancellationToken ct) =>
        dados.ModoDeVenda == ModoDeVenda.Publica && await mercadoPago.Credencial(ct) is null
            ? Erro.Conflito(
                "loja.sem_mercado_pago",
                "A loja pública precisa do Mercado Pago da turma conectado: é ele que confirma o pagamento sozinho. Conecte na tela da turma."
            )
            : null;

    /// <inheritdoc />
    /// <remarks>
    /// Delega ao encerramento do plano: é o mesmo item e a mesma consequência — para de cobrar, e o
    /// que ainda não venceu é cancelado. O que muda nos opcionais é só o que a tela oferece.
    /// </remarks>
    public async Task<Result<ItemDeCobrancaDetalhe>> Encerrar(Guid itemId, Guid autorId, CancellationToken ct = default)
    {
        var (plano, item) = await Localizar(itemId, ct);
        if (item is null || plano is null)
            return NaoEncontrado;

        var encerrado = await cobrancaService.EncerrarItem(plano.Id, itemId, autorId, ct);
        if (encerrado.Falhou)
            return Result.Falha<ItemDeCobrancaDetalhe>(encerrado.Erros);

        return encerrado.Valor.Itens.First(i => i.Id == itemId);
    }

    /// <inheritdoc />
    public async Task<Result> Excluir(Guid itemId, Guid autorId, CancellationToken ct = default)
    {
        var (plano, item) = await Localizar(itemId, ct);
        if (item is null || plano is null)
            return Result.Falha(NaoEncontrado);

        if (await pedidoRepository.ExisteDoItem(item.Id, ct))
            return Result.Falha(
                Erro.Conflito(
                    "cobranca.item_com_pedido",
                    "Este item já foi pedido por alguém e não pode ser excluído. Encerre-o: ele para de aceitar pedidos e o que já foi pedido fica."
                )
            );

        return await cobrancaService.RemoverItem(plano.Id, itemId, autorId, ct);
    }

    /// <summary>O item opcional e o plano dele, rastreados para alteração.</summary>
    /// <param name="itemId">Item.</param>
    private async Task<(PlanoDeCobranca? Plano, ItemDeCobranca? Item)> Localizar(Guid itemId, CancellationToken ct)
    {
        var plano = await planoRepository.ObterVigenteParaEdicao(ct);

        return (plano, plano?.Itens.Find(i => i.Id == itemId && i.Opcional));
    }

    /// <summary>
    /// Se o item da festa informado não serve de origem para este item opcional.
    /// </summary>
    /// <remarks>
    /// Precisa existir na turma, ser rateado <see cref="TipoDeRateio.PorFormando"/> — mensalidade da
    /// turma não é "o que só alguns compram" — e não estar cancelado. O índice único parcial do
    /// banco é a segunda barreira contra dois opcionais apontando para a mesma foto.
    /// </remarks>
    /// <param name="plano">Plano vigente, já com os itens.</param>
    /// <param name="itemDaFestaId">Item da festa pretendido.</param>
    /// <param name="itemId">O item opcional em alteração, que não conflita consigo mesmo.</param>
    private async Task<Erro?> FestaInvalida(PlanoDeCobranca plano, Guid? itemDaFestaId, Guid? itemId, CancellationToken ct)
    {
        if (itemDaFestaId is not { } festaId)
            return null;

        var daFesta = await itemDaFestaRepository.Obter(festaId, ct);

        if (daFesta is null || daFesta.Cancelado || daFesta.Rateio != TipoDeRateio.PorFormando)
            return Erro.Validacao(
                "cobranca.item_da_festa_invalido",
                "Escolha um item da festa que exista, esteja de pé e seja rateado por formando.",
                campo: "item_da_festa_id"
            );

        var jaLigado = plano.Itens.Find(i => i.ItemDaFestaId == festaId && i.Id != itemId && i.EncerradoEm is null);

        return jaLigado is null
            ? null
            : Erro.Validacao("cobranca.item_da_festa_invalido", "Este item da festa já está ligado a um opcional.", campo: "item_da_festa_id");
    }

    /// <summary>O item na vitrine do formando.</summary>
    /// <param name="item">Item opcional.</param>
    /// <param name="agora">Instante de referência, em UTC.</param>
    private static Opcional Vitrine(ItemDeCobranca item, DateTime agora) =>
        new(
            item.Id,
            item.Tipo,
            item.Descricao,
            item.ValorEmCentavos,
            item.NumeroDeParcelas,
            item.DiaDeVencimento,
            item.PrimeiroMes,
            item.LimitePorFormando,
            item.PedidosAteDia,
            item.Estoque,
            item.Reservados,
            item.Disponivel,
            item.AberturaDeVendas,
            item.ItemDaFestaId,
            item.AbertoAPedido(agora),
            item.UltimoVencimento
        );

    /// <summary>O que muda preço ou condição do que se vende — o corpo do <c>antes</c> e do <c>depois</c>.</summary>
    /// <param name="item">Item opcional.</param>
    private static object Retrato(ItemDeCobranca item) =>
        new
        {
            item.Descricao,
            item.Tipo,
            valorUnitarioEmCentavos = item.ValorEmCentavos,
            item.NumeroDeParcelas,
            item.DiaDeVencimento,
            item.Estoque,
            item.LimitePorFormando,
            item.PedidosAteDia,
            item.AberturaDeVendas,
            item.ItemDaFestaId,
            item.ModoDeVenda,
            item.PrecoPublicoEmCentavos,
            item.UltimoVencimento,
        };
}
