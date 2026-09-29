using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Arquivos.Services;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Services;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Texto;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.MercadoPago.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Recebimentos.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Pagamentos.Services;

/// <summary>
/// O caminho do dinheiro da turma, do lado do formando: extrato, cobrança e o "já paguei".
/// </summary>
/// <remarks>
/// O Kapa não vê o pagamento: monta a cobrança a partir dos meios que a comissão habilitou, e quem
/// confirma é a tesouraria, olhando o extrato do próprio banco (Sprint 8). Só o PIX vira BR Code; os
/// demais meios são instrução, e nenhum deles chama ninguém de fora nem cria cobrança. A conferência, a
/// baixa manual e o estorno são o <see cref="TesourariaService"/>.
/// <para>Log só com ids: parcela e informe dizem quanto alguém deve.</para>
/// </remarks>
/// <param name="parcelaRepository">Parcelas e regras aceitas.</param>
/// <param name="informeRepository">Avisos de pagamento.</param>
/// <param name="recebimentoRepository">Baixas, de onde sai o recibo.</param>
/// <param name="contaRepository">Os meios de recebimento da comissão.</param>
/// <param name="perfilRepository">Quem pede, e com que papel.</param>
/// <param name="arquivoService">Comprovantes.</param>
/// <param name="mercadoPago">O PIX do Mercado Pago da turma, quando ela conectou (Sprint 25).</param>
/// <param name="baixa">A baixa do que o cartão pagou (Sprint 39) — a mesma do aviso do Mercado Pago.</param>
/// <param name="informeValidator">Forma do "já paguei".</param>
/// <param name="cartaoValidator">Forma do cartão tokenizado.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class PagamentoService(
    IParcelaRepository parcelaRepository,
    IInformeRepository informeRepository,
    IRecebimentoRepository recebimentoRepository,
    IContaDeRecebimentoRepository contaRepository,
    IPerfilRepository perfilRepository,
    IArquivoService arquivoService,
    EmissaoNoMercadoPago mercadoPago,
    BaixaAutomatica baixa,
    IValidator<NovoInforme> informeValidator,
    IValidator<CartaoTokenizado> cartaoValidator,
    IUnitOfWork unitOfWork,
    ILogger<PagamentoService> logger
) : IPagamentoService
{
    /// <summary>Teto de parcelas num aviso de pagamento só.</summary>
    /// <remarks>Dois anos de mensalidade: quem se acerta de uma vez cobre o atraso inteiro, e além disso é engano.</remarks>
    public const int ParcelasPorInforme = 24;

    private static readonly Erro ParcelaPaga = Erro.Conflito(
        "pagamento.parcela_paga",
        "Essa parcela já está paga. Se pagou de novo, fale com a tesouraria."
    );

    private static readonly Erro InformePendente = Erro.Conflito(
        "pagamento.informe_pendente",
        "Você já avisou o pagamento de uma dessas parcelas. A tesouraria vai conferir, e você recebe um e-mail quando for confirmado."
    );

    /// <summary>
    /// A turma não tem por onde receber.
    /// </summary>
    /// <remarks>
    /// O código continua o mesmo — é contrato do front desde a Sprint 8 —, mas passou a falar de conta
    /// de recebimento, e não de chave PIX (decisão 1): desde o P4 de 21/09/2026 a chave é opcional, e o
    /// que falta aqui pode ser qualquer meio.
    /// </remarks>
    private static readonly Erro SemConta = Erro.Conflito(
        "pagamento.sem_conta",
        "A comissão ainda está configurando a conta de recebimento da turma. Tente de novo em alguns dias."
    );

    /// <inheritdoc />
    /// <remarks>
    /// Em aberto soma o valor do dia — com multa e juros, ou com desconto —, e não o original. A
    /// soma vai com sinal: o crédito de um pedido cancelado (P5 da Sprint 20) e a bolsa lançada como
    /// <c>Avulsa</c> negativa <b>abatem</b> o que a pessoa deve, que é para isso que existem.
    /// <para>
    /// A <b>próxima a pagar</b>, porém, pula o que é crédito: "pague −R$ 350,00" não é uma frase, e o
    /// botão levaria a um PIX de valor negativo.
    /// </para>
    /// <para>
    /// Do titular: o extrato é a prova do que a pessoa pagou, e não some com a saída (P5 da Sprint 15).
    /// </para>
    /// </remarks>
    public async Task<Result<ExtratoDoFormando>> ObterExtrato(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var membro = await perfilRepository.ObterTitular(formaturaId, usuarioId, ct);
        if (membro is null)
            return ErrosDeFormatura.MembroNaoEncontrado;

        var hoje = DataUtils.Hoje();
        var parcelas = await parcelaRepository.ComValorDoDia(await parcelaRepository.ListarDoVinculo(membro.VinculoId, hoje, ct), hoje, ct);

        return new ExtratoDoFormando(
            parcelas.Where(p => p.EmAberto).Sum(p => p.ValorDoDia!.TotalEmCentavos),
            parcelas.FirstOrDefault(p => p.EmAberto && !p.EmConferencia && p.ValorDoDia!.TotalEmCentavos > 0),
            parcelas
        );
    }

    /// <inheritdoc />
    public async Task<Result<int>> ContarVencidasSemAviso(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var membro = await perfilRepository.ObterTitular(formaturaId, usuarioId, ct);
        if (membro is null)
            return ErrosDeFormatura.MembroNaoEncontrado;

        return await parcelaRepository.ContarVencidasSemAviso(membro.VinculoId, DataUtils.Hoje(), ct);
    }

    /// <inheritdoc />
    public Task<Result<ParcelaResumo>> ObterParcela(Guid formaturaId, Guid usuarioId, Guid parcelaId, CancellationToken ct = default) =>
        ParcelaVisivel(formaturaId, usuarioId, parcelaId, PapelNaFormatura.Gestao, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Montada na hora e não gravada (decisão 1 da Sprint 8): os meios são os vigentes, e o valor é o de
    /// hoje. Conta não conferida mostra o PIX do mesmo jeito (P3 de 14/09/2026); só turma sem meio
    /// nenhum responde 409.
    /// </remarks>
    public async Task<Result<CobrancaDaParcela>> GerarCobranca(Guid formaturaId, Guid usuarioId, Guid parcelaId, CancellationToken ct = default)
    {
        var visivel = await ParcelaVisivel(formaturaId, usuarioId, parcelaId, PapelNaFormatura.Tesouraria, ct);
        if (visivel.Falhou)
            return Result.Falha<CobrancaDaParcela>(visivel.Erros);

        var parcela = visivel.Valor;

        if (parcela.Status == StatusDaParcela.Paga)
            return ParcelaPaga;

        if (parcela.ValorDoDia is not { TotalEmCentavos: > 0 } valor)
            return ErrosDePagamento.ParcelaNaoAberta;

        var titular = await perfilRepository.ObterTitular(formaturaId, usuarioId, ct);
        var pagador = titular?.VinculoId == parcela.VinculoId ? titular.Email : null;

        return await Cobrar(valor.TotalEmCentavos, [parcela.Id], pagador, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Um BR Code só, com a soma do que as parcelas cobram hoje: é o mesmo pagamento, e dois QR Codes
    /// numa tela viram dois PIX pela metade. As parcelas passam pelas mesmas conferências do aviso
    /// (<see cref="Conferir"/>) — quem não pode avisar também não deveria ver o QR —, e o identificador
    /// é o da mais antiga: no PIX estático ele não reconcilia nada, e a mais antiga é a que a tesouraria
    /// procura no extrato.
    /// </remarks>
    public async Task<Result<CobrancaDaParcela>> GerarCobrancaDeVarias(
        Guid formaturaId,
        Guid usuarioId,
        IReadOnlyList<Guid> parcelaIds,
        CancellationToken ct = default
    )
    {
        if (parcelaIds.Count == 0 || parcelaIds.Count > ParcelasPorInforme)
            return Erro.Validacao(
                "pagamento.parcelas_do_informe",
                $"Escolha de 1 a {ParcelasPorInforme} parcelas para este pagamento.",
                campo: "parcela_ids"
            );

        var conferidas = await Conferir(formaturaId, usuarioId, parcelaIds, ct);
        if (conferidas.Falhou)
            return Result.Falha<CobrancaDaParcela>(conferidas.Erros);

        var parcelas = conferidas.Valor;

        var titular = await perfilRepository.ObterTitular(formaturaId, usuarioId, ct);

        return await Cobrar(
            parcelas.Sum(parcela => parcela.ValorDoDia?.TotalEmCentavos ?? parcela.ValorOriginalEmCentavos),
            [.. parcelas.Select(parcela => parcela.Id)],
            titular?.Email,
            ct
        );
    }

    /// <summary>
    /// A cobrança de um valor: os meios que a turma aceita, cada um com o que a tela precisa mostrar.
    /// </summary>
    /// <remarks>
    /// O único meio que depende do valor é o PIX, que o carrega dentro do BR Code; os outros são
    /// instrução, e a mesma instrução serve para qualquer quantia. Por isso a parcela avulsa e o lote
    /// chegam aqui com um número só.
    /// </remarks>
    /// <param name="valorEmCentavos">O que se vai pagar.</param>
    /// <param name="parcelaIds">Parcelas do pagamento; a primeira — a mais antiga, no lote — dá o identificador do PIX estático.</param>
    /// <param name="emailDoPagador">E-mail do dono das parcelas; nulo quando quem vê é a tesouraria, que não paga — e então não se emite PIX dinâmico.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <remarks>
    /// Um modo ou o outro (29/09/2026): na cobrança automática, só o Mercado Pago — e, se ele não responde, a tela
    /// pede para tentar de novo em vez de oferecer a chave da comissão, que voltaria a exigir aviso e conferência;
    /// na manual, só os meios da comissão, e nada se emite no Mercado Pago.
    /// </remarks>
    private async Task<Result<CobrancaDaParcela>> Cobrar(
        long valorEmCentavos,
        IReadOnlyList<Guid> parcelaIds,
        string? emailDoPagador,
        CancellationToken ct
    )
    {
        if (await CredencialAutomatica(ct) is { } credencial)
        {
            var peloMercadoPago = await PeloMercadoPago(credencial, valorEmCentavos, parcelaIds, emailDoPagador, ct);

            if (emailDoPagador is not null && peloMercadoPago.Count == 0)
                return Erro.Indisponivel(
                    "pagamento.mercado_pago_indisponivel",
                    "O Mercado Pago não respondeu agora. Tente de novo em alguns minutos."
                );

            return new CobrancaDaParcela(valorEmCentavos, Identificador(parcelaIds[0]), peloMercadoPago, []);
        }

        var conta = await contaRepository.ObterDetalhe(ct);

        if (conta is null || conta.Meios.Habilitados.Count == 0)
            return SemConta;

        var meios = conta.Meios;
        var identificador = Identificador(parcelaIds[0]);

        return new CobrancaDaParcela(
            valorEmCentavos,
            identificador,
            [],
            [
                .. meios.Habilitados.Select(meio =>
                    meio switch
                    {
                        MeioDeRecebimento.Pix => new MeioDaCobranca(
                            meio,
                            new PixParaPagar(
                                BrCode.Montar(meios.Pix!.Chave, meios.Pix.NomeDoTitular, meios.Pix.Cidade, valorEmCentavos, identificador),
                                meios.Pix.Chave,
                                meios.Pix.NomeDoTitular,
                                ChavePix.DocumentoDoTitular(meios.Pix.TipoDeChave, meios.Pix.Chave),
                                conta!.ConferidaEm
                            ),
                            null,
                            null
                        ),
                        MeioDeRecebimento.Transferencia => new MeioDaCobranca(meio, null, meios.Transferencia, null),
                        _ => new MeioDaCobranca(meio, null, null, InstrucaoDoDinheiro(meios.Dinheiro!)),
                    }
                ),
            ]
        );
    }

    /// <summary>
    /// Os meios do Mercado Pago da turma (<see cref="MeiosDePagamento.DaTurma"/>): o PIX, emitido na hora, e o
    /// cartão, quando a Tesouraria o ligou (Sprint 39) — que só se cobra quando o formando manda o formulário.
    /// Só para o dono das parcelas: a tesouraria vê a cobrança de qualquer um, mas nada se emite em nome dela.
    /// </summary>
    private async Task<IReadOnlyList<PagamentoPeloMercadoPago>> PeloMercadoPago(
        CredencialDeProvedor credencial,
        long valorEmCentavos,
        IReadOnlyList<Guid> parcelaIds,
        string? emailDoPagador,
        CancellationToken ct
    )
    {
        if (emailDoPagador is null)
            return [];

        var pix = await mercadoPago.Pix(credencial, parcelaIds, valorEmCentavos, emailDoPagador, ct);
        var cartao = credencial.CartaoPara(valorEmCentavos);

        return
        [
            .. pix is null ? [] : new[] { new PagamentoPeloMercadoPago(MeioDePagamento.Pix, pix) },
            .. cartao is null ? [] : new[] { new PagamentoPeloMercadoPago(MeioDePagamento.Cartao, null, cartao) },
        ];
    }

    /// <inheritdoc />
    /// <remarks>
    /// As parcelas passam pelas mesmas conferências do aviso — só o dono, abertas, sem aviso pendente. O valor é
    /// recalculado aqui, e se não for o que a tela mostrou o cartão não é cobrado: depois da meia-noite o valor do
    /// dia muda, e cobrar outro número do que a pessoa confirmou é o erro que não se desfaz. Quem baixa é a
    /// <see cref="BaixaAutomatica"/>, pela consulta ao pedido — o aviso do Mercado Pago que chegar depois encontra
    /// a cobrança já paga.
    /// </remarks>
    public async Task<Result<SituacaoDoCartao>> PagarNoCartao(
        Guid formaturaId,
        Guid usuarioId,
        PagamentoNoCartao dados,
        CancellationToken ct = default
    )
    {
        var validacao = cartaoValidator.Validar(dados.Cartao);
        if (validacao.Falhou)
            return Result.Falha<SituacaoDoCartao>(validacao.Erros);

        if (dados.ParcelaIds.Count == 0 || dados.ParcelaIds.Count > ParcelasPorInforme)
            return Erro.Validacao(
                "pagamento.parcelas_do_informe",
                $"Escolha de 1 a {ParcelasPorInforme} parcelas para este pagamento.",
                campo: "parcela_ids"
            );

        var conferidas = await Conferir(formaturaId, usuarioId, dados.ParcelaIds, ct);
        if (conferidas.Falhou)
            return Result.Falha<SituacaoDoCartao>(conferidas.Erros);

        var parcelas = conferidas.Valor;
        var valor = parcelas.Sum(parcela => parcela.ValorDoDia?.TotalEmCentavos ?? parcela.ValorOriginalEmCentavos);

        if (await CredencialAutomatica(ct) is not { } credencial || credencial.CartaoPara(valor) is not { } cartao)
            return Erro.Conflito(
                "pagamento.cartao_desligado",
                "A turma não está aceitando cartão agora. Volte à tela de pagar e veja os meios de hoje."
            );

        if (cartao.ValorEmCentavos != dados.ValorEmCentavos)
            return Erro.Conflito(
                "pagamento.valor_mudou",
                $"O valor mudou para {FormatosBrasileiros.Reais(cartao.ValorEmCentavos)}. Confira e pague de novo — o cartão não foi cobrado."
            );

        var titular = await perfilRepository.ObterTitular(formaturaId, usuarioId, ct);

        var cobranca = await mercadoPago.Cartao(
            credencial,
            [.. parcelas.Select(parcela => parcela.Id)],
            valor,
            new PagadorNoMercadoPago(titular!.Email),
            dados.Cartao,
            ct
        );
        if (cobranca.Falhou)
            return Result.Falha<SituacaoDoCartao>(cobranca.Erros);

        var baixou = await baixa.Conciliar(cobranca.Valor.Id, ct);

        logger.LogInformation(
            "Cartão de {Quantidade} parcela(s) do vínculo {VinculoId}: cobrança {CobrancaId}.",
            parcelas.Count,
            parcelas[0].VinculoId,
            cobranca.Valor.Id
        );

        return baixou is { Sucesso: true, Valor: true } ? SituacaoDoCartao.Pago : SituacaoDoCartao.EmAnalise;
    }

    /// <summary>A credencial da turma quando ela cobra pelo Mercado Pago; nula no modo manual ou sem conexão.</summary>
    private async Task<CredencialDeProvedor?> CredencialAutomatica(CancellationToken ct) =>
        await mercadoPago.Credencial(ct) is { CobrancaAutomatica: true } credencial ? credencial : null;

    /// <summary>Com quem falar para pagar em espécie, e onde quando a comissão disse.</summary>
    private static string InstrucaoDoDinheiro(DinheiroComAlguem dinheiro) =>
        dinheiro.Onde is null ? $"Entregue a {dinheiro.Nome}." : $"Entregue a {dinheiro.Nome}, {dinheiro.Onde}.";

    /// <inheritdoc />
    /// <remarks>
    /// O comprovante é gravado antes do informe: se o informe falhar depois, sobra um arquivo órfão — nunca
    /// um informe apontando para comprovante que não existe. As conferências vêm antes do envio, para o
    /// arquivo não subir à toa.
    /// <para>
    /// A gravação trava as parcelas e confere de novo o status e o aviso pendente, sob a mesma trava da baixa
    /// manual: aviso e baixa que chegam juntos se enfileiram, e o segundo encontra o primeiro — em vez de
    /// sobrar um aviso pendente sobre uma parcela já paga.
    /// </para>
    /// </remarks>
    public async Task<Result<IReadOnlyList<ParcelaResumo>>> Informar(
        Guid formaturaId,
        Guid usuarioId,
        IReadOnlyList<Guid> parcelaIds,
        NovoInforme dados,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    )
    {
        var validacao = informeValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<IReadOnlyList<ParcelaResumo>>(validacao.Erros);

        if (parcelaIds.Count == 0 || parcelaIds.Count > ParcelasPorInforme)
            return Erro.Validacao(
                "pagamento.parcelas_do_informe",
                $"Escolha de 1 a {ParcelasPorInforme} parcelas para este pagamento.",
                campo: "parcela_ids"
            );

        if (await CredencialAutomatica(ct) is not null)
            return Erro.Conflito(
                "pagamento.aviso_desligado",
                "Esta turma recebe pelo Mercado Pago, e o pagamento é confirmado sozinho. Se você pagou por fora, fale com a tesouraria."
            );

        var conferidas = await Conferir(formaturaId, usuarioId, parcelaIds, ct);
        if (conferidas.Falhou)
            return Result.Falha<IReadOnlyList<ParcelaResumo>>(conferidas.Erros);

        var arquivo = await ComprovanteDePagamento.Enviar(arquivoService, comprovante, usuarioId, ct);
        if (arquivo.Falhou)
            return Result.Falha<IReadOnlyList<ParcelaResumo>>(arquivo.Erros);

        var parcelas = conferidas.Valor;

        var gravados = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var ids = parcelas.Select(parcela => parcela.Id).ToList();
                var travadas = await parcelaRepository.TravarParaBaixa(ids, token);

                if (travadas.Any(parcela => parcela.Status == StatusDaParcela.Paga))
                    return Result.Falha<IReadOnlyList<ParcelaResumo>>(ParcelaPaga);

                if (travadas.Count != ids.Count || travadas.Any(parcela => parcela.Status != StatusDaParcela.Aberta))
                    return Result.Falha<IReadOnlyList<ParcelaResumo>>(ErrosDePagamento.ParcelaNaoAberta);

                if (await informeRepository.ExistePendente(ids, token))
                    return Result.Falha<IReadOnlyList<ParcelaResumo>>(InformePendente);

                var aDistribuir = dados.ValorEmCentavos;
                var informadas = new List<ParcelaResumo>(parcelas.Count);

                foreach (var (parcela, ultima) in parcelas.Select((p, i) => (p, i == parcelas.Count - 1)))
                {
                    var cabe = ultima ? aDistribuir : Math.Min(aDistribuir, parcela.ValorDoDia?.TotalEmCentavos ?? parcela.ValorOriginalEmCentavos);
                    aDistribuir -= cabe;

                    var informe = InformeDePagamento.Novo(parcela.Id, parcela.VinculoId, dados.PagoEm, cabe, arquivo.Valor, dados.Meio);
                    await informeRepository.Adicionar(informe, token);

                    informadas.Add(parcela with { EmConferencia = true });
                }

                return Result.Ok<IReadOnlyList<ParcelaResumo>>(informadas);
            },
            ct
        );

        if (gravados.Falhou)
        {
            await arquivoService.DescartarComprovante(arquivo.Valor, usuarioId, ct);
            return gravados;
        }

        logger.LogInformation("{Quantidade} parcelas informadas de uma vez pelo vínculo {VinculoId}.", parcelas.Count, parcelas[0].VinculoId);

        return gravados;
    }

    /// <inheritdoc />
    /// <remarks>
    /// O titular é o da conta como estava no dia do pagamento, lido da trilha de auditoria (P2 da
    /// Sprint 22): a conta de hoje pode ser outra, e o recibo não muda com uma troca de chave. O
    /// instante é o fim do dia do pagamento, ou a baixa se veio antes — a troca feita depois de o
    /// dinheiro entrar não é a conta que o recebeu. Sem evento até lá (conta anterior à auditoria, ou
    /// além da retenção), vale a conta atual.
    /// </remarks>
    public async Task<Result<ArquivoParaDownload>> ObterRecibo(Guid formaturaId, Guid usuarioId, Guid recebimentoId, CancellationToken ct = default)
    {
        var recibo = await recebimentoRepository.ObterParaRecibo(recebimentoId, DataUtils.Hoje(), ct);
        var titular = await perfilRepository.ObterTitular(formaturaId, usuarioId, ct);

        if (recibo is null || titular is null)
            return ErrosDePagamento.RecebimentoNaoEncontrado;

        var proprio = recibo.Parcela.VinculoId == titular.VinculoId;

        if (
            !proprio
            && !(await perfilRepository.ObterMembro(formaturaId, usuarioId, ct) is { } membro && PapelNaFormatura.Gestao.Contains(membro.Papel))
        )
            return ErrosDePagamento.RecebimentoNaoEncontrado;

        if (recibo.Estornado)
            return ErrosDePagamento.RecebimentoEstornado;

        var instante = new[] { recibo.BaixadoEm, DataUtils.FimDoDiaEmUtc(recibo.PagoEm) }.Min();
        var meios = await contaRepository.ObterMeiosVigentesEm(formaturaId, instante, ct) ?? (await contaRepository.ObterDetalhe(ct))?.Meios;

        return new ArquivoParaDownload(
            new MemoryStream(ReciboEmPdf.Gerar(recibo, meios, mascararCpf: !proprio)),
            $"recibo-{recibo.PagoEm.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf",
            "application/pdf"
        );
    }

    /// <summary>
    /// As parcelas do aviso, do vencimento mais antigo ao mais novo, conferidas uma a uma.
    /// </summary>
    /// <remarks>
    /// Uma consulta por parcela: são poucas, e reusar <see cref="ParcelaVisivel"/> mantém a regra de
    /// quem enxerga a parcela num lugar só. <c>ponytail:</c> vira uma consulta por lista se o teto
    /// subir muito.
    /// </remarks>
    private async Task<Result<IReadOnlyList<ParcelaResumo>>> Conferir(
        Guid formaturaId,
        Guid usuarioId,
        IReadOnlyList<Guid> parcelaIds,
        CancellationToken ct
    )
    {
        var parcelas = new List<ParcelaResumo>(parcelaIds.Count);

        foreach (var parcelaId in parcelaIds.Distinct())
        {
            var visivel = await ParcelaVisivel(formaturaId, usuarioId, parcelaId, [], ct);
            if (visivel.Falhou)
                return Result.Falha<IReadOnlyList<ParcelaResumo>>(visivel.Erros);

            var parcela = visivel.Valor;

            if (parcela.Status == StatusDaParcela.Paga)
                return Result.Falha<IReadOnlyList<ParcelaResumo>>(ParcelaPaga);

            if (!parcela.EmAberto)
                return Result.Falha<IReadOnlyList<ParcelaResumo>>(ErrosDePagamento.ParcelaNaoAberta);

            if (parcela.EmConferencia)
                return Result.Falha<IReadOnlyList<ParcelaResumo>>(InformePendente);

            parcelas.Add(parcela);
        }

        return Result.Ok<IReadOnlyList<ParcelaResumo>>([.. parcelas.OrderBy(parcela => parcela.Vencimento).ThenBy(parcela => parcela.Id)]);
    }

    /// <summary>
    /// O identificador da parcela no PIX: <c>KAPA</c> e o final do id, 25 caracteres — o teto do campo.
    /// </summary>
    /// <remarks>O final, e não o começo: o começo do UUIDv7 é o instante, e as parcelas da mesma adesão o dividem.</remarks>
    /// <param name="parcelaId">Parcela.</param>
    public static string Identificador(Guid parcelaId) =>
        $"KAPA{parcelaId.ToString("N")[^(BrCode.TamanhoMaximoDoIdentificador - 4)..].ToUpperInvariant()}";

    /// <summary>
    /// A parcela, se quem pede é o dono ou tem um dos papéis que a veem; senão, 404 — como se não existisse.
    /// </summary>
    /// <remarks>
    /// Desligado vê só as próprias, qualquer que fosse o papel: o atraso mantido na saída continua
    /// devido, e é por aqui que ele gera o PIX e avisa o pagamento. Papel de gestão não sobrevive à saída.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    /// <param name="parcelaId">Parcela.</param>
    /// <param name="papeisQueVeem">Papéis que veem a parcela de qualquer formando; vazio, só o dono.</param>
    private async Task<Result<ParcelaResumo>> ParcelaVisivel(
        Guid formaturaId,
        Guid usuarioId,
        Guid parcelaId,
        IReadOnlyList<string> papeisQueVeem,
        CancellationToken ct
    )
    {
        var hoje = DataUtils.Hoje();
        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        var parcela = await parcelaRepository.Obter(parcelaId, hoje, ct);

        if (parcela is null)
            return ErrosDePagamento.ParcelaNaoEncontrada;

        if (membro is null)
            return await perfilRepository.ObterTitular(formaturaId, usuarioId, ct) is { } titular && parcela.VinculoId == titular.VinculoId
                ? (await parcelaRepository.ComValorDoDia([parcela], hoje, ct))[0]
                : ErrosDePagamento.ParcelaNaoEncontrada;

        if (parcela.VinculoId != membro.VinculoId && !papeisQueVeem.Contains(membro.Papel))
            return ErrosDePagamento.ParcelaNaoEncontrada;

        return (await parcelaRepository.ComValorDoDia([parcela], hoje, ct))[0];
    }
}
