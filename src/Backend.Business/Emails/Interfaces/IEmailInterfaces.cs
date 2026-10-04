using Backend.Business.Abstractions;
using Backend.Business.Emails.Models;

namespace Backend.Business.Emails.Interfaces;

/// <summary>
/// Entrega uma mensagem ao servidor de e-mail.
/// </summary>
/// <remarks>
/// É o único ponto que conhece o provedor. Lança em caso de falha — quem trata é o processamento da fila
/// (<c>ProcessamentoDaFilaDeEmail</c>), que sabe reagendar.
/// </remarks>
public interface IEmailSender
{
    /// <summary>Entrega a mensagem.</summary>
    /// <param name="mensagem">Mensagem a enviar.</param>
    /// <exception cref="Exception">Qualquer falha de conexão, autenticação ou recusa do servidor.</exception>
    Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct = default);
}

/// <summary>
/// Enfileira e-mails.
/// </summary>
/// <remarks>
/// É isto que a aplicação usa. Nenhum service de domínio chama <see cref="IEmailSender"/>
/// diretamente: e-mail enviado dentro da requisição HTTP amarra o tempo de resposta ao humor do
/// servidor SMTP, e some sem rastro se o provedor estiver fora no momento exato.
/// </remarks>
public interface IEmailService
{
    /// <summary>Coloca um e-mail na fila de envio.</summary>
    /// <param name="dados">Destinatário, assunto, corpo e prioridade.</param>
    /// <returns>O identificador do e-mail na fila.</returns>
    Task<Result<Guid>> Enfileirar(NovoEmail dados, CancellationToken ct = default);
}

/// <summary>
/// Acesso à fila de e-mails.
/// </summary>
public interface IEmailFilaRepository
{
    /// <summary>Marca um e-mail para inclusão na fila.</summary>
    /// <param name="email">E-mail a enfileirar.</param>
    Task Adicionar(EmailNaFila email, CancellationToken ct = default);

    /// <summary>
    /// Reserva um lote de e-mails pendentes para este processo.
    /// </summary>
    /// <remarks>
    /// Deve ser chamado dentro de uma transação (<c>IUnitOfWork.EmTransacaoAsync</c>): a reserva
    /// usa <c>FOR UPDATE SKIP LOCKED</c>, que é o que impede duas réplicas do worker de pegarem
    /// a mesma linha e enviarem o e-mail duas vezes.
    /// </remarks>
    /// <param name="tamanho">Quantidade máxima de e-mails.</param>
    /// <param name="agoraUtc">Momento da reserva.</param>
    Task<IReadOnlyList<EmailNaFila>> ReservarLote(int tamanho, DateTime agoraUtc, CancellationToken ct = default);

    /// <summary>Das contas informadas, as que ainda recebem comunicação do Kapa — conta ativa e preferência ligada.</summary>
    /// <remarks>
    /// Lido no <b>envio</b>, e não no enfileiramento (Sprint 40): quem saiu entre um e outro não recebe.
    /// </remarks>
    /// <param name="usuarioIds">Destinatários dos e-mails de marketing do lote.</param>
    Task<IReadOnlySet<Guid>> ListarQueRecebemMarketing(IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct = default);

    /// <summary>Apaga os já enviados ou desistidos que não mudam desde <paramref name="limiteUtc"/>.</summary>
    /// <remarks>
    /// O corpo guarda nome, valores e links de redefinição de senha: mantê-lo para sempre era guardar
    /// dado pessoal sem finalidade, e um vazamento do banco entregava links de acesso ainda válidos.
    /// </remarks>
    /// <param name="limiteUtc">Tudo o que foi concluído antes disto sai.</param>
    /// <returns>Quantos foram apagados.</returns>
    Task<int> RemoverConcluidosAnterioresA(DateTime limiteUtc, CancellationToken ct = default);

    /// <summary>Dá por falho o e-mail preso em envio desde antes de <paramref name="limiteUtc"/>.</summary>
    /// <remarks>
    /// O worker que o reservou morreu no meio. Volta como falho, e não pendente: o SMTP pode ter aceito
    /// antes da queda, e mandar de novo seria o e-mail em dobro — a tesouraria vê a falha e reenvia.
    /// </remarks>
    /// <param name="limiteUtc">Reservado antes disto é dado por perdido.</param>
    /// <returns>Quantos foram marcados.</returns>
    Task<int> DesistirDosPresosAnterioresA(DateTime limiteUtc, CancellationToken ct = default);

    /// <summary>Retrato da fila para a métrica de operação: tamanho, presos e idade.</summary>
    /// <remarks>
    /// Contagem agregada, e não listagem: quem chama é o job de métricas, a cada minuto, e não pode
    /// pagar o custo de trazer as linhas. A idade do pendente mais antigo é o sinal que falta à
    /// contagem crua — cem e-mails de um segundo atrás não são a mesma coisa que cem parados há uma hora.
    /// </remarks>
    /// <param name="limiteDoPreso">Em <c>Enviando</c> desde antes disto é preso.</param>
    Task<ProfundidadeDaFilaDeEmail> ContarProfundidade(DateTime limiteDoPreso, CancellationToken ct = default);
}

/// <summary>Retrato da fila de e-mails para a métrica de operação.</summary>
/// <param name="Pendentes">Quantos esperam envio.</param>
/// <param name="Presos">Quantos estão em envio desde antes do limite.</param>
/// <param name="MaisAntigoEm">Quando o pendente mais antigo entrou na fila, ou nulo se a fila está vazia.</param>
public readonly record struct ProfundidadeDaFilaDeEmail(int Pendentes, int Presos, DateTime? MaisAntigoEm);

/// <summary>
/// Esvazia a fila: reserva, envia e registra o resultado. Chamado pelo worker, nunca por uma requisição.
/// </summary>
/// <remarks>
/// Fica no <c>Business</c>, e não dentro do job, pelo mesmo motivo de <c>IGeracaoDeRelatoriosService</c>:
/// o job abre o escopo, respeita o intervalo e registra o log; a regra de envio mora aqui, e é
/// testável sem host.
/// </remarks>
public interface IProcessamentoDaFilaDeEmail
{
    /// <summary>Reserva um lote, envia fora de transação e registra enviado ou reagendado.</summary>
    /// <returns>Se o lote veio cheio — há mais esperando, e vale rodar outro já.</returns>
    Task<bool> ProcessarLote(CancellationToken ct = default);

    /// <summary>Apaga os concluídos antigos e desiste dos presos em envio.</summary>
    /// <returns>Quantos presos foram dados por falhos e quantos antigos foram apagados.</returns>
    Task<LimpezaDaFilaDeEmail> Limpar(CancellationToken ct = default);
}

/// <summary>O que a faxina da fila fez.</summary>
/// <param name="Presos">Presos em envio dados por falhos.</param>
/// <param name="Removidos">Concluídos antigos apagados.</param>
public readonly record struct LimpezaDaFilaDeEmail(int Presos, int Removidos);

/// <summary>
/// Enfileira uma amostra de cada e-mail do produto, com dados de exemplo — ferramenta de desenvolvimento.
/// </summary>
public interface IAmostraDeEmails
{
    /// <summary>Enfileira a amostra inteira para um endereço e salva a fila.</summary>
    /// <param name="para">Quem recebe. Vazio responde 400 <c>validacao.invalido</c> no campo <c>para</c>.</param>
    /// <returns>O instante em que a amostra começou a entrar na fila.</returns>
    Task<Result<DateTime>> Enfileirar(string? para, CancellationToken ct = default);
}
