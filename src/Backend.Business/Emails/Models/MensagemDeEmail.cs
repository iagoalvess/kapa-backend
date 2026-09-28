namespace Backend.Business.Emails.Models;

/// <summary>
/// Pedido de envio, como a aplicação o descreve.
/// </summary>
/// <param name="Para">Endereço do destinatário.</param>
/// <param name="Assunto">Assunto da mensagem.</param>
/// <param name="CorpoHtml">Corpo em HTML.</param>
/// <param name="Prioridade">Ordem de atendimento na fila.</param>
/// <param name="Anexo">Um arquivo para baixar junto — o PDF do convite da festa. Nulo é o caso comum.</param>
public sealed record NovoEmail(
    string Para,
    string Assunto,
    string CorpoHtml,
    EEmailPrioridade Prioridade = EEmailPrioridade.Normal,
    AnexoDoEmail? Anexo = null
);

/// <summary>
/// Mensagem pronta para entregar ao servidor de e-mail.
/// </summary>
/// <remarks>
/// Separada de <see cref="NovoEmail"/> porque é o contrato do <c>IEmailSender</c>, que não
/// conhece fila nem prioridade — só sabe entregar uma mensagem.
/// </remarks>
/// <param name="Para">Endereço do destinatário.</param>
/// <param name="Assunto">Assunto da mensagem.</param>
/// <param name="CorpoHtml">Corpo em HTML.</param>
/// <param name="Anexo">Arquivo anexado, se houver.</param>
public sealed record MensagemDeEmail(string Para, string Assunto, string CorpoHtml, AnexoDoEmail? Anexo = null);

/// <summary>
/// Um arquivo anexado ao e-mail, com os bytes junto.
/// </summary>
/// <remarks>
/// Os bytes moram na própria linha da fila (Sprint 21, 23/09/2026): a fila é um <i>outbox</i> — entra
/// na transação de quem a originou —, e guardar o payload inteiro nele é o que faz o anexo nascer e
/// morrer junto com o fato. Um arquivo só, e pequeno (<see cref="TamanhoMaximo"/>): o PDF do convite
/// tem ~10 KB, e o Postgres guarda <c>bytea</c> fora da linha, então o worker que varre a fila pelo
/// índice não lê esses bytes. A limpeza de 30 dias da fila apaga o anexo junto.
/// <para>
/// <c>ponytail:</c> anexo no banco serve para arquivo pequeno em volume de turma. Anexo de MB ou
/// milhões de envios pedem storage com a fila guardando só a referência — a troca é só na fila.
/// </para>
/// </remarks>
/// <param name="Nome">Nome do arquivo, como o cliente de e-mail o mostra.</param>
/// <param name="ContentType">Tipo do conteúdo (<c>application/pdf</c>).</param>
/// <param name="Conteudo">Os bytes.</param>
public sealed record AnexoDoEmail(string Nome, string ContentType, byte[] Conteudo)
{
    /// <summary>Teto do anexo: 2 MB — muito acima do convite, muito abaixo do que pesaria na fila.</summary>
    public const int TamanhoMaximo = 2 * 1024 * 1024;
}
