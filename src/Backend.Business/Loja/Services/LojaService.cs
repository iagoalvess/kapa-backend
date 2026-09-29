using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Texto;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Festa.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Loja.Interfaces;
using Backend.Business.Loja.Models;
using Backend.Business.MercadoPago.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Recebimentos.Services;
using Backend.Business.Relatorios.Models;
using Backend.Business.Relatorios.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Loja.Services;

/// <summary>
/// A loja pública da turma (Sprint 26): a vitrine, a compra sem conta, a compra pelo link e a lista da Gestão.
/// </summary>
/// <remarks>
/// A compra segue a ordem da decisão 8, e a ordem é o desenho: tudo o que pode recusar sem trava — forma, CPF,
/// abertura pelo relógio do servidor, estoque lido sem trava, meio de pagamento — vem antes, e quem perde sai com
/// "esgotado" numa consulta só (o item e a compra repetida juntos); a transação só
/// trava a chave de idempotência, reserva o item numa instrução condicional, confere o limite por CPF e grava.
/// A cobrança é emitida <b>depois</b>, fora dela (decisão 7): chamada HTTP segurando a linha do item faria a
/// fila inteira esperar o Mercado Pago.
/// <para>
/// As rotas anônimas apontam o escopo para a turma que provaram — a rota da loja, ou o link assinado da
/// compra —, e o filtro global faz o resto.
/// </para>
/// </remarks>
/// <param name="compras">Compras e a reserva no item.</param>
/// <param name="formaturas">A turma que vende.</param>
/// <param name="agenda">A festa.</param>
/// <param name="mercadoPago">A autorização da turma e a emissão.</param>
/// <param name="provedor">A cobrança viva da compra.</param>
/// <param name="convites">Os convites da compra.</param>
/// <param name="conviteService">A nomeação, com as regras do convite (Sprint 21).</param>
/// <param name="codigos">A assinatura do token do convite.</param>
/// <param name="link">O link assinado da compra.</param>
/// <param name="emails">Reserva e link reenviado.</param>
/// <param name="cancelamento">O pedido de cancelamento pelo link (Sprint 38, P1).</param>
/// <param name="baixa">A confirmação do que o cartão pagou (Sprint 39) — a mesma do aviso do Mercado Pago.</param>
/// <param name="cartaoValidator">Forma do cartão tokenizado.</param>
/// <param name="escopo">A turma da requisição anônima.</param>
/// <param name="fila">A fila de escrita da turma (decisão 8).</param>
/// <param name="validator">Forma da compra.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class LojaService(
    ICompraDeConviteRepository compras,
    IFormaturaRepository formaturas,
    IEventoDaTurmaRepository agenda,
    EmissaoNoMercadoPago mercadoPago,
    IProvedorDaTurmaRepository provedor,
    IConviteDoEventoRepository convites,
    IConviteDoEventoService conviteService,
    CodigoDoConvite codigos,
    LinkDaCompra link,
    EmailsDaLoja emails,
    ICancelamentoDaCompraService cancelamento,
    BaixaAutomatica baixa,
    IValidator<CartaoTokenizado> cartaoValidator,
    FormaturaDoProcessamento escopo,
    IFilaDaTurma fila,
    IValidator<DadosDaCompra> validator,
    IUnitOfWork unitOfWork,
    ILogger<LojaService> logger
) : ILojaService
{
    /// <summary>Quanto a reserva do PIX passa da validade do documento: o PIX vence antes, nunca depois (decisão 9).</summary>
    /// <remarks>O Mercado Pago aceita 30 minutos de mínimo, contados em minutos inteiros; a folga cobre o arredondamento.</remarks>
    public static readonly TimeSpan FolgaDaReserva = TimeSpan.FromMinutes(1);

    private static readonly Erro LojaNaoEncontrada = Erro.NaoEncontrado("loja.nao_encontrada", "Esta loja não existe ou não está vendendo.");

    private static readonly Erro ItemNaoEncontrado = Erro.NaoEncontrado("loja.item_nao_encontrado", "Este convite não está à venda nesta loja.");

    private static readonly Erro CompraNaoEncontrada = Erro.NaoEncontrado(
        "loja.compra_nao_encontrada",
        "Não encontramos esta compra. Se o link é antigo, peça um novo na loja."
    );

    private static readonly Erro Esgotado = Erro.Conflito("loja.esgotado", "Os convites esgotaram.");

    private static readonly Erro CartaoDesligado = Erro.Conflito(
        "pagamento.cartao_desligado",
        "A turma não está aceitando cartão agora. Faça a compra pelo PIX."
    );

    /// <inheritdoc />
    public async Task<Result<LojaDaTurma>> AbrirLoja(Guid formaturaId, CancellationToken ct = default)
    {
        if (await TurmaVendendo(formaturaId, ct) is not { } turma)
            return LojaNaoEncontrada;

        var itens = await compras.ListarItensDaLoja(ct);
        if (itens.Count == 0)
            return LojaNaoEncontrada;

        var agora = DateTime.UtcNow;
        var vendedor = await emails.Vendedor(formaturaId, ct);
        var credencial = await mercadoPago.Credencial(ct);

        return new LojaDaTurma(
            turma.Nome,
            turma.Instituicao,
            await Festa(ct),
            vendedor.Contato,
            MeiosDePagamento.DaTurma(credencial),
            agora,
            [
                .. itens
                    .OrderBy(item => item.CriadoEm)
                    .ThenBy(item => item.Id)
                    .Select(item => new ItemDaLoja(
                        item.Id,
                        Descricao(item.Descricao),
                        item.PrecoNaLoja,
                        item.Disponivel,
                        item.LimitePorFormando,
                        item.AberturaDeVendas,
                        item.PedidosAteDia,
                        item.Fechado(agora) is null && credencial is not null
                    )),
            ]
        );
    }

    /// <inheritdoc />
    public async Task<Result<CompraCriada>> Comprar(Guid formaturaId, DadosDaCompra dados, CancellationToken ct = default)
    {
        var compra = Normalizar(dados);

        var validacao = validator.Validar(compra);
        if (validacao.Falhou)
            return Result.Falha<CompraCriada>(validacao.Erros);

        escopo.Apontar(formaturaId);

        var (item, repetida) = await compras.ObterParaComprar(compra.ItemDeCobrancaId, compra.ChaveDeIdempotencia, ct);

        if (repetida is not null)
            return await Criada(repetida, ct);

        if (item is not { NaLoja: true })
            return ItemNaoEncontrado;

        var agora = DateTime.UtcNow;

        if (item.Fechado(agora) is { } fechado)
            return fechado;

        if (item.LimitePorFormando is { } limite && compra.Quantidade > limite)
            return LimitePorPessoa(limite);

        if (item.Disponivel is { } disponivel && disponivel < compra.Quantidade)
            return disponivel == 0 ? Esgotado : SoRestam(disponivel);

        if (await TurmaVendendo(formaturaId, ct) is null)
            return LojaNaoEncontrada;

        if (await mercadoPago.Credencial(ct) is not { } credencial)
            return Erro.Conflito("loja.sem_pagamento", "A loja está sem meio de pagamento agora. Tente mais tarde ou fale com a comissão.");

        if (!MeiosDePagamento.DaTurma(credencial).Contains(compra.Meio))
            return CartaoDesligado;

        var prazos = Prazos(agora);
        var vendedor = await emails.Vendedor(formaturaId, ct);
        var reservada = await NaFila(formaturaId, compra, item, prazos.Reserva, vendedor, ct);

        if (reservada.Falhou)
            return Result.Falha<CompraCriada>(reservada.Erros);

        logger.LogInformation("Compra {CompraId} reservou {Quantidade} do item {ItemId}.", reservada.Valor.Id, compra.Quantidade, item.Id);

        await Emitir(credencial, reservada.Valor, prazos.Documento, Pagador(compra), ct);

        return await Criada(reservada.Valor, ct);
    }

    /// <summary>
    /// A parte da compra que disputa a linha do item: entra na fila da turma, relê o estoque na vez e grava
    /// numa transação curta (decisões 7 e 8).
    /// </summary>
    /// <remarks>
    /// A releitura na vez é o que faz a fila andar depois que esgota: quem entrou quando ainda havia convite
    /// e chegou à vez sem nenhum sai com "esgotado" numa leitura, sem abrir transação. A vaga é devolvida
    /// antes de chamar o Mercado Pago — com ela presa, a fila inteira esperaria a rede (medido em 25/09/2026:
    /// p95 de 30 s para quem perdia, com 300 ms por emissão).
    /// </remarks>
    private async Task<Result<CompraDeConvite>> NaFila(
        Guid formaturaId,
        DadosDaCompra compra,
        ItemDeCobranca item,
        DateTime reservaAte,
        Vendedor vendedor,
        CancellationToken ct
    )
    {
        using var vaga = await fila.Entrar(formaturaId, ct);

        if (vaga is null)
            return Erro.Excesso("loja.fila_cheia", "Muita gente comprando agora. Tentando de novo em instantes.");

        if (await compras.ObterItem(item.Id, ct) is { Disponivel: { } disponivel } && disponivel < compra.Quantidade)
            return disponivel == 0 ? Esgotado : SoRestam(disponivel);

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                await compras.TravarChave(compra.ChaveDeIdempotencia, token);

                if (await compras.ObterPorChave(compra.ChaveDeIdempotencia, token) is { } gravada)
                    return Result.Ok(gravada);

                if (!await compras.ReservarNoItem(item.Id, compra.Quantidade, token))
                    return Result.Falha<CompraDeConvite>(Esgotado);

                if (item.LimitePorFormando is { } limite && await compras.ContarDoCpf(item.Id, compra.Cpf, token) + compra.Quantidade > limite)
                    return Result.Falha<CompraDeConvite>(LimitePorPessoa(limite));

                var nova = new CompraDeConvite(compra, item.PrecoNaLoja, reservaAte);
                await compras.Adicionar(nova, token);
                await emails.Reservada(nova, vendedor, token);
                await unitOfWork.SalvarAsync(token);

                return Result.Ok(nova);
            },
            ct
        );
    }

    /// <inheritdoc />
    public async Task<Result<CompraParaOComprador>> AbrirCompra(string token, CancellationToken ct = default)
    {
        var compra = await Localizar(token, ct);

        return compra.Falhou ? Result.Falha<CompraParaOComprador>(compra.Erros) : await ParaOComprador(compra.Valor, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O PIX reemitido vence junto com a reserva que resta — sem os 30 minutos que o Mercado Pago exige, não
    /// há mais como emitir, e o comprador faz uma compra nova.
    /// </remarks>
    public async Task<Result<CompraParaOComprador>> EmitirCobranca(string token, CancellationToken ct = default)
    {
        var localizada = await Localizar(token, ct);
        if (localizada.Falhou)
            return Result.Falha<CompraParaOComprador>(localizada.Erros);

        var compra = localizada.Valor;
        var agora = DateTime.UtcNow;
        var viva = await provedor.ObterViva(CobrancaBancaria.ChaveDaCompra(compra.Id), ct);

        if (compra.Status != StatusDaCompra.Pendente || compra.Meio == MeioDePagamento.Cartao || viva is { Status: StatusDaCobrancaBancaria.Emitida })
            return await ParaOComprador(compra, ct);

        if (viva is null && compra.ExpiraEm - FolgaDaReserva - agora < EmissaoNoMercadoPago.ValidadeMinima)
            return Erro.Conflito("loja.reserva_no_fim", "Não dá mais para gerar o pagamento desta reserva. Faça uma compra nova na loja.");

        if (await mercadoPago.Credencial(ct) is not { } credencial)
            return Erro.Conflito("loja.sem_pagamento", "A loja está sem meio de pagamento agora. Tente mais tarde ou fale com a comissão.");

        await Emitir(credencial, compra, viva?.ExpiraEm ?? compra.ExpiraEm - FolgaDaReserva, new PagadorNoMercadoPago(compra.Email!), ct);

        return await ParaOComprador(compra, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O cartão é cobrado dentro da reserva, que continua sendo a de 31 minutos: vencida, o lugar já voltou ao estoque,
    /// e o comprador faz uma compra nova. A baixa é a da <c>BaixaAutomatica</c>, como no PIX — a compra paga ganha os
    /// convites ali; o cartão em análise confirma quando o Mercado Pago avisar.
    /// </remarks>
    public async Task<Result<CompraParaOComprador>> PagarNoCartao(string token, CartaoDaCompra dados, CancellationToken ct = default)
    {
        var validacao = cartaoValidator.Validar(dados.Cartao);
        if (validacao.Falhou)
            return Result.Falha<CompraParaOComprador>(validacao.Erros);

        var localizada = await Localizar(token, ct);
        if (localizada.Falhou)
            return Result.Falha<CompraParaOComprador>(localizada.Erros);

        var compra = localizada.Valor;

        if (compra.Status != StatusDaCompra.Pendente || compra.Meio != MeioDePagamento.Cartao || compra.ExpiraEm <= DateTime.UtcNow)
            return Erro.Conflito(
                "loja.compra_nao_pendente",
                "Esta compra não está esperando pagamento no cartão. Se a reserva venceu, faça uma compra nova."
            );

        if (await mercadoPago.Credencial(ct) is not { } credencial || credencial.CartaoPara(compra.ValorEmCentavos) is not { } cartao)
            return CartaoDesligado;

        if (cartao.ValorEmCentavos != dados.ValorEmCentavos)
            return Erro.Conflito(
                "pagamento.valor_mudou",
                $"O valor mudou para {FormatosBrasileiros.Reais(cartao.ValorEmCentavos)}. Confira e pague de novo — o cartão não foi cobrado."
            );

        var cobranca = await mercadoPago.CartaoDaCompra(
            credencial,
            compra.Id,
            compra.ValorEmCentavos,
            new PagadorNoMercadoPago(compra.Email!),
            dados.Cartao,
            ct
        );
        if (cobranca.Falhou)
            return Result.Falha<CompraParaOComprador>(cobranca.Erros);

        await baixa.Conciliar(cobranca.Valor.Id, ct);

        return await ParaOComprador((await compras.Obter(compra.Id, ct))!, ct);
    }

    /// <inheritdoc />
    public async Task<Result<MeuConvite>> NomearConvidado(string token, Guid conviteId, DadosDoConvidado dados, CancellationToken ct = default)
    {
        var compra = await Localizar(token, ct);
        if (compra.Falhou)
            return Result.Falha<MeuConvite>(compra.Erros);

        return await conviteService.NomearDaCompra(conviteId, compra.Valor.Id, dados, ct);
    }

    /// <inheritdoc />
    public async Task<Result<CompraParaOComprador>> PedirCancelamento(string token, PedidoDoComprador dados, CancellationToken ct = default)
    {
        var compra = await Localizar(token, ct);
        if (compra.Falhou)
            return Result.Falha<CompraParaOComprador>(compra.Erros);

        var pedido = await cancelamento.Pedir(compra.Valor, dados, ct);

        return pedido.Falhou ? Result.Falha<CompraParaOComprador>(pedido.Erros) : await ParaOComprador(compra.Valor, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Sob o limite estreito de <c>Autenticacao</c>, como o "esqueci a senha": a resposta é a mesma exista
    /// compra ou não, e a distinção fica só no log.
    /// </remarks>
    public async Task<Result> ReenviarLink(Guid formaturaId, string email, CancellationToken ct = default)
    {
        escopo.Apontar(formaturaId);

        var dasCompras = await compras.ListarDoEmailParaEdicao(email.Trim().ToLowerInvariant(), ct);

        if (dasCompras.Count == 0)
        {
            logger.LogInformation("Reenvio de link da loja da formatura {FormaturaId} sem compra para o e-mail.", formaturaId);
            return Result.Ok();
        }

        var vendedor = await emails.Vendedor(formaturaId, ct);

        foreach (var compra in dasCompras)
        {
            compra.GirarLink();
            await emails.LinkReenviado(compra, vendedor, ct);
        }

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Links de {Quantidade} compra(s) reenviados na formatura {FormaturaId}.", dasCompras.Count, formaturaId);

        return Result.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Antes da festa, os dados sustentam o convite e a devolução — só a compra que expirou sem pagar os
    /// solta antes. O link morre junto: sem e-mail, não há para onde reenviá-lo.
    /// </remarks>
    public async Task<Result> ApagarDados(string token, CancellationToken ct = default)
    {
        var localizada = await Localizar(token, ct);
        if (localizada.Falhou)
            return Result.Falha(localizada.Erros);

        if (!PodeApagar(localizada.Valor, await Festa(ct)))
            return Result.Falha(
                Erro.Conflito(
                    "loja.dados_ainda_necessarios",
                    "Seus dados sustentam os convites e a devolução até a festa. Depois dela, você pode apagá-los por aqui."
                )
            );

        var compra = (await compras.ObterParaEdicao(localizada.Valor.Id, ct))!;
        compra.ApagarDados(DateTime.UtcNow, inclusiveONome: true);
        compras.EsquecerCpf(compra);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Dados do comprador da compra {CompraId} apagados a pedido dele.", compra.Id);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result<PaginaDe<CompraNaGestao>>> Listar(PaginacaoRequest paginacao, FiltroDeCompras filtro, CancellationToken ct = default) =>
        await compras.Listar(paginacao.Normalizar(), filtro, ct);

    /// <inheritdoc />
    public async Task<Result<ResumoDaLoja>> Resumir(CancellationToken ct = default) =>
        await compras.Resumir(ct) with
        {
            FestaId = (await agenda.ObterDoTipo(TipoDeEvento.Festa, ct))?.Id,
        };

    /// <inheritdoc />
    public async Task<Result<ArquivoParaDownload>> Exportar(FiltroDeCompras filtro, CancellationToken ct = default)
    {
        var linhas = await compras.ListarTodas(filtro, ct);

        var tabela = new TabelaDoRelatorio(
            "Compras da loja",
            $"Gerado em {DataUtils.ParaExibicao(DateTime.UtcNow).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} — a devolução é da turma",
            [
                new("Data", 1.2f),
                new("Comprador", 2f),
                new("E-mail", 2.4f),
                new("CPF", 1.4f),
                new("Convite", 1.6f),
                new("Qtd.", 0.6f, Direita: true),
                new("Valor", 1.2f, Direita: true),
                new("Meio", 0.9f),
                new("Situação", 1.2f),
                new("Pago em", 1.1f, Direita: true),
                new("Valor pago", 1.2f, Direita: true),
            ],
            [
                .. linhas.Select(linha =>
                    (IReadOnlyList<Celula>)
                        [
                            Celula.Data(DateOnly.FromDateTime(DataUtils.ParaExibicao(linha.CriadaEm))),
                            Celula.De(linha.Nome ?? "Dados apagados"),
                            Celula.De(linha.Email),
                            Celula.De(linha.Cpf),
                            Celula.De(linha.Item),
                            Celula.Inteiro(linha.Quantidade),
                            Celula.Reais(linha.ValorEmCentavos),
                            Celula.De(MeiosDePagamento.Rotulo(linha.Meio)),
                            Celula.De(Rotulo(linha.Status)),
                            Celula.Data(linha.PagaEm is { } paga ? DateOnly.FromDateTime(DataUtils.ParaExibicao(paga)) : null),
                            Celula.Reais(linha.ValorPagoEmCentavos),
                        ]
                ),
            ]
        );

        return new ArquivoParaDownload(
            new MemoryStream(RelatorioEmExcel.Gerar(tabela)),
            "compras-da-loja.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        );
    }

    /// <summary>Como a situação aparece na planilha.</summary>
    /// <param name="status">Situação.</param>
    public static string Rotulo(StatusDaCompra status) =>
        status switch
        {
            StatusDaCompra.Pendente => "Aguardando pagamento",
            StatusDaCompra.Paga => "Paga",
            StatusDaCompra.Expirada => "Expirada",
            StatusDaCompra.ADevolver => "A devolver",
            _ => "Devolvida",
        };

    /// <summary>
    /// Até quando a reserva vale e até quando o PIX aceita pagamento (decisão 3; P1 da Sprint 35).
    /// </summary>
    /// <remarks>
    /// O PIX vale os 30 minutos que o Mercado Pago exige, e a reserva, um minuto a mais — o QR morre antes de o
    /// lugar voltar ao estoque, nunca depois (decisão 9).
    /// </remarks>
    /// <param name="agora">Instante da compra, em UTC.</param>
    public static (DateTime Reserva, DateTime Documento) Prazos(DateTime agora)
    {
        var documento = agora + EmissaoNoMercadoPago.ValidadeMinima + TimeSpan.FromSeconds(30);

        return (documento + FolgaDaReserva, documento);
    }

    /// <summary>
    /// Aponta o escopo e devolve a turma, se ela está ativa e a festa não foi cancelada — turma suspensa ou
    /// encerrada não vende, e festa cancelada também não (Sprint 38, P10).
    /// </summary>
    private async Task<FormaturaDetalhe?> TurmaVendendo(Guid formaturaId, CancellationToken ct)
    {
        escopo.Apontar(formaturaId);

        if (await formaturas.ObterDetalheDeTodasAsFormaturas(formaturaId, ct) is not { Status: StatusDaFormatura.Ativa } turma)
            return null;

        return await agenda.ObterDoTipo(TipoDeEvento.Festa, ct) is { Situacao: SituacaoDoEvento.Cancelado } ? null : turma;
    }

    /// <summary>
    /// A compra do link: o id aponta a turma, e a assinatura com a versão atual prova o link. Link torto,
    /// antigo ou de compra inexistente respondem o mesmo 404.
    /// </summary>
    private async Task<Result<CompraDeConvite>> Localizar(string token, CancellationToken ct)
    {
        if (LinkDaCompra.Id(token) is not { } compraId || await compras.ObterFormaturaDeTodasAsFormaturas(compraId, ct) is not { } formaturaId)
            return CompraNaoEncontrada;

        escopo.Apontar(formaturaId);

        return await compras.Obter(compraId, ct) is { } compra && link.Confere(token, compra) ? compra : CompraNaoEncontrada;
    }

    /// <summary>
    /// Emite o PIX; a falha não desfaz a reserva — a tela oferece tentar de novo. A compra no cartão não emite nada:
    /// ela é cobrada quando o comprador manda o cartão (<see cref="PagarNoCartao"/>).
    /// </summary>
    private async Task Emitir(
        CredencialDeProvedor credencial,
        CompraDeConvite compra,
        DateTime documentoAte,
        PagadorNoMercadoPago pagador,
        CancellationToken ct
    )
    {
        if (compra.Status != StatusDaCompra.Pendente || compra.Meio == MeioDePagamento.Cartao)
            return;

        var cobranca = await mercadoPago.DaCompra(credencial, compra.Id, compra.Meio, compra.ValorEmCentavos, documentoAte, pagador, ct);

        if (cobranca is null)
            logger.LogWarning("Compra {CompraId} reservada sem cobrança; a tela pede de novo.", compra.Id);
    }

    private async Task<Result<CompraCriada>> Criada(CompraDeConvite compra, CancellationToken ct) =>
        new CompraCriada(link.Token(compra), await ParaOComprador(compra, ct));

    private async Task<CompraParaOComprador> ParaOComprador(CompraDeConvite compra, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        var item = await compras.ObterItem(compra.ItemDeCobrancaId, ct);
        var festa = await Festa(ct);
        var vendedor = await emails.Vendedor(compra.FormaturaId, ct);
        var cobranca =
            compra.Status == StatusDaCompra.Pendente
            && await provedor.ObterViva(CobrancaBancaria.ChaveDaCompra(compra.Id), ct) is { Status: StatusDaCobrancaBancaria.Emitida } viva
            && viva.Pagavel(agora)
                ? new CobrancaDaCompra(viva.Meio, viva.CopiaECola, viva.ExpiraEm)
                : null;
        var paga = compra.Status is not (StatusDaCompra.Pendente or StatusDaCompra.Expirada);
        var dosConvites = paga ? await convites.ListarDaCompra(compra.Id, ct) : [];
        var pedido = paga ? await compras.ObterUltimoPedido(compra.Id, ct) : null;

        return new CompraParaOComprador(
            compra.Id,
            compra.Status,
            Descricao(item?.Descricao),
            compra.Quantidade,
            compra.ValorEmCentavos,
            compra.Meio,
            compra.ExpiraEm,
            compra.PagaEm,
            compra.NomeDoComprador,
            compra.Email is null ? null : TextoUtils.MascararEmail(compra.Email),
            vendedor.Turma,
            vendedor.Contato,
            festa,
            cobranca,
            festa?.ListaAberta(agora) ?? true,
            PodeApagar(compra, festa),
            [.. dosConvites.Select(linha => ConviteDoEventoService.ParaMeu(linha.Convite, linha.ValidadoEm, codigos))],
            compra.FormaturaId,
            compra.ConvitesCancelados,
            compra.ValorADevolverEmCentavos,
            pedido is null
                ? null
                : new PedidoDoCompradorNaTela(pedido.Status, pedido.PedidoEm, pedido.ConviteIds.Length, pedido.RespondidoEm, pedido.MotivoDaResposta),
            pedido is not { Status: StatusDoPedidoDeCancelamento.Aberto } && dosConvites.Any(linha => linha.ValidadoEm is null),
            compra is { Status: StatusDaCompra.Pendente, Meio: MeioDePagamento.Cartao } && compra.ExpiraEm > agora && cobranca is null
                ? (await mercadoPago.Credencial(ct))?.CartaoPara(compra.ValorEmCentavos)
                : null
        );
    }

    private async Task<EventoDoConvite?> Festa(CancellationToken ct) =>
        await agenda.ObterDoTipo(TipoDeEvento.Festa, ct) is { } festa ? EmissaoDeConvites.ParaConvite(festa) : null;

    /// <summary>Se o comprador já pode apagar os dados: a compra expirou, ou a festa passou (decisão 5).</summary>
    private static bool PodeApagar(CompraDeConvite compra, EventoDoConvite? festa) =>
        compra.DadosApagadosEm is null && (compra.Status == StatusDaCompra.Expirada || (festa is not null && festa.Data < DataUtils.Hoje()));

    private static string Descricao(string? descricao) => string.IsNullOrWhiteSpace(descricao) ? "Convite da festa" : descricao;

    private static DadosDaCompra Normalizar(DadosDaCompra dados) =>
        dados with
        {
            Nome = dados.Nome?.Trim() ?? string.Empty,
            Email = dados.Email?.Trim().ToLowerInvariant() ?? string.Empty,
            Cpf = FormatosBrasileiros.SomenteDigitos(dados.Cpf),
            Convidados =
            [
                .. dados.Convidados.Select(c =>
                    c with
                    {
                        Nome = c.Nome.Trim(),
                        NumeroDoDocumento = DocumentoDoConvidado.Normalizar(c.TipoDoDocumento, c.NumeroDoDocumento),
                    }
                ),
            ],
        };

    /// <summary>Quem paga, como o Mercado Pago pede: e-mail e nome — a turma reconhece o comprador no painel dela.</summary>
    private static PagadorNoMercadoPago Pagador(DadosDaCompra compra)
    {
        var partes = compra.Nome.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        return new PagadorNoMercadoPago(compra.Email, partes[0], partes.Length > 1 ? partes[1] : partes[0]);
    }

    private static Erro LimitePorPessoa(int limite) =>
        Erro.Conflito(
            "loja.limite_por_pessoa",
            $"Cada pessoa pode comprar no máximo {limite} convite{(limite == 1 ? string.Empty : "s")} deste tipo."
        );

    private static Erro SoRestam(int disponivel) =>
        Erro.Conflito(
            "loja.esgotado",
            $"Só resta{(disponivel == 1 ? string.Empty : "m")} {disponivel} convite{(disponivel == 1 ? string.Empty : "s")}. Diminua a quantidade."
        );
}
