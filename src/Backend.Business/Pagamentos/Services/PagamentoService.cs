using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Services;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Pagamentos.Services;

/// <summary>
/// O caminho do dinheiro da turma: extrato, PIX, "já paguei", conferência em lote, baixa manual e estorno.
/// </summary>
/// <remarks>
/// O Kapa não vê o pagamento: monta o PIX a partir da chave da comissão, e quem confirma é a tesouraria,
/// olhando o extrato do próprio banco (Sprint 8). Toda baixa passa por <see cref="BaixaService"/>, dentro
/// de uma transação que trava a parcela antes de ler o informe.
/// <para>Log só com ids: parcela e informe dizem quanto alguém deve.</para>
/// </remarks>
/// <param name="parcelaRepository">Parcelas e regras aceitas.</param>
/// <param name="informeRepository">Avisos de pagamento.</param>
/// <param name="recebimentoRepository">Entradas no caixa.</param>
/// <param name="contaRepository">A chave PIX da comissão.</param>
/// <param name="perfilRepository">Quem pede, e com que papel.</param>
/// <param name="vinculoRepository">E-mails dos formandos.</param>
/// <param name="formaturaRepository">Nome da turma, para os e-mails.</param>
/// <param name="arquivoService">Comprovantes.</param>
/// <param name="baixaService">A porta única da baixa.</param>
/// <param name="emails">Aviso de recusa.</param>
/// <param name="eventos">Auditoria do estorno.</param>
/// <param name="informeValidator">Forma do "já paguei".</param>
/// <param name="baixaValidator">Forma da baixa manual.</param>
/// <param name="loteValidator">Forma do lote.</param>
/// <param name="recusaValidator">Forma da recusa.</param>
/// <param name="estornoValidator">Forma do estorno.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class PagamentoService(
    IParcelaRepository parcelaRepository,
    IInformeRepository informeRepository,
    IRecebimentoRepository recebimentoRepository,
    IContaDeRecebimentoRepository contaRepository,
    IPerfilRepository perfilRepository,
    IVinculoRepository vinculoRepository,
    IFormaturaRepository formaturaRepository,
    IArquivoService arquivoService,
    BaixaService baixaService,
    EmailsDePagamento emails,
    IEventoRepository eventos,
    IValidator<NovoInforme> informeValidator,
    IValidator<BaixaManual> baixaValidator,
    IValidator<ConfirmarInformes> loteValidator,
    IValidator<RecusarInforme> recusaValidator,
    IValidator<EstornarBaixa> estornoValidator,
    IUnitOfWork unitOfWork,
    ILogger<PagamentoService> logger
) : IPagamentoService
{
    /// <summary>Categoria dos comprovantes no módulo de arquivos.</summary>
    public const string CategoriaDoComprovante = "comprovantes";

    /// <summary>Evento do estorno, com a justificativa — a segunda linha da auditoria, ao lado da baixa.</summary>
    public const string EventoDeEstorno = "pagamento.estornado";

    /// <summary>
    /// Comprovante é PDF ou imagem.
    /// </summary>
    /// <remarks>
    /// Planilha e texto passam no módulo de arquivos, mas não comprovam pagamento — e comprovante sem
    /// restrição de tipo vira depósito de arquivos. Mesma lista do comprovante da despesa (Sprint 10),
    /// repetida de propósito: são duas decisões que podem divergir, não uma regra em dois lugares.
    /// </remarks>
    private static readonly HashSet<string> ExtensoesDoComprovante = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
    };

    private static readonly Erro ParcelaNaoEncontrada = Erro.NaoEncontrado("pagamento.parcela_nao_encontrada", "Parcela não encontrada.");

    private static readonly Erro InformeNaoEncontrado = Erro.NaoEncontrado("pagamento.informe_nao_encontrado", "Aviso de pagamento não encontrado.");

    private static readonly Erro ParcelaPaga = Erro.Conflito(
        "pagamento.parcela_paga",
        "Essa parcela já está paga. Se pagou de novo, fale com a tesouraria."
    );

    private static readonly Erro ParcelaNaoAberta = Erro.Conflito("pagamento.parcela_nao_aberta", "Esta parcela não está em aberto.");

    /// <inheritdoc />
    /// <remarks>Em aberto soma o valor do dia — com multa e juros, ou com desconto —, e não o original.</remarks>
    public async Task<Result<ExtratoDoFormando>> ObterExtrato(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        if (membro is null)
            return Erro.NaoEncontrado("membro.nao_encontrado", "Membro não encontrado nesta formatura.");

        var hoje = DataUtils.Hoje();
        var parcelas = await parcelaRepository.ComValorDoDia(await parcelaRepository.ListarDoVinculo(membro.VinculoId, hoje, ct), hoje, ct);

        return new ExtratoDoFormando(
            parcelas.Where(p => p.EmAberto).Sum(p => p.ValorDoDia!.TotalEmCentavos),
            parcelas.FirstOrDefault(p => p.EmAberto && !p.EmConferencia),
            parcelas
        );
    }

    /// <inheritdoc />
    public Task<Result<ParcelaResumo>> ObterParcela(Guid formaturaId, Guid usuarioId, Guid parcelaId, CancellationToken ct = default) =>
        ParcelaVisivel(formaturaId, usuarioId, parcelaId, PapelNaFormatura.Gestao, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Montado na hora e não gravado (decisão 1): a chave é a vigente, e o valor é o de hoje. Conta não
    /// conferida mostra o PIX do mesmo jeito (P3 de 14/09/2026); só turma sem conta responde 409.
    /// </remarks>
    public async Task<Result<PixDaParcela>> GerarPix(Guid formaturaId, Guid usuarioId, Guid parcelaId, CancellationToken ct = default)
    {
        var visivel = await ParcelaVisivel(formaturaId, usuarioId, parcelaId, PapelNaFormatura.Tesouraria, ct);
        if (visivel.Falhou)
            return Result.Falha<PixDaParcela>(visivel.Erros);

        var parcela = visivel.Valor;

        if (parcela.Status == StatusDaParcela.Paga)
            return ParcelaPaga;

        if (parcela.ValorDoDia is not { TotalEmCentavos: > 0 } valor)
            return ParcelaNaoAberta;

        var conta = await contaRepository.ObterDetalhe(ct);
        if (conta is null)
            return Erro.Conflito(
                "pagamento.sem_conta",
                "A comissão ainda está configurando a conta de recebimento da turma. Tente de novo em alguns dias."
            );

        var identificador = Identificador(parcela.Id);

        return new PixDaParcela(
            BrCode.Montar(conta.Chave, conta.NomeDoTitular, conta.Cidade, valor.TotalEmCentavos, identificador),
            valor.TotalEmCentavos,
            conta.Chave,
            conta.NomeDoTitular,
            identificador
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// O comprovante é gravado antes do informe: se o informe falhar depois, sobra um arquivo órfão — nunca
    /// um informe apontando para comprovante que não existe. As conferências vêm antes do envio, para o
    /// arquivo não subir à toa.
    /// </remarks>
    public async Task<Result<ParcelaResumo>> Informar(
        Guid formaturaId,
        Guid usuarioId,
        Guid parcelaId,
        NovoInforme dados,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    )
    {
        var validacao = informeValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ParcelaResumo>(validacao.Erros);

        var visivel = await ParcelaVisivel(formaturaId, usuarioId, parcelaId, [], ct);
        if (visivel.Falhou)
            return visivel;

        var parcela = visivel.Valor;

        if (parcela.Status == StatusDaParcela.Paga)
            return ParcelaPaga;

        if (!parcela.EmAberto)
            return ParcelaNaoAberta;

        if (parcela.EmConferencia)
            return Erro.Conflito(
                "pagamento.informe_pendente",
                "Você já avisou este pagamento. A tesouraria vai conferir, e você recebe um e-mail quando for confirmado."
            );

        var arquivo = await EnviarComprovante(comprovante, usuarioId, ct);
        if (arquivo.Falhou)
            return Result.Falha<ParcelaResumo>(arquivo.Erros);

        var informe = InformeDePagamento.Novo(parcela.Id, parcela.VinculoId, dados.PagoEm, dados.ValorEmCentavos, arquivo.Valor);
        await informeRepository.Adicionar(informe, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Informe {InformeId} da parcela {ParcelaId} aguardando conferência.", informe.Id, parcela.Id);

        return parcela with
        {
            EmConferencia = true,
        };
    }

    /// <inheritdoc />
    /// <remarks>O devido é o valor da parcela no dia que o formando informou — é com ele que a divergência se mede.</remarks>
    public async Task<Result<PaginaDe<InformeNaFila>>> ListarInformes(
        PaginacaoRequest paginacao,
        FiltroDeInformes filtro,
        CancellationToken ct = default
    )
    {
        var hoje = DataUtils.Hoje();
        var desde = filtro.ConferidosHoje ? DataUtils.InicioDoDiaEmUtc(hoje) : (DateTime?)null;
        var pagina = await informeRepository.Listar(paginacao.Normalizar(), filtro, hoje, desde, ct);

        if (pagina.Itens.Count == 0)
            return pagina;

        var regras = await parcelaRepository.ObterRegrasDeAtraso([.. pagina.Itens.Select(i => i.Parcela.VinculoId).Distinct()], ct);

        return pagina with
        {
            Itens =
            [
                .. pagina.Itens.Select(informe =>
                {
                    var aceitas = regras.GetValueOrDefault(informe.Parcela.VinculoId, RegrasDeAtraso.Nenhuma);
                    var parcela = informe.Parcela;

                    return informe with
                    {
                        Parcela = parcela.ComValorDoDia(hoje, aceitas),
                        DevidoEmCentavos = ValorDoDia
                            .Calcular(parcela.ValorOriginalEmCentavos, parcela.Vencimento, informe.PagoEm, aceitas)
                            .TotalEmCentavos,
                    };
                }),
            ],
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pede o arquivo ao módulo de arquivos <b>como o formando que o enviou</b>: lá a regra é "cada um vê os
    /// próprios", e quem pode ver o comprovante é decisão daqui — a política de Tesouraria já passou, e o
    /// informe foi achado nesta turma. O mesmo caminho da foto do formando.
    /// </remarks>
    public async Task<Result<ArquivoParaDownload>> BaixarComprovante(Guid informeId, CancellationToken ct = default)
    {
        var comprovante = await informeRepository.ObterComprovante(informeId, ct);
        if (comprovante is null)
            return Erro.NaoEncontrado("pagamento.sem_comprovante", "Este aviso de pagamento não tem comprovante.");

        return await arquivoService.Baixar(
            comprovante.ArquivoId,
            new SolicitanteDeArquivo(comprovante.EnviadoPorUsuarioId, EhAdministrador: false),
            ct
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uma transação para o lote (decisão 4): as parcelas são travadas antes de os informes serem lidos,
    /// então a segunda confirmação simultânea espera a primeira e encontra o informe já confirmado — ignora,
    /// em vez de baixar duas vezes. Informe de outra turma derruba o lote com 404: a lista veio da tela, e
    /// um id que não é daqui é erro de quem montou o pedido.
    /// </remarks>
    public async Task<Result<ResultadoDaConferencia>> Confirmar(
        Guid formaturaId,
        Guid usuarioId,
        string? enderecoIp,
        ConfirmarInformes lote,
        CancellationToken ct = default
    )
    {
        var validacao = loteValidator.Validar(lote);
        if (validacao.Falhou)
            return Result.Falha<ResultadoDaConferencia>(validacao.Erros);

        var ids = lote.Itens.Select(item => item.InformeId).ToList();
        var nomeDaTurma = await NomeDaTurma(formaturaId, ct);

        var resultado = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var parcelas = (await parcelaRepository.TravarParaBaixa(await informeRepository.ListarParcelas(ids, token), token)).ToDictionary(p =>
                    p.Id
                );
                var informes = (await informeRepository.ListarParaEdicao(ids, token)).ToDictionary(i => i.Id);

                if (informes.Count != ids.Count)
                    return Result.Falha<ResultadoDaConferencia>(InformeNaoEncontrado);

                var vinculos = informes.Values.Select(i => i.VinculoId).Distinct().ToList();
                var regras = await parcelaRepository.ObterRegrasDeAtraso(vinculos, token);
                var enderecos = await vinculoRepository.ListarEmailsDosVinculos(vinculos, token);
                var agora = DateTime.UtcNow;
                var confirmados = 0;

                foreach (var item in lote.Itens)
                {
                    var informe = informes[item.InformeId];

                    if (informe.Status != StatusDoInforme.Pendente || !parcelas.TryGetValue(informe.ParcelaId, out var parcela))
                        continue;

                    var baixa = await baixaService.Baixar(
                        parcela,
                        new DadosDaBaixa(FormaDePagamento.Pix, informe.PagoEm, item.ValorRecebidoEmCentavos, null, usuarioId, enderecoIp, agora),
                        informe,
                        new ContextoDaBaixa(
                            formaturaId,
                            nomeDaTurma,
                            regras.GetValueOrDefault(informe.VinculoId, RegrasDeAtraso.Nenhuma),
                            enderecos.GetValueOrDefault(informe.VinculoId)
                        ),
                        token
                    );

                    if (baixa.Falhou)
                        return Result.Falha<ResultadoDaConferencia>(baixa.Erros);

                    if (baixa.Valor)
                        confirmados++;
                }

                return Result.Ok(new ResultadoDaConferencia(confirmados, ids.Count - confirmados));
            },
            ct
        );

        if (resultado.Sucesso)
            logger.LogInformation(
                "Lote conferido por {UsuarioId}: {Confirmados} confirmados, {Ignorados} ignorados.",
                usuarioId,
                resultado.Valor.Confirmados,
                resultado.Valor.Ignorados
            );

        return resultado;
    }

    /// <inheritdoc />
    public async Task<Result> Recusar(Guid formaturaId, Guid usuarioId, Guid informeId, RecusarInforme dados, CancellationToken ct = default)
    {
        var validacao = recusaValidator.Validar(dados);
        if (validacao.Falhou)
            return validacao;

        var informe = (await informeRepository.ListarParaEdicao([informeId], ct)).SingleOrDefault();
        if (informe is null)
            return Result.Falha(InformeNaoEncontrado);

        var recusa = informe.Recusar(usuarioId, dados.Motivo, DateTime.UtcNow);
        if (recusa.Falhou)
            return recusa;

        var parcela = await parcelaRepository.Obter(informe.ParcelaId, DataUtils.Hoje(), ct);
        var enderecos = await vinculoRepository.ListarEmailsDosVinculos([informe.VinculoId], ct);

        if (parcela is not null && enderecos.TryGetValue(informe.VinculoId, out var email))
            await emails.Recusado(
                email,
                await NomeDaTurma(formaturaId, ct),
                parcela.Vencimento,
                informe.ValorEmCentavos,
                informe.MotivoDaRecusa!,
                ct
            );

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Informe {InformeId} recusado por {UsuarioId}.", informe.Id, usuarioId);

        return Result.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Parcela com aviso pendente não baixa à mão: a baixa deixaria o aviso na fila, e a conferência
    /// seguinte o confirmaria sobre uma parcela já paga. A tesouraria confirma ou recusa o aviso.
    /// </remarks>
    public async Task<Result<ParcelaResumo>> BaixarManualmente(
        Guid formaturaId,
        Guid usuarioId,
        string? enderecoIp,
        Guid parcelaId,
        BaixaManual dados,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    )
    {
        var validacao = baixaValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ParcelaResumo>(validacao.Erros);

        var parcela = await parcelaRepository.Obter(parcelaId, DataUtils.Hoje(), ct);
        if (parcela is null)
            return ParcelaNaoEncontrada;

        if (parcela.Status == StatusDaParcela.Paga)
            return Erro.Conflito("pagamento.parcela_paga", "Esta parcela já está paga.");

        if (!parcela.EmAberto)
            return ParcelaNaoAberta;

        if (parcela.EmConferencia)
            return Erro.Conflito(
                "pagamento.informe_pendente",
                "O formando já avisou o pagamento desta parcela. Confirme ou recuse o aviso na Conferência."
            );

        var arquivo = await EnviarComprovante(comprovante, usuarioId, ct);
        if (arquivo.Falhou)
            return Result.Falha<ParcelaResumo>(arquivo.Erros);

        var nomeDaTurma = await NomeDaTurma(formaturaId, ct);

        var baixa = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var travada = (await parcelaRepository.TravarParaBaixa([parcelaId], token)).Single();
                var regras = await parcelaRepository.ObterRegrasDeAtraso([travada.VinculoId], token);
                var enderecos = await vinculoRepository.ListarEmailsDosVinculos([travada.VinculoId], token);

                var baixou = await baixaService.Baixar(
                    travada,
                    new DadosDaBaixa(dados.Forma, dados.PagoEm, dados.ValorEmCentavos, arquivo.Valor, usuarioId, enderecoIp, DateTime.UtcNow),
                    null,
                    new ContextoDaBaixa(
                        formaturaId,
                        nomeDaTurma,
                        regras.GetValueOrDefault(travada.VinculoId, RegrasDeAtraso.Nenhuma),
                        enderecos.GetValueOrDefault(travada.VinculoId)
                    ),
                    token
                );

                if (baixou.Falhou)
                    return Result.Falha(baixou.Erros);

                return baixou.Valor ? Result.Ok() : Result.Falha(Erro.Conflito("pagamento.parcela_paga", "Esta parcela já está paga."));
            },
            ct
        );

        if (baixa.Falhou)
            return Result.Falha<ParcelaResumo>(baixa.Erros);

        logger.LogWarning("Parcela {ParcelaId} baixada manualmente por {UsuarioId}.", parcelaId, usuarioId);

        return await Reler(parcelaId, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Não apaga nada: o recebimento fica, marcado como estornado, e o evento do estorno se soma ao da baixa
    /// na auditoria (decisão 6). O informe confirmado também fica como está — é o histórico do que o
    /// formando disse; se ele pagar de novo, avisa de novo.
    /// <para>
    /// O formando é avisado por e-mail, com a justificativa: a parcela dele volta a ser devida, e
    /// descobrir isso sozinho no extrato é o que vira ligação para a comissão.
    /// </para>
    /// </remarks>
    public async Task<Result<ParcelaResumo>> Estornar(
        Guid formaturaId,
        Guid usuarioId,
        string? enderecoIp,
        Guid parcelaId,
        EstornarBaixa dados,
        CancellationToken ct = default
    )
    {
        var validacao = estornoValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ParcelaResumo>(validacao.Erros);

        var nomeDaTurma = await NomeDaTurma(formaturaId, ct);

        var estorno = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var parcela = (await parcelaRepository.TravarParaBaixa([parcelaId], token)).SingleOrDefault();
                if (parcela is null)
                    return Result.Falha(ParcelaNaoEncontrada);

                var recebimento = await recebimentoRepository.ObterAtivoParaEdicao(parcelaId, token);
                var desfeita = parcela.Estornar();

                if (recebimento is null || desfeita.Falhou)
                    return Result.Falha(Erro.Conflito("pagamento.parcela_nao_paga", "Esta parcela não tem baixa para estornar."));

                recebimento.Estornar(usuarioId, dados.Justificativa, DateTime.UtcNow);

                await eventos.Auditar(
                    EventoDeEstorno,
                    usuarioId,
                    new
                    {
                        formaturaId,
                        parcelaId,
                        recebimentoId = recebimento.Id,
                        valorEmCentavos = recebimento.ValorEmCentavos,
                        justificativa = recebimento.JustificativaDoEstorno,
                        enderecoIp,
                    },
                    token
                );

                var enderecos = await vinculoRepository.ListarEmailsDosVinculos([parcela.VinculoId], token);

                if (enderecos.TryGetValue(parcela.VinculoId, out var email))
                    await emails.Estornado(
                        email,
                        nomeDaTurma,
                        parcela.Vencimento,
                        recebimento.ValorEmCentavos,
                        recebimento.JustificativaDoEstorno!,
                        token
                    );

                return Result.Ok();
            },
            ct
        );

        if (estorno.Falhou)
            return Result.Falha<ParcelaResumo>(estorno.Erros);

        logger.LogWarning("Baixa da parcela {ParcelaId} estornada por {UsuarioId}.", parcelaId, usuarioId);

        return await Reler(parcelaId, ct);
    }

    /// <inheritdoc />
    public async Task<Result<PaginaDe<Divergencia>>> ListarDivergencias(
        PaginacaoRequest paginacao,
        string? busca = null,
        CancellationToken ct = default
    ) => Result.Ok(await recebimentoRepository.ListarDivergencias(paginacao.Normalizar(), DataUtils.Hoje(), busca, ct));

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

        if (membro is null || parcela is null || (parcela.VinculoId != membro.VinculoId && !papeisQueVeem.Contains(membro.Papel)))
            return ParcelaNaoEncontrada;

        return (await parcelaRepository.ComValorDoDia([parcela], hoje, ct))[0];
    }

    /// <summary>A parcela depois da escrita, com o valor do dia.</summary>
    private async Task<Result<ParcelaResumo>> Reler(Guid parcelaId, CancellationToken ct)
    {
        var hoje = DataUtils.Hoje();
        var parcela = await parcelaRepository.Obter(parcelaId, hoje, ct);

        return parcela is null ? ParcelaNaoEncontrada : (await parcelaRepository.ComValorDoDia([parcela], hoje, ct))[0];
    }

    /// <summary>Grava o comprovante, se veio; devolve o id do arquivo, ou nulo.</summary>
    private async Task<Result<Guid?>> EnviarComprovante(NovoArquivo? comprovante, Guid usuarioId, CancellationToken ct)
    {
        if (comprovante is null)
            return Result.Ok<Guid?>(null);

        if (!ExtensoesDoComprovante.Contains(Path.GetExtension(comprovante.Nome)))
            return Erro.Validacao("pagamento.comprovante_invalido", "Envie o comprovante em PDF ou imagem (PNG, JPG ou WebP).", "comprovante");

        var arquivo = await arquivoService.Enviar(comprovante with { Categoria = CategoriaDoComprovante }, usuarioId, ct);

        return arquivo.Falhou ? Result.Falha<Guid?>(arquivo.Erros) : arquivo.Valor.Id;
    }

    private async Task<string> NomeDaTurma(Guid formaturaId, CancellationToken ct) =>
        (await formaturaRepository.ObterDetalhe(formaturaId, ct))?.Nome ?? string.Empty;
}
