using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Emails.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.Notificacoes.Services;

/// <summary>
/// A régua rodando numa turma: seleciona, agrupa, enfileira e registra — nesta ordem, uma vez por dia.
/// </summary>
/// <remarks>
/// As três barreiras que a sprint pede, todas aqui e nenhuma na tela:
/// <list type="bullet">
/// <item><b>Não cobra quem pagou</b> — a consulta já exclui a parcela paga e a que tem informe
/// pendente esperando a tesouraria (decisão 1).</item>
/// <item><b>Não manda duas vezes</b> — o que já foi disparado hoje é descontado antes de montar a
/// mensagem, e o índice único <c>(parcela, regra, dia, destinatário)</c> é a última barreira
/// (decisão 2).</item>
/// <item><b>Uma mensagem por pessoa por dia</b> — as parcelas de um formando viram um texto só, com
/// o degrau mais severo entre os que a alcançaram (decisão 3).</item>
/// </list>
/// <para>
/// Fora da janela de 9h–20h em dia útil, a rodada devolve nada sem tocar em coisa alguma (decisão 4).
/// Os dias que caíram no fim de semana são cobertos na segunda, por <c>JanelaDeEnvio.DiasRepresados</c>.
/// </para>
/// </remarks>
/// <param name="notificacoes">Régua, histórico e seleção de parcelas.</param>
/// <param name="parcelas">Regras de atraso aceitas na adesão de cada formando.</param>
/// <param name="vinculos">Quem é a tesouraria da turma.</param>
/// <param name="canal">Por onde a mensagem sai.</param>
/// <param name="assinaturas">O plano de cada turma: a régua é do módulo Avisos.</param>
/// <param name="aplicacao">Identidade da aplicação, para os links das mensagens.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ReguaService(
    INotificacaoRepository notificacoes,
    IParcelaRepository parcelas,
    IVinculoRepository vinculos,
    ICanalDeNotificacao canal,
    IAssinaturaRepository assinaturas,
    IOptions<AplicacaoSettings> aplicacao,
    IUnitOfWork unitOfWork,
    ILogger<ReguaService> logger
) : IReguaService
{
    /// <summary>Entregas conferidas por rodada. A fila de e-mail é esvaziada pelo worker a cada 30 segundos.</summary>
    private const int LoteDaConferencia = 200;

    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    /// <inheritdoc />
    /// <remarks>
    /// Só a turma cujo plano inclui <c>avisos</c>: a régua é diferencial do Premium, e até 22/09/2026
    /// o gate valia só para as telas — o envio automático saía para toda turma ativa, gratuito incluso.
    /// <c>ponytail:</c> uma consulta de plano por turma, uma vez por rodada; vira junção no SQL se o
    /// número de turmas pesar.
    /// </remarks>
    public async Task<IReadOnlyList<FormaturaParaRegua>> ListarFormaturas(CancellationToken ct = default)
    {
        var comAvisos = new List<FormaturaParaRegua>();

        foreach (var formatura in await notificacoes.ListarFormaturasAtivasDeTodasAsFormaturas(ct))
        {
            if (await assinaturas.ObterPlanoVigenteDeTodasAsFormaturas(formatura.Id, ct) is { } plano && plano.Modulos.Contains(Modulo.Avisos))
                comAvisos.Add(formatura);
        }

        return comAvisos;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A conferência de entregas roda fora da janela também: ela não fala com ninguém, só lê o
    /// desfecho da fila de e-mail. Presa atrás da janela, o histórico ficaria a noite inteira em
    /// "Na fila" — e é de madrugada que a tesouraria abre a tela para entender o que aconteceu.
    /// </remarks>
    public async Task<ResumoDaRodada> Executar(FormaturaParaRegua formatura, DateTime agoraUtc, CancellationToken ct = default)
    {
        var conferidas = await ConferirEntregas(ct);

        if (!JanelaDeEnvio.Aberta(agoraUtc))
            return ResumoDaRodada.Nenhuma with { Conferidas = conferidas };

        var hoje = JanelaDeEnvio.Hoje(agoraUtc);
        var regras = await ReguaDaTurma.Garantir(notificacoes, unitOfWork, ct);

        var alcancadas = await Selecionar(regras, hoje, ct);
        var enviados = await Enfileirar(formatura, regras, alcancadas, hoje, ct);

        return new ResumoDaRodada(enviados, alcancadas.Count, conferidas);
    }

    /// <summary>
    /// As parcelas que os degraus de vencimento alcançam hoje, já sem as que a régua não pode cobrar.
    /// </summary>
    /// <remarks>A mesma parcela pode ser alcançada por um degrau só: dois deslocamentos diferentes dão dois dias diferentes.</remarks>
    private async Task<IReadOnlyList<(RegraResumo Regra, ParcelaParaCobranca Parcela)>> Selecionar(
        IReadOnlyList<RegraResumo> regras,
        DateOnly hoje,
        CancellationToken ct
    )
    {
        var jaEnviadas = (await notificacoes.ListarChavesDoDia(hoje, ct)).ToHashSet();
        var invalidos = (await notificacoes.ListarDestinatariosInvalidos(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<(RegraResumo, ParcelaParaCobranca)> alcancadas = [];

        foreach (var dia in JanelaDeEnvio.DiasRepresados(hoje))
        foreach (var regra in regras.Where(r => r.Ativa && r.Gatilho is GatilhoDaRegua.Vencimento))
        {
            var encontradas = await notificacoes.ListarParaCobranca(dia.AddDays(-regra.DiasDeDeslocamento), ct);

            alcancadas.AddRange(
                encontradas
                    .Where(parcela => !invalidos.Contains(parcela.Email))
                    .Where(parcela => !jaEnviadas.Contains(new ChaveDeEnvio(regra.Id, parcela.ParcelaId)))
                    .Select(parcela => (regra, parcela))
            );
        }

        return alcancadas;
    }

    /// <summary>Monta e enfileira: uma mensagem por formando, mais os resumos da tesouraria.</summary>
    private async Task<int> Enfileirar(
        FormaturaParaRegua formatura,
        IReadOnlyList<RegraResumo> regras,
        IReadOnlyList<(RegraResumo Regra, ParcelaParaCobranca Parcela)> alcancadas,
        DateOnly hoje,
        CancellationToken ct
    )
    {
        List<NotificacaoEnviada> envios = [];

        envios.AddRange(await CobrarFormandos(formatura, alcancadas, hoje, ct));
        envios.AddRange(await AvisarTesouraria(formatura, regras, alcancadas, hoje, ct));

        if (envios.Count == 0)
            return 0;

        await notificacoes.AdicionarEnvios(envios, ct);
        await unitOfWork.SalvarAsync(ct);

        return envios.Count;
    }

    /// <summary>Uma mensagem por formando, com todas as parcelas do dia e o degrau mais severo.</summary>
    private async Task<IReadOnlyList<NotificacaoEnviada>> CobrarFormandos(
        FormaturaParaRegua formatura,
        IReadOnlyList<(RegraResumo Regra, ParcelaParaCobranca Parcela)> alcancadas,
        DateOnly hoje,
        CancellationToken ct
    )
    {
        if (alcancadas.Count == 0)
            return [];

        var regrasDeAtraso = await parcelas.ObterRegrasDeAtraso([.. alcancadas.Select(a => a.Parcela.VinculoId).Distinct()], ct);
        var link = MontagemDaMensagem.LinkDoExtrato(_aplicacao);

        List<NotificacaoEnviada> envios = [];

        foreach (var pessoa in alcancadas.GroupBy(a => a.Parcela.VinculoId))
        {
            var atraso = regrasDeAtraso.GetValueOrDefault(pessoa.Key, RegrasDeAtraso.Nenhuma);
            var itens = pessoa
                .Select(a => new ParcelaNaMensagem(
                    a.Parcela,
                    ValorDoDia.Calcular(a.Parcela.ValorOriginalEmCentavos, a.Parcela.Vencimento, hoje, atraso, a.Parcela.JaPagoEmCentavos)
                ))
                .ToList();

            var severa = pessoa.MaxBy(a => a.Regra.DiasDeDeslocamento)!.Regra;
            var destinatario = itens[0].Parcela;

            var mensagem = MontagemDaMensagem.Cobranca(severa, destinatario.Nome, destinatario.Email, formatura.Nome, itens, link);
            var entrega = await Entregar(mensagem, ct);

            if (entrega is null)
                continue;

            envios.AddRange(
                pessoa.Select(a =>
                    NotificacaoEnviada.Nova(
                        a.Regra.Id,
                        hoje,
                        destinatario.Email,
                        mensagem.Assunto,
                        a.Parcela.ParcelaId,
                        pessoa.Key,
                        entrega.ReferenciaExterna
                    )
                )
            );
        }

        return envios;
    }

    /// <summary>
    /// Os resumos que vão à tesouraria: o dos degraus que a avisam (D+15 e D+30) e o do informe parado.
    /// </summary>
    /// <remarks>
    /// O do informe parado sai no dia em que algum aviso completa o prazo, e não em todo dia em que haja
    /// algum parado (07/10/2026): a fila continua à vista na tela de conferência.
    /// <para>
    /// Um resumo, e não uma cópia por parcela: a caixa de entrada de quem confere é a primeira a ser
    /// ignorada quando enche, e é justamente ela que a sprint quer desbloquear.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<NotificacaoEnviada>> AvisarTesouraria(
        FormaturaParaRegua formatura,
        IReadOnlyList<RegraResumo> regras,
        IReadOnlyList<(RegraResumo Regra, ParcelaParaCobranca Parcela)> alcancadas,
        DateOnly hoje,
        CancellationToken ct
    )
    {
        var pendentes = regras.FirstOrDefault(r => r.Ativa && r.Gatilho is GatilhoDaRegua.InformePendente);
        var marcadas = regras.Where(r => r.Ativa && r.Degrau.ResumoDaTesouraria is not null).ToList();

        if (pendentes is null && marcadas.Count == 0)
            return [];

        var destinatarios = await vinculos.ListarEmailsDaTesouraria(formatura.Id, ct);
        if (destinatarios.Count == 0)
            return [];

        var jaEnviadas = (await notificacoes.ListarChavesDoDia(hoje, ct)).ToHashSet();
        var invalidos = (await notificacoes.ListarDestinatariosInvalidos(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validos = destinatarios.Where(email => !invalidos.Contains(email)).ToList();

        List<NotificacaoEnviada> envios = [];

        foreach (var regra in marcadas)
        {
            var quantidade = alcancadas.Count(a => a.Regra.Id == regra.Id);

            if (quantidade > 0)
                envios.AddRange(
                    await Resumir(
                        regra,
                        regra.Degrau.ResumoDaTesouraria!,
                        formatura,
                        quantidade,
                        MontagemDaMensagem.LinkDoExtrato(_aplicacao),
                        "Ver as parcelas",
                        validos,
                        jaEnviadas,
                        hoje,
                        ct
                    )
                );
        }

        if (pendentes is not null)
        {
            var parados = await notificacoes.ContarInformesPendentesAte(hoje.AddDays(-pendentes.DiasDeDeslocamento), ct);
            var jaAvisados = await notificacoes.ContarInformesPendentesAte(
                JanelaDeEnvio.DiasRepresados(hoje).Min().AddDays(-pendentes.DiasDeDeslocamento - 1),
                ct
            );

            // Só quando algum aviso cruzou o prazo nesta rodada: repetir todo dia o mesmo resumo era um e-mail
            // diário por tesoureiro enquanto a conferência não andasse. O total vai na mensagem do mesmo jeito.
            if (parados > jaAvisados)
                envios.AddRange(
                    await Resumir(
                        pendentes,
                        pendentes.Degrau.Texto,
                        formatura,
                        parados,
                        MontagemDaMensagem.LinkDaConferencia(_aplicacao),
                        "Abrir a conferência",
                        validos,
                        jaEnviadas,
                        hoje,
                        ct
                    )
                );
        }

        return envios;
    }

    private async Task<IReadOnlyList<NotificacaoEnviada>> Resumir(
        RegraResumo regra,
        TextoDaMensagem texto,
        FormaturaParaRegua formatura,
        int quantidade,
        string link,
        string textoDoLink,
        IReadOnlyList<string> destinatarios,
        HashSet<ChaveDeEnvio> jaEnviadas,
        DateOnly hoje,
        CancellationToken ct
    )
    {
        if (jaEnviadas.Contains(new ChaveDeEnvio(regra.Id, null)))
            return [];

        List<NotificacaoEnviada> envios = [];

        foreach (var email in destinatarios)
        {
            var mensagem = MontagemDaMensagem.Resumo(texto, email, formatura.Nome, quantidade, link, textoDoLink);
            var entrega = await Entregar(mensagem, ct);

            if (entrega is not null)
                envios.Add(NotificacaoEnviada.Nova(regra.Id, hoje, email, mensagem.Assunto, emailNaFilaId: entrega.ReferenciaExterna));
        }

        return envios;
    }

    /// <summary>
    /// Entrega a mensagem. Recusa do canal não vira mensagem engolida.
    /// </summary>
    /// <remarks>
    /// Nulo é "não saiu, e está no log": a rodada segue para a próxima pessoa em vez de abortar a
    /// turma inteira por causa de um endereço.
    /// </remarks>
    /// <param name="mensagem">Mensagem pronta.</param>
    private async Task<EntregaDaMensagem?> Entregar(MensagemDeNotificacao mensagem, CancellationToken ct)
    {
        var enviado = await canal.Enviar(mensagem, ct);

        if (enviado.Sucesso)
            return enviado.Valor;

        logger.LogWarning("O canal recusou a mensagem: {Codigo}.", enviado.PrimeiroErro.Codigo);

        return null;
    }

    /// <summary>
    /// Fecha o histórico com o desfecho da fila de e-mail — e marca o contato inválido.
    /// </summary>
    /// <remarks>
    /// Critério de aceite: falha permanente marca o contato como inválido e para de tentar. "Inválido"
    /// não é uma coluna nova: é o próprio histórico — <c>ListarDestinatariosInvalidos</c> devolve quem
    /// já falhou, e a seleção da rodada seguinte o pula. A comissão vê no histórico, com o motivo.
    /// </remarks>
    private async Task<int> ConferirEntregas(CancellationToken ct)
    {
        var pendentes = await notificacoes.ListarEntregasAConferir(LoteDaConferencia, ct);

        if (pendentes.Count == 0)
            return 0;

        foreach (var pendente in pendentes)
        {
            if (pendente.Status is EEmailStatus.Enviado)
                pendente.Notificacao.MarcarEntregue();
            else
                pendente.Notificacao.MarcarFalha(pendente.Erro);
        }

        await unitOfWork.SalvarAsync(ct);

        return pendentes.Count;
    }
}
