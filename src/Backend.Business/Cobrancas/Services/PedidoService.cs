using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Festa.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Cobrancas.Services;

/// <summary>
/// O pedido do formando: pedir, ajustar a quantidade e cancelar.
/// </summary>
/// <remarks>
/// Toda escrita daqui acontece dentro de <c>EmTransacaoAsync</c>, e a primeira coisa que ela faz é
/// travar a linha do item (decisão 10). A razão é que o delta a reservar depende da quantidade que
/// o pedido já tinha: ler e escrever precisam acontecer sob a mesma trava, senão dois pedidos
/// simultâneos leem o mesmo "restam 1" e vendem dois.
/// <para>
/// A trava dá a mensagem certa; quem <b>garante</b> é o <c>CHECK</c> do banco. Migration futura,
/// bug de cancelamento e <c>UPDATE</c> na mão às duas da manhã abortam a transação em vez de vender
/// o convite 81.
/// </para>
/// </remarks>
/// <param name="pedidoRepository">Pedidos e a trava do item.</param>
/// <param name="parcelaRepository">Parcelas geradas pelo pedido.</param>
/// <param name="perfilRepository">Vínculo de quem pede.</param>
/// <param name="adesaoRepository">Adesão: sem ela não se passa a dever (decisão 4).</param>
/// <param name="validator">Forma do pedido.</param>
/// <param name="eventos">Trilha de auditoria.</param>
/// <param name="emissao">Os convites da festa que o pedido de convite extra paga (Sprint 21).</param>
/// <param name="donosDeMesa">As mesas que o pedido de mesa dá direito a ter (Sprint 27).</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class PedidoService(
    IPedidoRepository pedidoRepository,
    IParcelaRepository parcelaRepository,
    IPerfilRepository perfilRepository,
    IAdesaoRepository adesaoRepository,
    IValidator<DadosDoPedido> validator,
    IEventoRepository eventos,
    EmissaoDeConvites emissao,
    DonosDeMesa donosDeMesa,
    IUnitOfWork unitOfWork,
    ILogger<PedidoService> logger
) : IPedidoService, IQuitacaoDePedidos
{
    private static readonly Erro NaoEncontrado = Erro.NaoEncontrado("cobranca.pedido_nao_encontrado", "Pedido não encontrado.");

    private static readonly Erro SemVinculo = Erro.NaoEncontrado("formatura.vinculo_nao_encontrado", "Você não é membro ativo desta turma.");

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PedidoResumo>>> ListarMeus(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        if (await perfilRepository.ObterTitular(formaturaId, usuarioId, ct) is not { } membro)
            return Result.Falha<IReadOnlyList<PedidoResumo>>(SemVinculo);

        return Result.Ok(await pedidoRepository.ListarDoVinculo(membro.VinculoId, ct));
    }

    /// <inheritdoc />
    public async Task<Result<PaginaDe<PedidoResumo>>> Listar(PaginacaoRequest paginacao, FiltroDePedidos filtro, CancellationToken ct = default) =>
        Result.Ok(await pedidoRepository.Listar(paginacao.Normalizar(), filtro, ct));

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ResumoDoItemPedido>>> Resumir(CancellationToken ct = default) =>
        Result.Ok(await pedidoRepository.Resumir(ct));

    /// <inheritdoc />
    public async Task<Result<PedidoResumo>> Pedir(Guid formaturaId, Guid usuarioId, DadosDoPedido dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PedidoResumo>(validacao.Erros);

        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        if (membro is null)
            return SemVinculo;

        if (!await adesaoRepository.JaAderiuAlgumaVez(membro.VinculoId, ct))
            return Erro.Conflito("cobranca.pedido_sem_adesao", "Aceite o termo da turma antes de pedir: é ele que cria a sua conta de cobrança.");

        return await unitOfWork.EmTransacaoAsync(
            async token => await Escrever(membro.VinculoId, dados.ItemDeCobrancaId, dados.Quantidade, dados.Parcelas ?? 1, token),
            ct
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// O pedido de outro responde 404, nunca 403: distinguir os casos transformaria a rota num
    /// verificador de "quem pediu o quê" para quem não pode saber.
    /// </remarks>
    public async Task<Result<PedidoResumo>> Ajustar(Guid pedidoId, Guid formaturaId, Guid usuarioId, int quantidade, CancellationToken ct = default)
    {
        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        if (membro is null)
            return SemVinculo;

        var pedido = await pedidoRepository.Obter(pedidoId, ct);

        if (pedido is null || pedido.UsuarioId != usuarioId)
            return NaoEncontrado;

        var dados = new DadosDoPedido(pedido.ItemDeCobrancaId, quantidade);

        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PedidoResumo>(validacao.Erros);

        return await unitOfWork.EmTransacaoAsync(
            async token => await Escrever(membro.VinculoId, pedido.ItemDeCobrancaId, quantidade, parcelas: null, token),
            ct
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// O crédito tem teto no que o formando já pagou naquele pedido: sem ele, um valor digitado a mais
    /// virava parcela negativa enorme, e as somas do caixa e dos relatórios estouravam.
    /// <para>
    /// Com parcela paga (P9), o pedido encolhe para o que o dinheiro já pago cobre, e só o excedente
    /// volta ao estoque. Com crédito, a tesouraria está devolvendo o valor — aí o pedido inteiro cai.
    /// </para>
    /// </remarks>
    public async Task<Result<PedidoResumo>> Cancelar(
        Guid pedidoId,
        Guid formaturaId,
        Guid usuarioId,
        CancelamentoDePedido dados,
        CancellationToken ct = default
    )
    {
        if (dados.CreditoEmCentavos < 0)
            return Erro.Validacao("cobranca.credito_invalido", "O crédito não pode ser negativo.", campo: "credito_em_centavos");

        var membro = await perfilRepository.ObterTitular(formaturaId, usuarioId, ct);
        var daTesouraria = membro is not null && PapelNaFormatura.Tesouraria.Contains(membro.Papel);

        if (dados.CreditoEmCentavos > 0 && !daTesouraria)
            return Erro.Proibido("cobranca.credito_da_tesouraria", "Só a tesouraria lança crédito de um pedido já pago.");

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var pedido = await pedidoRepository.ObterParaEdicao(pedidoId, token);
                if (pedido is null || (!daTesouraria && pedido.VinculoId != membro?.VinculoId))
                    return Result.Falha<PedidoResumo>(NaoEncontrado);

                if (!pedido.Confirmado)
                    return await Resumir(pedido.Id, token);

                var item = await pedidoRepository.TravarItem(pedido.ItemDeCobrancaId, token);
                if (item is null)
                    return Result.Falha<PedidoResumo>(NaoEncontrado);

                var parcelas = await pedidoRepository.ListarParcelasParaEdicao(pedido, token);
                var pago = parcelas.Sum(parcela => parcela.ValorPagoEmCentavos ?? 0);

                if (dados.CreditoEmCentavos > pago)
                    return Result.Falha<PedidoResumo>(
                        Erro.Validacao(
                            "cobranca.credito_acima_do_pago",
                            "O crédito não pode passar do que o formando já pagou neste pedido.",
                            campo: "credito_em_centavos"
                        )
                    );

                if (pago > 0 && !daTesouraria)
                    return Result.Falha<PedidoResumo>(
                        Erro.Conflito(
                            "cobranca.pedido_com_parcela_paga",
                            "Este pedido já tem parcela paga. Fale com a tesouraria: o cancelamento agora é dela."
                        )
                    );

                var mantidas =
                    dados.CreditoEmCentavos > 0 || item.ValorEmCentavos <= 0 ? 0 : (int)Math.Min(pedido.Quantidade, pago / item.ValorEmCentavos);

                var hoje = DataUtils.Hoje();
                var devolvidas = pedido.Cancelar(mantidas);

                var reserva = item.Reservar(-devolvidas, pedido.Quantidade, DateTime.UtcNow);
                if (reserva.Falhou)
                    return Result.Falha<PedidoResumo>(reserva.Erros);

                var canceladas = parcelas.Count(parcela => parcela.Cancelar(hoje, incluirVencidas: true));

                await emissao.RevogarDoPedido(pedido.Id, pedido.Confirmado ? pedido.Quantidade : 0, EmissaoDeConvites.MotivoDoCancelamento, token);

                if (dados.CreditoEmCentavos > 0)
                    await Creditar(pedido, parcelas, dados.CreditoEmCentavos, hoje, token);

                await eventos.Auditar(
                    NomesDeAuditoria.PedidoCancelado,
                    usuarioId,
                    new
                    {
                        formaturaId = pedido.FormaturaId,
                        pedidoId = pedido.Id,
                        itemId = item.Id,
                        unidadesMantidas = mantidas,
                        unidadesDevolvidas = devolvidas,
                        parcelasCanceladas = canceladas,
                        pagoEmCentavos = pago,
                        creditoEmCentavos = dados.CreditoEmCentavos,
                        pelaTesouraria = daTesouraria,
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);
                await SoltarMesas(item, pedido.VinculoId, token);

                logger.LogInformation(
                    "Pedido {PedidoId} cancelado: {Devolvidas} unidades ao estoque, {Canceladas} parcelas canceladas.",
                    pedido.Id,
                    devolvidas,
                    canceladas
                );

                return await Resumir(pedido.Id, token);
            },
            ct
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Quitado é nenhuma parcela do pedido em aberto e ao menos uma paga. As parcelas vêm do rastreador:
    /// num lote que paga duas parcelas do mesmo pedido, a primeira já está marcada em memória quando a
    /// segunda chega, e o banco ainda não sabe de nenhuma das duas.
    /// <para>
    /// Festa incompleta na agenda não derruba a baixa: o dinheiro entrou, e o convite espera — a Gestão
    /// emite os pendentes depois de completar a agenda (P6).
    /// </para>
    /// </remarks>
    public async Task AposBaixa(Parcela parcela, CancellationToken ct = default)
    {
        if (await PedidoDeConvite(parcela, ct) is not { } pedido)
            return;

        var parcelas = await pedidoRepository.ListarParcelasParaEdicao(pedido, ct);
        var quitado =
            parcelas.Any(p => p.Status == StatusDaParcela.Paga)
            && parcelas.All(p => p.Status is StatusDaParcela.Paga or StatusDaParcela.Cancelada or StatusDaParcela.Renegociada);

        if (!quitado)
            return;

        var emitidos = await emissao.EmitirDoPedido(pedido, ct);
        if (emitidos.Falhou)
            logger.LogWarning("Pedido {PedidoId} quitado sem convite: {Codigo}.", pedido.Id, emitidos.PrimeiroErro.Codigo);
    }

    /// <inheritdoc />
    public async Task AposEstorno(Parcela parcela, CancellationToken ct = default)
    {
        if (await PedidoDeConvite(parcela, ct) is { } pedido)
            await emissao.RevogarDoPedido(pedido.Id, 0, EmissaoDeConvites.MotivoDoEstorno, ct);
    }

    /// <summary>
    /// Solta as mesas que o formando tem além do que o pedido de mesa ainda confirma (decisão 6 da Sprint 27).
    /// </summary>
    /// <remarks>Depois do <c>SalvarAsync</c> do pedido: a conta do direito lê o pedido do banco.</remarks>
    /// <param name="item">Item do pedido que diminuiu ou caiu.</param>
    /// <param name="vinculoId">Formando.</param>
    private async Task SoltarMesas(ItemDeCobranca item, Guid vinculoId, CancellationToken ct)
    {
        if (item.Tipo == TipoDeCobranca.Mesa && await donosDeMesa.SoltarAlemDoPedido(vinculoId, ct) > 0)
            await unitOfWork.SalvarAsync(ct);
    }

    /// <summary>O pedido confirmado de convite extra a que a parcela pertence; nulo para qualquer outra parcela.</summary>
    /// <param name="parcela">Parcela do plano ou de um pedido.</param>
    private async Task<Pedido?> PedidoDeConvite(Parcela parcela, CancellationToken ct) =>
        await pedidoRepository.ObterParaEdicao(parcela.VinculoId, parcela.ItemDeCobrancaId, ct) is { Confirmado: true } pedido
        && await pedidoRepository.Obter(pedido.Id, ct) is { Tipo: TipoDeCobranca.ConviteExtra }
            ? pedido
            : null;

    /// <summary>
    /// Cria ou ajusta o pedido sob a trava do item, e move as parcelas junto.
    /// </summary>
    /// <remarks>
    /// Quantidade sempre <b>absoluta</b> (decisão 10): repetir a mesma chamada reserva delta zero e
    /// não gera parcela nenhuma. Aumentar acrescenta só a grade do que faltava, continuando a
    /// numeração; diminuir cancela as parcelas do pedido e grava a grade da quantidade nova — e só
    /// acontece enquanto nada foi pago.
    /// <para>
    /// O número de parcelas do item opcional é teto, não regra (22/09): foto, kit e convite são
    /// compras de cada um, e parcelar é conveniência de quem compra.
    /// </para>
    /// </remarks>
    /// <param name="vinculoId">Quem pede.</param>
    /// <param name="itemId">Item opcional.</param>
    /// <param name="quantidade">Quantidade final.</param>
    /// <param name="parcelas">
    /// Em quantas vezes, no pedido novo (ou que volta de um cancelamento); nulo vem do <c>PUT</c>, que
    /// só muda a quantidade. O pedido de pé mantém a divisão que tem, e o bloco do aumento sai com ela.
    /// </param>
    private async Task<Result<PedidoResumo>> Escrever(Guid vinculoId, Guid itemId, int quantidade, int? parcelas, CancellationToken ct)
    {
        var item = await pedidoRepository.TravarItem(itemId, ct);
        if (item is null)
            return Result.Falha<PedidoResumo>(Erro.NaoEncontrado("cobranca.item_nao_encontrado", "Item opcional não encontrado."));

        if (!item.Opcional)
            return Result.Falha<PedidoResumo>(
                Erro.Validacao("cobranca.item_nao_e_opcional", "Este item é do plano da turma e não se pede — ele já está no seu extrato.")
            );

        if (parcelas > item.NumeroDeParcelas)
            return Result.Falha<PedidoResumo>(
                Erro.Validacao("cobranca.parcelas_acima_do_teto", $"Este item pode ser pago em até {item.NumeroDeParcelas}×.", campo: "parcelas")
            );

        var pedido = await pedidoRepository.ObterParaEdicao(vinculoId, itemId, ct);
        var atual = pedido is { Confirmado: true } ? pedido.Quantidade : 0;
        var delta = quantidade - atual;
        var hoje = DataUtils.Hoje();

        IReadOnlyList<Parcela> aCancelar = delta < 0 && pedido is not null ? await pedidoRepository.ListarParcelasParaEdicao(pedido, ct) : [];

        if (aCancelar.Any(parcela => parcela.ValorPagoEmCentavos > 0))
            return Result.Falha<PedidoResumo>(
                Erro.Conflito("cobranca.pedido_com_parcela_paga", "Este pedido já tem parcela paga e não pode diminuir. Fale com a tesouraria.")
            );

        var reserva = item.Reservar(delta, quantidade, DateTime.UtcNow);
        if (reserva.Falhou)
            return Result.Falha<PedidoResumo>(reserva.Erros);

        if (pedido is null)
        {
            pedido = Pedido.Novo(vinculoId, itemId, quantidade, parcelas ?? 1);
            await pedidoRepository.Adicionar(pedido, ct);
        }
        else
        {
            pedido.Ajustar(quantidade, parcelas);
        }

        foreach (var parcela in aCancelar)
            parcela.Cancelar(hoje, incluirVencidas: true);

        if (delta < 0)
            await emissao.RevogarDoPedido(pedido.Id, quantidade, EmissaoDeConvites.MotivoDoCancelamento, ct);

        var unidadesAGerar = delta < 0 ? quantidade : delta;

        if (unidadesAGerar > 0)
            await GerarParcelas(pedido, item, unidadesAGerar, hoje, ct);

        await unitOfWork.SalvarAsync(ct);

        if (delta < 0)
            await SoltarMesas(item, vinculoId, ct);

        logger.LogInformation("Pedido {PedidoId} do item {ItemId}: quantidade {Quantidade} (delta {Delta}).", pedido.Id, itemId, quantidade, delta);

        return await Resumir(pedido.Id, ct);
    }

    /// <summary>
    /// Grava a grade das unidades pedidas, continuando a numeração do item para aquele vínculo.
    /// </summary>
    /// <remarks>
    /// O preço do item opcional é <b>unitário</b> (decisão 2): é aqui que ele vira total. "Convite
    /// extra, R$ 180", pedido em 2× com quantidade 3, são duas parcelas de R$ 270, e o centavo que
    /// sobra fica na primeira, como em todo o resto do sistema. A divisão é a do pedido, não a do item.
    /// <para>
    /// O primeiro vencimento é o maior entre o do item e o próximo dia de vencimento a partir de
    /// hoje: parcela de pedido nunca nasce vencida, com multa e juros de um atraso que a pessoa não
    /// teve como cometer.
    /// </para>
    /// </remarks>
    /// <param name="pedido">Pedido dono.</param>
    /// <param name="item">Item opcional, já travado.</param>
    /// <param name="unidades">Quantas unidades esta grade cobre.</param>
    /// <param name="hoje">Dia do pedido.</param>
    private async Task GerarParcelas(Pedido pedido, ItemDeCobranca item, int unidades, DateOnly hoje, CancellationToken ct)
    {
        var dados = item.ParaDados() with
        {
            ValorEmCentavos = item.ValorEmCentavos * unidades,
            NumeroDeParcelas = pedido.Parcelas,
            PrimeiroMes = PrimeiroMes(item, hoje),
        };
        var ultimo = (await parcelaRepository.ListarNumerosGerados(pedido.VinculoId, item.Id, ct)).DefaultIfEmpty(0).Max();

        await parcelaRepository.Adicionar(
            [
                .. GradeDeParcelas
                    .Calcular(dados)
                    .Select((prevista, posicao) => Parcela.Nova(pedido.VinculoId, item.Id, prevista with { Numero = ultimo + posicao + 1 })),
            ],
            ct
        );
    }

    /// <summary>O mês do primeiro vencimento do pedido: o do item, ou o próximo que ainda acontece.</summary>
    /// <param name="item">Item opcional.</param>
    /// <param name="hoje">Dia do pedido.</param>
    private static DateOnly PrimeiroMes(ItemDeCobranca item, DateOnly hoje)
    {
        var proximo =
            GradeDeParcelas.Vencimento(hoje, item.DiaDeVencimento) >= hoje
                ? GradeDeParcelas.PrimeiroDoMes(hoje)
                : GradeDeParcelas.PrimeiroDoMes(hoje).AddMonths(1);

        return item.PrimeiroMes > proximo ? item.PrimeiroMes : proximo;
    }

    /// <summary>
    /// Lança o crédito do cancelamento como parcela negativa do próprio pedido (P5).
    /// </summary>
    /// <remarks>
    /// Parcela negativa, e não item novo no plano: item de plano alcançaria a turma inteira, que é o
    /// buraco que esta sprint existe para fechar. O valor negativo já funciona ponta a ponta — a
    /// grade divide com sinal, o valor do dia trata como abatimento —, então não há código de
    /// devolução. O dinheiro de volta é uma despesa no caixa, lançada quando o PIX acontece.
    /// </remarks>
    /// <param name="pedido">Pedido cancelado.</param>
    /// <param name="parcelas">Parcelas dele, já carregadas.</param>
    /// <param name="creditoEmCentavos">Quanto devolver.</param>
    /// <param name="hoje">Dia do cancelamento.</param>
    private async Task Creditar(Pedido pedido, IReadOnlyList<Parcela> parcelas, long creditoEmCentavos, DateOnly hoje, CancellationToken ct)
    {
        var numero = parcelas.Count == 0 ? 1 : parcelas.Max(parcela => parcela.Numero) + 1;

        await parcelaRepository.Adicionar(
            [Parcela.Nova(pedido.VinculoId, pedido.ItemDeCobrancaId, new ParcelaPrevista(numero, hoje, -creditoEmCentavos))],
            ct
        );
    }

    /// <summary>O pedido como as telas o mostram, relido depois da escrita.</summary>
    /// <param name="pedidoId">Pedido.</param>
    private async Task<Result<PedidoResumo>> Resumir(Guid pedidoId, CancellationToken ct) =>
        await pedidoRepository.Obter(pedidoId, ct) is { } pedido ? pedido : Result.Falha<PedidoResumo>(NaoEncontrado);
}
