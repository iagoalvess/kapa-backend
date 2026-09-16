using Backend.Business.Abstractions;
using Backend.Business.Emails.Models;
using Backend.Business.Notificacoes.Models;

namespace Backend.Business.Notificacoes.Interfaces;

/// <summary>
/// Por onde uma mensagem sai.
/// </summary>
/// <remarks>
/// Esta sprint entrega o e-mail, que já tem fila e worker prontos; o WhatsApp entra como segunda
/// implementação atrás desta mesma interface (decisão 6). O canal não conhece régua, parcela nem
/// formatura — recebe <see cref="MensagemDeNotificacao"/> e devolve o que precisa para o histórico.
/// <para>
/// <c>Canal</c> é o enum, e não um nome em texto: a regra guarda o canal escolhido, e casar regra com
/// implementação por <c>string</c> transformaria um erro de digitação em mensagem que nunca sai.
/// </para>
/// </remarks>
public interface ICanalDeNotificacao
{
    /// <summary>Qual canal esta implementação atende.</summary>
    CanalDeNotificacao Canal { get; }

    /// <summary>
    /// Entrega a mensagem ao canal.
    /// </summary>
    /// <remarks>
    /// Não persiste: quem chama grava o registro de envio na <b>mesma transação</b>, que é o que
    /// garante "ou os dois, ou nenhum".
    /// </remarks>
    /// <param name="mensagem">Mensagem pronta.</param>
    Task<Result<EntregaDaMensagem>> Enviar(MensagemDeNotificacao mensagem, CancellationToken ct = default);
}

/// <summary>
/// A régua rodando: seleciona, agrupa, enfileira e registra. Chamada pelo worker, nunca por uma requisição.
/// </summary>
/// <remarks>
/// Fica no <c>Business</c>, e não dentro do job, pelo mesmo motivo de <c>IGeracaoDeRelatoriosService</c>:
/// o job é um <c>BackgroundService</c> — ele abre o escopo, marca a hora e registra o log; a regra é aqui,
/// e é aqui que o teste unitário a alcança sem worker e sem banco.
/// </remarks>
public interface IReguaService
{
    /// <summary>As formaturas que a régua percorre: só as <c>Ativa</c>.</summary>
    /// <remarks>Suspensa ou encerrada não dispara: turma inadimplente com a Kapa não cobra os próprios alunos em nome da plataforma.</remarks>
    Task<IReadOnlyList<FormaturaParaRegua>> ListarFormaturas(CancellationToken ct = default);

    /// <summary>
    /// Roda a régua de uma formatura. O escopo já está apontado para ela.
    /// </summary>
    /// <remarks>
    /// Idempotente por <c>(parcela, regra, dia)</c>: chamar duas vezes no mesmo dia manda uma mensagem.
    /// Fora da janela de envio devolve <see cref="ResumoDaRodada.Nenhuma"/> sem tocar em nada.
    /// </remarks>
    /// <param name="formatura">Turma e nome, que vai na variável <c>{formatura}</c>.</param>
    /// <param name="agoraUtc">Momento da rodada, em UTC.</param>
    Task<ResumoDaRodada> Executar(FormaturaParaRegua formatura, DateTime agoraUtc, CancellationToken ct = default);
}

/// <summary>
/// A régua como a comissão a governa: os degraus, o histórico, a prévia e o disparo avulso.
/// </summary>
/// <remarks>
/// Quem é da turma vem da política do endpoint; o que é "do próprio titular" é regra daqui — a
/// preferência é lida e gravada pelo vínculo de quem chama, nunca por um id vindo do corpo.
/// </remarks>
public interface INotificacaoService
{
    /// <summary>A régua da turma. Sem nenhuma configurada, materializa a padrão e devolve.</summary>
    /// <remarks>Decisão 5: a turma que não configurar nada recebe a régua padrão.</remarks>
    Task<Result<IReadOnlyList<RegraResumo>>> ListarRegras(CancellationToken ct = default);

    /// <summary>Grava a régua inteira. Variável desconhecida é recusada <b>aqui</b>, não no envio.</summary>
    /// <param name="dados">Os degraus, um por par <c>(gatilho, dias)</c>.</param>
    Task<Result<IReadOnlyList<RegraResumo>>> SalvarRegras(DadosDaRegua dados, CancellationToken ct = default);

    /// <summary>
    /// Manda o degrau com dados de exemplo para quem clicou — nunca para a turma.
    /// </summary>
    /// <remarks>
    /// O botão que testa mandando de verdade é o botão que um dia alguém clica achando que é prévia.
    /// O destinatário é o e-mail da conta de <paramref name="usuarioId"/>, e não um campo do corpo.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem clicou — e quem recebe.</param>
    /// <param name="regraId">Degrau a testar.</param>
    Task<Result> Testar(Guid formaturaId, Guid usuarioId, Guid regraId, CancellationToken ct = default);

    /// <summary>Quem recebeu o quê, quando, por qual canal e com qual resultado.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Canal, situação, período e busca.</param>
    Task<Result<PaginaDe<NotificacaoNoHistorico>>> ListarHistorico(
        PaginacaoRequest paginacao,
        FiltroDeNotificacoes filtro,
        CancellationToken ct = default
    );

    /// <summary>O que o titular escolheu receber. Tipo sem linha gravada vem ligado.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Titular.</param>
    Task<Result<IReadOnlyList<PreferenciaResumo>>> ListarPreferencias(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Grava as escolhas do titular. Desligar cobrança devolve 409.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Titular.</param>
    /// <param name="dados">Um item por tipo.</param>
    Task<Result<IReadOnlyList<PreferenciaResumo>>> SalvarPreferencias(
        Guid formaturaId,
        Guid usuarioId,
        DadosDasPreferencias dados,
        CancellationToken ct = default
    );

    /// <summary>
    /// Cobra uma parcela agora, à mão, com o degrau de atraso mais próximo.
    /// </summary>
    /// <remarks>
    /// Passa pelas mesmas barreiras da régua automática: parcela paga, cancelada ou com informe
    /// pendente não é cobrada, e o teto de uma mensagem por pessoa por dia continua valendo.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="parcelaId">Parcela a cobrar.</param>
    Task<Result> Cobrar(Guid formaturaId, Guid parcelaId, CancellationToken ct = default);
}

/// <summary>
/// Régua, histórico e preferências da formatura selecionada.
/// </summary>
/// <remarks>
/// Isolados pelo filtro global: nenhum método recebe a formatura. A exceção é
/// <see cref="ListarFormaturasAtivasDeTodasAsFormaturas"/>, do worker, que escolhe as turmas a
/// percorrer antes de existir escopo apontado para alguma.
/// </remarks>
public interface INotificacaoRepository
{
    /// <summary>As turmas <c>Ativa</c>, para o job abrir um escopo por turma.</summary>
    Task<IReadOnlyList<FormaturaParaRegua>> ListarFormaturasAtivasDeTodasAsFormaturas(CancellationToken ct = default);

    /// <summary>Os degraus da turma, do mais cedo ao mais tarde.</summary>
    Task<IReadOnlyList<RegraResumo>> ListarRegras(CancellationToken ct = default);

    /// <summary>Os degraus rastreados para alteração.</summary>
    Task<IReadOnlyList<RegraDeNotificacao>> ListarRegrasParaEdicao(CancellationToken ct = default);

    /// <summary>Um degrau rastreado; nulo se não existir aqui.</summary>
    /// <param name="regraId">Degrau.</param>
    Task<RegraDeNotificacao?> ObterRegraParaEdicao(Guid regraId, CancellationToken ct = default);

    /// <summary>Marca degraus novos para inclusão.</summary>
    /// <param name="regras">Degraus.</param>
    Task AdicionarRegras(IReadOnlyList<RegraDeNotificacao> regras, CancellationToken ct = default);

    /// <summary>Marca um degrau para remoção — o que sumiu da régua gravada.</summary>
    /// <param name="regras">Degraus rastreados.</param>
    void RemoverRegras(IReadOnlyList<RegraDeNotificacao> regras);

    /// <summary>
    /// As parcelas que vencem no dia, de quem a régua pode cobrar.
    /// </summary>
    /// <remarks>
    /// Já sai sem paga, cancelada e renegociada, e <b>sem a que tem informe de pagamento pendente</b>
    /// (decisão 1): o intervalo entre o formando pagar e a tesouraria confirmar é real, e cobrar nesse
    /// intervalo é cobrar quem pagou.
    /// </remarks>
    /// <param name="vencimento">Dia do vencimento procurado.</param>
    Task<IReadOnlyList<ParcelaParaCobranca>> ListarParaCobranca(DateOnly vencimento, CancellationToken ct = default);

    /// <summary>A mesma leitura, de uma parcela só — o disparo avulso. Nula se ela não puder ser cobrada.</summary>
    /// <param name="parcelaId">Parcela.</param>
    Task<ParcelaParaCobranca?> ObterParaCobranca(Guid parcelaId, CancellationToken ct = default);

    /// <summary>Quantos informes de pagamento estão pendentes desde o dia, ou antes.</summary>
    /// <param name="ate">Último dia de criação contado.</param>
    Task<int> ContarInformesPendentesAte(DateOnly ate, CancellationToken ct = default);

    /// <summary>O que a régua já disparou no dia — é o que ela desconta antes de montar as mensagens.</summary>
    /// <param name="dia">Dia de referência.</param>
    Task<IReadOnlyList<ChaveDeEnvio>> ListarChavesDoDia(DateOnly dia, CancellationToken ct = default);

    /// <summary>Os endereços que o canal já recusou em definitivo — a régua para de tentar.</summary>
    Task<IReadOnlyList<string>> ListarDestinatariosInvalidos(CancellationToken ct = default);

    /// <summary>Marca registros de envio novos para inclusão.</summary>
    /// <param name="envios">Registros.</param>
    Task AdicionarEnvios(IReadOnlyList<NotificacaoEnviada> envios, CancellationToken ct = default);

    /// <summary>
    /// As notificações ainda enfileiradas cujo e-mail a fila já resolveu, rastreadas.
    /// </summary>
    /// <remarks>É o que fecha o histórico: "entregue", "falhou" e o motivo vêm da fila que o worker esvazia.</remarks>
    /// <param name="limite">Quantas conferir por rodada.</param>
    Task<IReadOnlyList<EntregaAConferir>> ListarEntregasAConferir(int limite, CancellationToken ct = default);

    /// <summary>Uma página do histórico, da mais recente.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Canal, situação, período e busca.</param>
    Task<PaginaDe<NotificacaoNoHistorico>> ListarHistorico(PaginacaoRequest paginacao, FiltroDeNotificacoes filtro, CancellationToken ct = default);

    /// <summary>As preferências gravadas do vínculo, rastreadas.</summary>
    /// <param name="vinculoId">Titular.</param>
    Task<IReadOnlyList<PreferenciaDeNotificacao>> ListarPreferenciasParaEdicao(Guid vinculoId, CancellationToken ct = default);

    /// <summary>Marca preferências novas para inclusão.</summary>
    /// <param name="preferencias">Preferências.</param>
    Task AdicionarPreferencias(IReadOnlyList<PreferenciaDeNotificacao> preferencias, CancellationToken ct = default);
}

/// <summary>A identidade de um disparo no dia: o que o índice único protege.</summary>
/// <param name="RegraId">Degrau.</param>
/// <param name="ParcelaId">Parcela, ou nulo no resumo à tesouraria.</param>
public sealed record ChaveDeEnvio(Guid RegraId, Guid? ParcelaId);

/// <summary>Uma notificação enfileirada e o desfecho do e-mail dela na fila.</summary>
/// <param name="Notificacao">Registro rastreado.</param>
/// <param name="Status">Situação do e-mail na fila.</param>
/// <param name="Erro">Última falha do e-mail, se houve.</param>
public sealed record EntregaAConferir(NotificacaoEnviada Notificacao, EEmailStatus Status, string? Erro);
