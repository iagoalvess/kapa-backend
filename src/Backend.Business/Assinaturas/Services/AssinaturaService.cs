using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Settings;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Models;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Backend.Business.Assinaturas.Services;

/// <summary>
/// Catálogo de planos e o lado da comissão na assinatura.
/// </summary>
/// <param name="assinaturaRepository">Planos e assinaturas.</param>
/// <param name="formaturaRepository">A formatura, que muda de status no checkout.</param>
/// <param name="provedor">PSP que cobra a licença.</param>
/// <param name="vagas">Vagas ocupadas, para não vender plano menor que a turma.</param>
/// <param name="checkoutValidator">Forma do pedido de checkout.</param>
/// <param name="trocaValidator">Forma do pedido de troca de plano.</param>
/// <param name="settings">Configuração da assinatura.</param>
/// <param name="aplicacao">Endereço do front, para a URL de retorno.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class AssinaturaService(
    IAssinaturaRepository assinaturaRepository,
    IFormaturaRepository formaturaRepository,
    IProvedorDeAssinatura provedor,
    VagasDoPlano vagas,
    IValidator<IniciarCheckout> checkoutValidator,
    IValidator<TrocaDePlano> trocaValidator,
    IOptions<AssinaturaSettings> settings,
    IOptions<AplicacaoSettings> aplicacao,
    IUnitOfWork unitOfWork
) : IAssinaturaService
{
    private static readonly Erro NaoEncontrada = Erro.NaoEncontrado("assinatura.nao_encontrada", "Esta formatura ainda não contratou um plano.");

    private static readonly Erro NaoAtiva = Erro.Conflito("assinatura.nao_ativa", "Só uma assinatura ativa muda de plano ou de meio de pagamento.");

    /// <summary>
    /// Piso da diferença cobrada na subida de plano: faltando horas para a renovação, a conta dá centavos que não
    /// cobrem nem a tarifa do meio. <c>ponytail:</c> valor fixo; parametrizar se o preço dos planos mudar de escala.
    /// </summary>
    public const long DiferencaMinimaEmCentavos = 100;

    private string UrlDeRetorno => aplicacao.Value.Link(settings.Value.CaminhoDeRetorno);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PlanoResumo>>> ListarPlanos(CancellationToken ct = default) =>
        Result.Ok(await assinaturaRepository.ListarPlanosAtivos(ct));

    /// <inheritdoc />
    /// <remarks>Sem nem o gratuito no catálogo, o banco está mal semeado: 404, e não uma turma sem plano nenhum.</remarks>
    public async Task<Result<PlanoDaTurma>> ObterPlanoDaTurma(Guid formaturaId, CancellationToken ct = default) =>
        await assinaturaRepository.ObterPlanoDosModulosDeTodasAsFormaturas(formaturaId, ct) is { } plano
            ? new PlanoDaTurma(plano.Codigo, plano.Nome, plano.Modulos, plano.Pago)
            : Erro.NaoEncontrado("plano.nao_encontrado", "O catálogo não tem o plano desta turma.");

    /// <inheritdoc />
    public async Task<Result<AssinaturaDetalhe>> ObterAtual(CancellationToken ct = default) =>
        await assinaturaRepository.ObterDetalheDaMaisRecente(ct) is { } detalhe ? detalhe : NaoEncontrada;

    /// <inheritdoc />
    /// <remarks>
    /// A sessão é criada no provedor <b>antes</b> de gravar: provedor fora do ar devolve 503 e nada
    /// muda — a formatura continua em rascunho, sem assinatura órfã.
    /// <para>
    /// Uma pendente por formatura. Clicar de novo em "contratar" (fechou a aba, voltou pelo histórico)
    /// retoma a pendente com uma sessão nova, em vez de empilhar assinaturas. A referência enviada ao
    /// provedor é o id da assinatura, então o pagamento de qualquer uma das sessões encontra a mesma
    /// linha. O índice único parcial em <c>AssinaturaMapping</c> fecha a corrida do clique duplo.
    /// </para>
    /// <para>
    /// <b>Toda sessão nova invalida a anterior no provedor.</b> Como o pagamento de qualquer sessão
    /// confirma a mesma linha, pagar a sessão antiga do Essencial depois de pedir o Premium ativaria o
    /// Premium pelo preço do Essencial. Até 30/09/2026 o mesmo plano não cancelava ("qualquer sessão
    /// cobra o mesmo valor"): duas sessões do Essencial seguidas de uma do Premium deixavam a primeira
    /// viva — só a última é a <c>IdExterno</c> —, e pagar duas sessões cobrava a turma em dobro.
    /// </para>
    /// <para>
    /// Formatura suspensa contrata sem sair de <c>Suspensa</c>: ela continua em modo leitura até o
    /// pagamento confirmar — é o webhook que a reativa.
    /// </para>
    /// <para>
    /// <b>"Já tem plano" é pergunta da assinatura, não do status.</b> Até 18/09/2026 bastava olhar se
    /// a turma era <c>Ativa</c>, porque só chegava lá quem tinha pago. Com o gratuito, <b>toda</b>
    /// turma é ativa — o mesmo teste recusaria todo checkout e trancaria o upgrade, que é justamente
    /// o que o gratuito existe para provocar.
    /// </para>
    /// <para>
    /// <b>Plano menor que a turma é recusado</b> (22/09/2026): sem isso, a turma de 200 no Premium
    /// deixava vencer e contratava o Essencial (50) com os 200 dentro. Igual ao limite passa — a
    /// turma não cresce, mas também não precisa encolher.
    /// </para>
    /// </remarks>
    public async Task<Result<SessaoDeCheckout>> IniciarCheckout(Guid formaturaId, IniciarCheckout dados, CancellationToken ct = default)
    {
        var validacao = checkoutValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<SessaoDeCheckout>(validacao.Erros);

        var formatura = await formaturaRepository.ObterParaEdicao(formaturaId, ct);

        if (formatura is null)
            return ErrosDeFormatura.FormaturaNaoEncontrada;

        if (formatura.Status is StatusDaFormatura.Encerrada or StatusDaFormatura.Descartada)
            return Erro.Conflito("formatura.encerrada", "Esta turma não contrata assinatura.");

        var maisRecente = await assinaturaRepository.ObterMaisRecenteParaEdicao(ct);

        if (maisRecente is { Status: StatusDaAssinatura.Ativa })
            return Erro.Conflito("assinatura.ja_ativa", "Esta formatura já tem uma assinatura ativa.");

        var plano = await assinaturaRepository.ObterPlanoAtivo(dados.PlanoCodigo.Trim(), ct);

        if (plano is null)
            return Erro.Validacao("assinatura.plano_invalido", "Plano não encontrado.", campo: "plano_codigo");

        var ocupadas = await vagas.Ocupadas(formaturaId, ct);

        if (plano.LimiteDeFormandos >= 0 && ocupadas > plano.LimiteDeFormandos)
            return Erro.Conflito(
                "assinatura.plano_menor_que_a_turma",
                $"A turma já tem {ocupadas} pessoas e o plano {plano.Nome} comporta {plano.LimiteDeFormandos}. Escolha um plano maior."
            );

        var pendente = maisRecente is { Status: StatusDaAssinatura.Pendente } atual ? atual : null;
        var meio = dados.Meio ?? MeioDePagamento.Cartao;

        if (pendente is { IdExterno: { } sessaoAnterior })
        {
            var invalidada = await provedor.Cancelar(sessaoAnterior, ct);
            if (invalidada.Falhou)
                return Result.Falha<SessaoDeCheckout>(invalidada.Erros);
        }

        var assinatura = pendente ?? new Assinatura();
        assinatura.PlanoId = plano.Id;
        assinatura.Meio = meio;

        if (pendente is not null)
            (await assinaturaRepository.ObterCobrancaAbertaParaEdicao(pendente.Id, MotivoDaCobranca.Ciclo, ct))?.Cancelar();

        var cobranca =
            meio == MeioDePagamento.Pix
                ? CobrancaDaAssinatura.Abrir(assinatura.Id, plano.Id, MotivoDaCobranca.Ciclo, meio, plano.PrecoEmCentavos)
                : null;

        var sessao = await provedor.CriarCheckout(
            new PedidoDeCheckout(
                assinatura.Id,
                plano.Codigo,
                plano.Nome,
                plano.PrecoEmCentavos,
                plano.Ciclo,
                UrlDeRetorno,
                meio,
                dados.EmailDoPagador,
                cobranca?.Id
            ),
            ct
        );

        if (sessao.Falhou)
            return sessao;

        assinatura.IdExterno = cobranca is null ? sessao.Valor.IdExterno : null;

        if (pendente is null)
            await assinaturaRepository.Adicionar(assinatura, ct);

        if (cobranca is not null)
        {
            cobranca.Url = sessao.Valor.Url;
            await assinaturaRepository.AdicionarCobranca(cobranca, ct);
        }

        await unitOfWork.SalvarAsync(ct);

        return sessao;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Só entre planos do mesmo ciclo: passar do mensal ao anual no meio do mês pediria um crédito dos dias pagos que o
    /// P4 não previu — a turma cancela a renovação e contrata o outro ciclo no fim da vigência.
    /// <para>
    /// A diferença é o que falta do ciclo pago, na proporção do preço (<see cref="Assinatura.FracaoRestante"/>), com
    /// piso de <see cref="DiferencaMinimaEmCentavos"/>. Ela vai para a página do provedor no meio da turma, e o plano novo
    /// vale só quando o pagamento chegar — é o webhook que sobe o plano. Escolher o plano atual desfaz uma descida agendada.
    /// </para>
    /// </remarks>
    public async Task<Result<ResultadoDaTroca>> TrocarPlano(
        Guid formaturaId,
        string? planoCodigo,
        string? emailDoPagador,
        CancellationToken ct = default
    )
    {
        var validacao = trocaValidator.Validar(new TrocaDePlano(planoCodigo ?? string.Empty));
        if (validacao.Falhou)
            return Result.Falha<ResultadoDaTroca>(validacao.Erros);

        var assinatura = await assinaturaRepository.ObterMaisRecenteParaEdicao(ct);

        if (assinatura is not { Status: StatusDaAssinatura.Ativa })
            return NaoAtiva;

        var novo = await assinaturaRepository.ObterPlanoAtivo(planoCodigo!.Trim(), ct);

        if (novo is null)
            return Erro.Validacao("assinatura.plano_invalido", "Plano não encontrado.", campo: "plano_codigo");

        var atual = await assinaturaRepository.ObterPlano(assinatura.PlanoId, ct) ?? novo;

        if (novo.Ciclo != atual.Ciclo)
            return Erro.Conflito(
                "assinatura.troca_de_ciclo",
                "A troca de plano é no mesmo ciclo. Para mudar entre mensal e anual, cancele a renovação e contrate o outro ciclo quando a vigência acabar."
            );

        var ocupadas = await vagas.Ocupadas(formaturaId, ct);

        if (novo.LimiteDeFormandos >= 0 && ocupadas > novo.LimiteDeFormandos)
            return Erro.Conflito(
                "assinatura.plano_menor_que_a_turma",
                $"A turma já tem {ocupadas} pessoas e o plano {novo.Nome} comporta {novo.LimiteDeFormandos}. Escolha um plano maior."
            );

        if (novo.PrecoEmCentavos <= atual.PrecoEmCentavos)
            return await AgendarDescida(assinatura, novo, ct);

        var valor = Math.Max(
            DiferencaMinimaEmCentavos,
            (long)Math.Round((novo.PrecoEmCentavos - atual.PrecoEmCentavos) * assinatura.FracaoRestante(DateTime.UtcNow, atual.Ciclo))
        );

        (await assinaturaRepository.ObterCobrancaAbertaParaEdicao(assinatura.Id, MotivoDaCobranca.Diferenca, ct))?.Cancelar();

        var cobranca = CobrancaDaAssinatura.Abrir(assinatura.Id, novo.Id, MotivoDaCobranca.Diferenca, assinatura.Meio, valor);

        var sessao = await provedor.CriarCheckout(
            new PedidoDeCheckout(
                assinatura.Id,
                novo.Codigo,
                $"{novo.Nome} (diferença)",
                valor,
                novo.Ciclo,
                UrlDeRetorno,
                assinatura.Meio,
                emailDoPagador,
                cobranca.Id
            ),
            ct
        );

        if (sessao.Falhou)
            return Result.Falha<ResultadoDaTroca>(sessao.Erros);

        cobranca.Url = sessao.Valor.Url;
        await assinaturaRepository.AdicionarCobranca(cobranca, ct);
        await unitOfWork.SalvarAsync(ct);

        return new ResultadoDaTroca(sessao.Valor.Url);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Para o PIX, a recorrência é cancelada agora e o próximo ciclo vira um PIX pela tela. Para o cartão, a recorrência
    /// nova nasce com o primeiro débito no fim da vigência e só vale quando for autorizada — até lá a assinatura segue no
    /// PIX, e o PIX em aberto do próximo ciclo é cancelado para não cobrar duas vezes.
    /// </remarks>
    public async Task<Result<ResultadoDaTroca>> TrocarMeio(MeioDePagamento meio, string? emailDoPagador, CancellationToken ct = default)
    {
        var assinatura = await assinaturaRepository.ObterMaisRecenteParaEdicao(ct);

        if (assinatura is not { Status: StatusDaAssinatura.Ativa })
            return NaoAtiva;

        if (assinatura.Meio == meio && (meio == MeioDePagamento.Cartao || assinatura.IdExterno is null))
            return Erro.Conflito("assinatura.mesmo_meio", "A assinatura já é paga por este meio.");

        if (assinatura.IdExterno is { } recorrenciaAnterior)
        {
            var cancelada = await provedor.Cancelar(recorrenciaAnterior, ct);
            if (cancelada.Falhou)
                return Result.Falha<ResultadoDaTroca>(cancelada.Erros);

            assinatura.IdExterno = null;
        }

        string? url = null;

        if (meio == MeioDePagamento.Cartao)
        {
            var plano = await assinaturaRepository.ObterPlano(assinatura.PlanoDoProximoCicloId ?? assinatura.PlanoId, ct);

            if (plano is null)
                return NaoEncontrada;

            var agora = DateTime.UtcNow;
            var sessao = await provedor.CriarCheckout(
                new PedidoDeCheckout(
                    assinatura.Id,
                    plano.Codigo,
                    plano.Nome,
                    plano.PrecoEmCentavos,
                    plano.Ciclo,
                    UrlDeRetorno,
                    MeioDePagamento.Cartao,
                    emailDoPagador,
                    ComecaEm: assinatura.VigenteAte > agora ? assinatura.VigenteAte : null
                ),
                ct
            );

            if (sessao.Falhou)
                return Result.Falha<ResultadoDaTroca>(sessao.Erros);

            assinatura.IdExterno = sessao.Valor.IdExterno;
            url = sessao.Valor.Url;

            (await assinaturaRepository.ObterCobrancaAbertaParaEdicao(assinatura.Id, MotivoDaCobranca.Ciclo, ct))?.Cancelar();
        }
        else
            assinatura.Meio = MeioDePagamento.Pix;

        await unitOfWork.SalvarAsync(ct);

        return new ResultadoDaTroca(url);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uma cobrança aberta por ciclo: clicar de novo gera outra página para a mesma cobrança — a anterior pode ter
    /// vencido. O plano é o do próximo ciclo, que já leva em conta a descida agendada.
    /// </remarks>
    public async Task<Result<SessaoDeCheckout>> PagarCiclo(string? emailDoPagador, CancellationToken ct = default)
    {
        var assinatura = await assinaturaRepository.ObterMaisRecenteParaEdicao(ct);

        if (assinatura is not { Status: StatusDaAssinatura.Ativa, VigenteAte: { } vigenteAte })
            return NaoAtiva;

        if (assinatura.Meio != MeioDePagamento.Pix)
            return Erro.Conflito("assinatura.renovacao_automatica", "A renovação no cartão é automática: não há PIX a pagar.");

        var abreEm = vigenteAte.AddDays(-Assinatura.MarcosDeAviso.Max());

        if (DateTime.UtcNow < abreEm)
            return Erro.Conflito(
                "assinatura.renovacao_ainda_nao_aberta",
                $"O PIX da renovação fica disponível a partir de {DataUtils.ParaExibicao(abreEm):dd/MM/yyyy}, sete dias antes do vencimento."
            );

        var plano = await assinaturaRepository.ObterPlano(assinatura.PlanoDoProximoCicloId ?? assinatura.PlanoId, ct);

        if (plano is null)
            return NaoEncontrada;

        var cobranca = await assinaturaRepository.ObterCobrancaAbertaParaEdicao(assinatura.Id, MotivoDaCobranca.Ciclo, ct);

        if (cobranca is not null && cobranca.PlanoId != plano.Id)
        {
            cobranca.Cancelar();
            cobranca = null;
        }

        var nova = cobranca is null;
        cobranca ??= CobrancaDaAssinatura.Abrir(assinatura.Id, plano.Id, MotivoDaCobranca.Ciclo, MeioDePagamento.Pix, plano.PrecoEmCentavos);

        var sessao = await provedor.CriarCheckout(
            new PedidoDeCheckout(
                assinatura.Id,
                plano.Codigo,
                plano.Nome,
                cobranca.ValorEmCentavos,
                plano.Ciclo,
                UrlDeRetorno,
                MeioDePagamento.Pix,
                emailDoPagador,
                cobranca.Id
            ),
            ct
        );

        if (sessao.Falhou)
            return sessao;

        cobranca.Url = sessao.Valor.Url;

        if (nova)
            await assinaturaRepository.AdicionarCobranca(cobranca, ct);

        await unitOfWork.SalvarAsync(ct);

        return sessao;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CobrancaDoPlanoResumo>>> ListarCobrancas(CancellationToken ct = default) =>
        Result.Ok(await assinaturaRepository.ListarCobrancas(ct));

    /// <summary>
    /// A descida (P4): vale na próxima renovação. No cartão, a recorrência passa a cobrar o preço novo já — o próximo
    /// débito é o do ciclo que o plano novo paga.
    /// </summary>
    private async Task<Result<ResultadoDaTroca>> AgendarDescida(Assinatura assinatura, Plano novo, CancellationToken ct)
    {
        assinatura.AgendarPlano(novo.Id);

        if (assinatura.IdExterno is { } recorrencia)
        {
            var ajuste = await provedor.AtualizarValor(recorrencia, novo.PrecoEmCentavos, ct);
            if (ajuste.Falhou)
                return Result.Falha<ResultadoDaTroca>(ajuste.Erros);
        }

        (await assinaturaRepository.ObterCobrancaAbertaParaEdicao(assinatura.Id, MotivoDaCobranca.Ciclo, ct))?.Cancelar();

        await unitOfWork.SalvarAsync(ct);

        return new ResultadoDaTroca(null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A regra da entidade vem antes do provedor: não se cancela no PSP o que aqui nem está ativo. Se
    /// a gravação falhar depois de o provedor cancelar, o <c>assinatura.cancelada</c> que ele manda
    /// em seguida acerta o estado.
    /// </remarks>
    public async Task<Result<AssinaturaDetalhe>> Cancelar(CancellationToken ct = default)
    {
        var assinatura = await assinaturaRepository.ObterMaisRecenteParaEdicao(ct);

        if (assinatura is null)
            return NaoEncontrada;

        var cancelamento = assinatura.Cancelar(DateTime.UtcNow);
        if (cancelamento.Falhou)
            return Result.Falha<AssinaturaDetalhe>(cancelamento.Erros);

        if (assinatura.IdExterno is { } idExterno)
        {
            var noProvedor = await provedor.Cancelar(idExterno, ct);
            if (noProvedor.Falhou)
                return Result.Falha<AssinaturaDetalhe>(noProvedor.Erros);
        }

        (await assinaturaRepository.ObterCobrancaAbertaParaEdicao(assinatura.Id, MotivoDaCobranca.Ciclo, ct))?.Cancelar();

        await unitOfWork.SalvarAsync(ct);

        return await ObterAtual(ct);
    }
}
