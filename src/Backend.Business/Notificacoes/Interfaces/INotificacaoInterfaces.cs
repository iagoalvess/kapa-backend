using Backend.Business.Abstractions;
using Backend.Business.Emails.Models;
using Backend.Business.Notificacoes.Models;

namespace Backend.Business.Notificacoes.Interfaces;

/// <summary>
/// Por onde uma mensagem sai.
/// </summary>
/// <remarks>
/// Só existe o e-mail (decisão 6), e a interface fica pelo que ela já paga hoje: é por ela que o
/// teste troca o envio real por um dublê. O canal não conhece régua, parcela nem formatura — recebe
/// <see cref="MensagemDeNotificacao"/> e devolve o que precisa para o histórico.
/// </remarks>
public interface ICanalDeNotificacao
{
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
    /// <summary>As formaturas que a régua percorre: só as <c>Ativa</c>, e com o módulo Avisos no plano.</summary>
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
/// A régua como a comissão a governa: os degraus, o histórico e o disparo avulso.
/// </summary>
/// <remarks>
/// Quem é da turma vem da política do endpoint; o que é "do próprio titular" é regra daqui — a
/// preferência é lida e gravada pelo vínculo de quem chama, nunca por um id vindo do corpo.
/// </remarks>
public interface INotificacaoService
{
    /// <summary>Os degraus da régua do Kapa, cada um ligado ou não. O que faltar é criado ligado.</summary>
    /// <remarks>Decisão 5: a turma que não configurar nada recebe a régua inteira ligada.</remarks>
    Task<Result<IReadOnlyList<RegraResumo>>> ListarRegras(CancellationToken ct = default);

    /// <summary>Liga ou desliga um degrau. Texto e destinatários são do Kapa e não mudam.</summary>
    /// <param name="regraId">Degrau.</param>
    /// <param name="ativa">Se dispara.</param>
    Task<Result<IReadOnlyList<RegraResumo>>> DefinirRegra(Guid regraId, bool ativa, CancellationToken ct = default);

    /// <summary>Quem recebeu o quê, quando e com qual resultado.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Situação, período e busca.</param>
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
    /// pendente não é cobrada (409), e o teto de uma mensagem por pessoa por dia continua valendo.
    /// Parcela que não é da turma responde 404, como a inexistente.
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

    /// <summary>Um degrau rastreado para alteração, ou nulo se não é da turma.</summary>
    /// <param name="id">Degrau.</param>
    Task<RegraDeNotificacao?> ObterRegraParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>Marca degraus novos para inclusão.</summary>
    /// <param name="regras">Degraus.</param>
    Task AdicionarRegras(IReadOnlyList<RegraDeNotificacao> regras, CancellationToken ct = default);

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
    /// <param name="filtro">Situação, período e busca.</param>
    Task<PaginaDe<NotificacaoNoHistorico>> ListarHistorico(PaginacaoRequest paginacao, FiltroDeNotificacoes filtro, CancellationToken ct = default);

    /// <summary>As preferências gravadas do vínculo, rastreadas.</summary>
    /// <param name="vinculoId">Titular.</param>
    Task<IReadOnlyList<PreferenciaDeNotificacao>> ListarPreferenciasParaEdicao(Guid vinculoId, CancellationToken ct = default);

    /// <summary>Marca preferências novas para inclusão.</summary>
    /// <param name="preferencias">Preferências.</param>
    Task AdicionarPreferencias(IReadOnlyList<PreferenciaDeNotificacao> preferencias, CancellationToken ct = default);
}
