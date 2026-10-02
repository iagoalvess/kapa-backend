using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Arquivos.Services;
using Backend.Business.Common.Datas;
using Backend.Business.Emails.Services;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Festa.Services;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Loja.Interfaces;
using Backend.Business.Loja.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Loja.Services;

/// <summary>
/// Desfazer uma compra paga da loja (Sprint 38): o cancelamento da Gestão, a devolução e o pedido do
/// comprador.
/// </summary>
/// <remarks>
/// O coração é <see cref="CancelarNaTransacao"/>, e as duas portas — a Gestão e a aprovação do pedido —
/// passam por ele: trava a compra, revoga os convites, devolve o lugar ao item, lança o estorno,
/// leva a compra à lista a devolver, avisa comprador e convidados e audita (decisão 1). A trava da compra é o
/// que faz dois cliques contarem uma vez: o segundo espera, relê e não acha mais o que revogar (decisão 3).
/// </remarks>
/// <param name="compras">Compras, o estoque do item e os pedidos de cancelamento.</param>
/// <param name="convites">Os convites da compra.</param>
/// <param name="receitas">O estorno da receita da venda (P5).</param>
/// <param name="agenda">A festa — o evento do e-mail do convidado.</param>
/// <param name="eventos">Auditoria.</param>
/// <param name="arquivos">O comprovante da devolução.</param>
/// <param name="emails">O aviso ao comprador e à comissão.</param>
/// <param name="emailsDoConvite">O aviso ao convidado.</param>
/// <param name="validator">Forma do cancelamento.</param>
/// <param name="pedidoValidator">Forma do pedido do comprador.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class CancelamentoDaCompra(
    ICompraDeConviteRepository compras,
    IConviteDoEventoRepository convites,
    IOutraReceitaRepository receitas,
    IEventoDaTurmaRepository agenda,
    IEventoRepository eventos,
    IArquivoService arquivos,
    EmailsDaLoja emails,
    EmailsDoConvite emailsDoConvite,
    IValidator<DadosDoCancelamento> validator,
    IValidator<PedidoDoComprador> pedidoValidator,
    IUnitOfWork unitOfWork,
    ILogger<CancelamentoDaCompra> logger
) : ICancelamentoDaCompraService
{
    /// <summary>Categoria dos comprovantes de devolução no módulo de arquivos.</summary>
    public const string CategoriaDoComprovante = "comprovantes-devolucao";

    /// <summary>Comprovante é PDF ou imagem — a mesma lista da despesa, repetida de propósito (são duas decisões).</summary>
    private static readonly HashSet<string> ExtensoesDoComprovante = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
    };

    private static readonly Erro CompraNaoEncontrada = Erro.NaoEncontrado("loja.compra_nao_encontrada", "Compra não encontrada nesta turma.");

    private static readonly Erro CompraNaoPaga = Erro.Conflito(
        "loja.compra_nao_paga",
        "Esta compra ainda não foi paga. Compra pendente não se cancela: ela expira sozinha se o PIX não for pago."
    );

    private static readonly Erro CompraJaCancelada = Erro.Conflito("loja.compra_ja_cancelada", "Estes convites já foram cancelados.");

    private static readonly Erro PedidoNaoEncontrado = Erro.NaoEncontrado(
        "loja.pedido_nao_encontrado",
        "Pedido de cancelamento não encontrado nesta turma."
    );

    private static readonly Erro PedidoJaRespondido = Erro.Conflito("loja.pedido_ja_respondido", "Este pedido de cancelamento já foi respondido.");

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ConviteDaCompra>>> ListarConvites(Guid compraId, CancellationToken ct = default) =>
        await compras.Obter(compraId, ct) is null ? CompraNaoEncontrada : Result.Ok(await compras.ListarConvites(compraId, ct));

    /// <inheritdoc />
    public async Task<Result<CompraCancelada>> Cancelar(Guid compraId, DadosDoCancelamento dados, Guid usuarioId, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<CompraCancelada>(validacao.Erros);

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var cancelada = await CancelarNaTransacao(compraId, dados.ConviteIds, dados.Motivo.Trim(), usuarioId, null, token);
                if (cancelada.Falhou)
                    return cancelada;

                await unitOfWork.SalvarAsync(token);

                return cancelada;
            },
            ct
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// O comprovante é gravado antes da trava, como na despesa paga; se a compra saiu da lista no meio do
    /// caminho, ele é apagado, e a resposta é a da compra.
    /// </remarks>
    public async Task<Result> MarcarDevolvida(Guid compraId, NovoArquivo? comprovante, Guid usuarioId, CancellationToken ct = default)
    {
        if (comprovante is null)
            return Result.Falha(Erro.Validacao("loja.comprovante_obrigatorio", "Anexe o comprovante do PIX de volta."));

        if (await compras.Obter(compraId, ct) is not { } lida)
            return Result.Falha(CompraNaoEncontrada);

        if (lida.Status != StatusDaCompra.ADevolver)
            return Result.Falha(NaoADevolver);

        var arquivo = await arquivos.EnviarComprovante(
            comprovante,
            CategoriaDoComprovante,
            ExtensoesDoComprovante,
            Erro.Validacao("loja.comprovante_invalido", "Envie o comprovante em PDF ou imagem (PNG, JPG ou WebP)."),
            usuarioId,
            ct
        );
        if (arquivo.Falhou)
            return Result.Falha(arquivo.Erros);

        var devolvida = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var compra = await compras.Travar(compraId, token);
                if (compra is not { Status: StatusDaCompra.ADevolver })
                    return Result.Falha(NaoADevolver);

                var aDevolver = compra.ValorADevolverEmCentavos;
                var estorno = compra.Devolver(DateTime.UtcNow, arquivo.Valor!.Value);

                if (estorno > 0)
                    await Estornar(compra, estorno, "devolução do pagamento sem lugar", token);

                await eventos.Auditar(
                    NomesDeAuditoria.CompraDevolvida,
                    usuarioId,
                    new
                    {
                        formaturaId = compra.FormaturaId,
                        compraId,
                        valorDevolvido = aDevolver,
                        comprovanteId = arquivo.Valor,
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);

                return Result.Ok();
            },
            ct
        );

        if (devolvida.Falhou)
            await arquivos.DescartarComprovante(arquivo.Valor, usuarioId, ct);
        else
            logger.LogInformation("Compra {CompraId} marcada devolvida por {UsuarioId}.", compraId, usuarioId);

        return devolvida;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Sob a trava da compra: dois pedidos no mesmo segundo viram um, e o segundo recebe o primeiro (decisão 5).
    /// Pede-se só o convite válido e sem entrada; o índice único parcial segura um aberto por compra.
    /// </remarks>
    public async Task<Result> Pedir(CompraDeConvite compra, PedidoDoComprador dados, CancellationToken ct = default)
    {
        var validacao = pedidoValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha(validacao.Erros);

        if (compra.Status is StatusDaCompra.Pendente or StatusDaCompra.Expirada)
            return Result.Falha(CompraNaoPaga);

        var validos = await convites.ListarDaCompra(compra.Id, ct);
        IReadOnlyList<ConviteDoVinculo> pedidos = dados.ConviteIds is { Count: > 0 } ids
            ? [.. validos.Where(linha => ids.Contains(linha.Convite.Id))]
            : [.. validos.Where(linha => linha.ValidadoEm is null)];

        if (pedidos.FirstOrDefault(linha => linha.ValidadoEm is not null) is { } usado)
            return Result.Falha(JaValidado([usado.Convite.Codigo]));

        if (pedidos.Count == 0)
            return Result.Falha(CompraJaCancelada);

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                await compras.Travar(compra.Id, token);

                if (await compras.ObterUltimoPedido(compra.Id, token) is { Status: StatusDoPedidoDeCancelamento.Aberto })
                    return Result.Ok();

                await compras.AdicionarPedido(
                    new PedidoDeCancelamento(compra.Id, pedidos.Select(linha => linha.Convite.Id), dados.Motivo, DateTime.UtcNow),
                    token
                );
                await emails.PedidoRecebido(compra, pedidos.Count, compra.FormaturaId, token);
                await unitOfWork.SalvarAsync(token);

                logger.LogInformation("Compra {CompraId}: pedido de cancelamento de {Convites} convites.", compra.Id, pedidos.Count);

                return Result.Ok();
            },
            ct
        );
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PedidoNaGestao>>> ListarPedidos(CancellationToken ct = default) =>
        Result.Ok(await compras.ListarPedidosAbertos(ct));

    /// <inheritdoc />
    /// <remarks>
    /// Se um convite pedido entrou na festa depois do pedido, a aprovação falha com o motivo e o pedido continua
    /// aberto — a Gestão recusa, ou desfaz a entrada e aprova (P3).
    /// </remarks>
    public Task<Result<CompraCancelada>> Aprovar(Guid pedidoId, Guid usuarioId, CancellationToken ct = default) =>
        unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var pedido = await compras.TravarPedido(pedidoId, token);
                if (pedido is null)
                    return Result.Falha<CompraCancelada>(PedidoNaoEncontrado);

                if (pedido.Status != StatusDoPedidoDeCancelamento.Aberto)
                    return Result.Falha<CompraCancelada>(PedidoJaRespondido);

                var cancelada = await CancelarNaTransacao(
                    pedido.CompraId,
                    pedido.ConviteIds,
                    pedido.Motivo ?? "pedido do comprador",
                    usuarioId,
                    pedido.Id,
                    token
                );
                if (cancelada.Falhou)
                    return cancelada;

                pedido.Responder(aprovado: true, usuarioId, null, DateTime.UtcNow);
                await unitOfWork.SalvarAsync(token);

                return cancelada;
            },
            ct
        );

    /// <inheritdoc />
    public async Task<Result> Recusar(Guid pedidoId, string motivo, Guid usuarioId, CancellationToken ct = default)
    {
        var validacao = validator.Validar(new DadosDoCancelamento(null, motivo));
        if (validacao.Falhou)
            return Result.Falha(validacao.Erros);

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var pedido = await compras.TravarPedido(pedidoId, token);
                if (pedido is null)
                    return Result.Falha(PedidoNaoEncontrado);

                if (!pedido.Responder(aprovado: false, usuarioId, motivo, DateTime.UtcNow))
                    return Result.Falha(PedidoJaRespondido);

                if (await compras.Obter(pedido.CompraId, token) is { Email: not null } compra)
                    await emails.PedidoRecusado(compra, motivo.Trim(), await emails.Vendedor(compra.FormaturaId, token), token);

                await eventos.Auditar(
                    NomesDeAuditoria.PedidoDeCancelamentoRecusado,
                    usuarioId,
                    new
                    {
                        formaturaId = pedido.FormaturaId,
                        compraId = pedido.CompraId,
                        pedidoId,
                        motivo = motivo.Trim(),
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);

                return Result.Ok();
            },
            ct
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// O convite que já entrou na festa não se revoga (P3) — ele fica, e o log conta; o resto é cancelado como na
    /// Gestão, e a compra sai direto como devolvida, sem comprovante: quem devolveu foi o Mercado Pago.
    /// </remarks>
    public async Task<Result<string>> DevolverPeloMercadoPago(Guid compraId, string motivo, Guid usuarioId, CancellationToken ct = default)
    {
        var compra = await compras.Travar(compraId, ct);
        if (compra is null)
            return CompraNaoEncontrada;

        var semEntrada = (await convites.ListarDaCompra(compraId, ct))
            .Where(linha => linha.ValidadoEm is null)
            .Select(linha => linha.Convite.Id)
            .ToList();

        if (compra.LugaresValendo > 0 && semEntrada.Count > 0)
        {
            var cancelada = await CancelarNaTransacao(compraId, semEntrada, motivo, usuarioId, null, ct);
            if (cancelada.Falhou)
                return Result.Falha<string>(cancelada.Erros);
        }

        if (compra.Status == StatusDaCompra.ADevolver)
        {
            var aDevolver = compra.ValorADevolverEmCentavos;
            var estorno = compra.Devolver(DateTime.UtcNow, null);

            if (estorno > 0)
                await Estornar(compra, estorno, motivo, ct);

            await eventos.Auditar(
                NomesDeAuditoria.CompraDevolvida,
                usuarioId,
                new
                {
                    formaturaId = compra.FormaturaId,
                    compraId,
                    valorDevolvido = aDevolver,
                    motivo,
                },
                ct
            );
        }

        if (compra.LugaresValendo > 0)
            logger.LogWarning(
                "Compra {CompraId} devolvida pelo Mercado Pago com {Lugares} convite(s) já usados na festa.",
                compraId,
                compra.LugaresValendo
            );

        return $"a compra {compraId.ToString("N")[^8..].ToUpperInvariant()} da loja";
    }

    /// <summary>
    /// O cancelamento em si, na transação de quem chama (decisão 1). Não salva: quem chama salva uma vez.
    /// </summary>
    /// <param name="compraId">A compra.</param>
    /// <param name="conviteIds">Os convites; nulo ou vazio é todos os lugares que ainda valem.</param>
    /// <param name="motivo">Já aparado.</param>
    /// <param name="usuarioId">Quem cancela.</param>
    /// <param name="pedidoId">O pedido do comprador que originou, se houve.</param>
    /// <param name="ct">Token de cancelamento.</param>
    private async Task<Result<CompraCancelada>> CancelarNaTransacao(
        Guid compraId,
        IReadOnlyCollection<Guid>? conviteIds,
        string motivo,
        Guid usuarioId,
        Guid? pedidoId,
        CancellationToken ct
    )
    {
        var compra = await compras.Travar(compraId, ct);
        if (compra is null)
            return CompraNaoEncontrada;

        if (compra.Status is StatusDaCompra.Pendente or StatusDaCompra.Expirada)
            return CompraNaoPaga;

        if (compra.LugaresValendo == 0)
            return CompraJaCancelada;

        var todos = conviteIds is not { Count: > 0 };
        var validos = await convites.TravarDaCompra(compraId, ct);
        IReadOnlyList<ConviteDoEvento> alvo = todos ? validos : [.. validos.Where(convite => conviteIds!.Contains(convite.Id))];

        if (!todos && alvo.Count == 0)
            return CompraJaCancelada;

        var comEntrada = await convites.ListarComEntrada([.. alvo.Select(convite => convite.Id)], ct);
        if (comEntrada.Count > 0)
            return JaValidado([.. alvo.Where(convite => comEntrada.Contains(convite.Id)).Select(convite => convite.Codigo)]);

        var agora = DateTime.UtcNow;
        var revogacao = $"compra cancelada: {motivo}";
        foreach (var convite in alvo)
            convite.Revogar(revogacao.Length > 200 ? revogacao[..200] : revogacao, agora);

        var lugares = todos ? compra.LugaresValendo : alvo.Count;

        await compras.DevolverAoItem(compra.ItemDeCobrancaId, lugares, ct);

        var estorno = compra.Cancelar(lugares);
        await Estornar(compra, estorno, lugares == 1 ? "1 convite cancelado" : $"{lugares} convites cancelados", ct);

        var vendedor = await emails.Vendedor(compra.FormaturaId, ct);
        if (compra.Email is not null)
            await emails.Cancelada(compra, lugares, estorno, motivo, vendedor, ct);

        if (alvo.Any(convite => convite.EmailDoConvidado is not null) && await agenda.ObterDoTipo(TipoDeEvento.Festa, ct) is { } festa)
        {
            var porque = $"foi cancelado pela comissão da {ModeloDeEmail.Texto(vendedor.Turma)}";

            foreach (var convite in alvo.Where(convite => convite.EmailDoConvidado is not null))
                await emailsDoConvite.Cancelado(convite.EmailDoConvidado!, EmissaoDeConvites.ParaConvite(festa), porque, ct);
        }

        await eventos.Auditar(
            NomesDeAuditoria.CompraCancelada,
            usuarioId,
            new
            {
                formaturaId = compra.FormaturaId,
                compraId,
                convites = lugares,
                codigos = alvo.Select(convite => convite.Codigo).ToArray(),
                estornoEmCentavos = estorno,
                motivo,
                pedidoId,
            },
            ct
        );

        logger.LogInformation("Compra {CompraId}: {Lugares} lugares cancelados, estorno de {Estorno} centavos.", compraId, lugares, estorno);

        return new CompraCancelada(lugares, estorno);
    }

    /// <summary>Lança o estorno ligado à receita da venda, na data de hoje (P5).</summary>
    private async Task Estornar(CompraDeConvite compra, long valorEmCentavos, string oque, CancellationToken ct)
    {
        var original =
            compra.OutraReceitaId ?? throw new InvalidOperationException($"Compra {compra.Id} paga sem a receita da venda — não há o que estornar.");

        var descricao =
            $"Estorno da loja — compra {compra.Id.ToString("N")[^8..].ToUpperInvariant()}: {oque} ({compra.ConvitesCancelados}/{compra.Quantidade})";

        await receitas.Adicionar(
            OutraReceita.Estorno(original, descricao, valorEmCentavos, DateOnly.FromDateTime(DataUtils.ParaExibicao(DateTime.UtcNow))),
            ct
        );
    }

    private static Erro NaoADevolver => Erro.Conflito("loja.compra_nao_a_devolver", "Esta compra não está na lista a devolver.");

    private static Erro JaValidado(IReadOnlyList<string> codigos) =>
        Erro.Conflito(
            "loja.convite_ja_validado",
            codigos.Count == 1
                ? $"O convite {codigos[0]} já entrou na festa, e convite usado não se cancela. Desfaça a entrada na portaria antes, se for o caso."
                : $"Os convites {string.Join(", ", codigos)} já entraram na festa, e convite usado não se cancela. Desfaça as entradas na portaria antes, se for o caso."
        );
}
