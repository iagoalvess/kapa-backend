using Backend.Business.Abstractions;
using Backend.Business.Common;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Privacidade.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Privacidade.Services;

/// <summary>
/// Os avisos do portal do titular.
/// </summary>
/// <remarks>
/// Só enfileira — quem salva é o service, na mesma transação do pedido. Sem interface, como
/// <c>EmailsDeRecebimento</c>.
/// <para>
/// O e-mail de eliminação concluída é o único do produto cujo destinatário precisa ser <b>lido
/// antes</b> da operação que ele anuncia: depois da anonimização o endereço já não está na conta.
/// Quem o lê é <c>ProcessamentoDePrivacidadeService.Eliminar</c>, logo no começo; o enfileiramento
/// acontece só depois, quando a anonimização já deu certo — assim ninguém recebe "seus dados foram
/// eliminados" sobre dados que continuam lá.
/// </para>
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="aplicacao">Identidade da aplicação.</param>
public sealed class EmailsDePrivacidade(IEmailService emailService, IOptions<AplicacaoSettings> aplicacao)
{
    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    private string LinkDoPortal => $"{_aplicacao.UrlDoFrontend.TrimEnd('/')}/minha-privacidade";

    /// <summary>O pacote da exportação ficou pronto.</summary>
    /// <param name="email">Titular.</param>
    /// <param name="dias">Por quantos dias o arquivo fica disponível.</param>
    public Task ExportacaoPronta(string email, int dias, CancellationToken ct = default)
    {
        var mensagem =
            "O pacote com tudo o que a Kapa guarda sobre você está pronto. Ele traz os mesmos dados que a tela "
            + "<strong>Privacidade</strong> mostra, em JSON e em CSV, e fica disponível por "
            + $"<strong>{dias} dias</strong> — depois disso ele é apagado, e você pode pedir outro quando quiser.";

        return Enfileirar(
            email,
            "Sua exportação de dados está pronta",
            "Exportação de dados pronta",
            mensagem,
            "Baixar o pacote",
            Mascote.Documento,
            ct
        );
    }

    /// <summary>
    /// A eliminação foi pedida: o que vai acontecer, quando, e como desistir.
    /// </summary>
    /// <remarks>
    /// Diz o prazo e diz o limite — o que é anonimizado e o que é preservado — porque prometer
    /// apagamento total e não cumprir é pior que explicar o limite. O mesmo texto está na tela antes
    /// da confirmação; este e-mail é o que alcança quem não pediu.
    /// </remarks>
    /// <param name="email">Titular.</param>
    /// <param name="prazoEm">Quando a eliminação acontece, em UTC.</param>
    public Task ExclusaoSolicitada(string email, DateTime prazoEm, CancellationToken ct = default)
    {
        var mensagem =
            "Recebemos seu pedido de eliminação de dados. Em <strong>"
            + $"{prazoEm:dd/MM/yyyy}</strong> — ou assim que você confirmar, se quiser antecipar — seu nome, CPF, RG, "
            + "endereço, telefone, e-mail e foto serão apagados de forma irreversível.<br><br>"
            + "O que <strong>não</strong> é apagado: os lançamentos financeiros da sua turma. As parcelas, os pagamentos e a "
            + "adesão continuam existindo, ligados a um código que não identifica você — a comissão precisa deles para prestar "
            + "contas, e a lei exige a guarda.<br><br>"
            + "Se você não fez este pedido, entre agora e cancele.";

        return Enfileirar(
            email,
            "Pedido de eliminação de dados recebido",
            "Pedido de eliminação recebido",
            mensagem,
            "Ver o pedido",
            Mascote.Alerta,
            ct
        );
    }

    /// <summary>
    /// Avisa o Presidente de que um membro da turma pediu eliminação.
    /// </summary>
    /// <remarks>
    /// O pedido <b>não</b> é recusado por haver parcela em aberto — direito do titular não depende de
    /// ele estar em dia. O que o aviso faz é dar à comissão a chance de conversar antes do prazo, e
    /// deixar claro que a dívida continua, só que sem nome.
    /// </remarks>
    /// <param name="aviso">Presidente, turma e o que o titular deve nela.</param>
    /// <param name="titular">Nome de quem pediu, enquanto ele ainda tem nome.</param>
    /// <param name="prazoEm">Quando a eliminação acontece, em UTC.</param>
    public Task ExclusaoParaOPresidente(PresidenteParaAviso aviso, string titular, DateTime prazoEm, CancellationToken ct = default)
    {
        var emAberto =
            aviso.EmAbertoEmCentavos > 0
                ? $"Atenção: há <strong>{FormatosBrasileiros.Reais(aviso.EmAbertoEmCentavos)}</strong> em parcelas em aberto no nome dessa pessoa. "
                    + "O valor <strong>continua devido</strong> depois da anonimização — o que muda é que a parcela deixa de mostrar o nome. "
                    + "Se for o caso de conversar, este é o momento."
                : "Não há parcelas em aberto no nome dessa pessoa.";

        var mensagem =
            $"{ModeloDeEmail.Texto(titular)}, de <strong>{ModeloDeEmail.Texto(aviso.Formatura)}</strong>, pediu a eliminação dos "
            + $"próprios dados pessoais. O pedido é um direito garantido por lei e será atendido em <strong>{prazoEm:dd/MM/yyyy}</strong>.<br><br>"
            + $"{emAberto}<br><br>"
            + "Os lançamentos da turma não são apagados: o caixa, o balancete e o histórico continuam corretos.";

        return Enfileirar(
            aviso.Email,
            $"Pedido de eliminação de dados — {aviso.Formatura}",
            "Um membro da turma pediu eliminação de dados",
            mensagem,
            null,
            Mascote.Alerta,
            ct
        );
    }

    /// <summary>A anonimização aconteceu. Último e-mail que este endereço recebe.</summary>
    /// <param name="email">Titular, antes de o endereço ser anonimizado.</param>
    /// <param name="marcador">O código que passou a identificar a pessoa nos lançamentos.</param>
    public Task ExclusaoConcluida(string email, string marcador, CancellationToken ct = default)
    {
        var mensagem =
            "Seus dados pessoais foram apagados da Kapa. Seu acesso foi encerrado e este é o último e-mail que enviamos "
            + $"para este endereço.<br><br>Os lançamentos financeiros da sua turma foram preservados sob o código "
            + $"<strong>{ModeloDeEmail.Texto(marcador)}</strong>, sem ligação com seu nome. Se precisar de comprovante do que pagou, "
            + "fale com a comissão da sua turma — ela continua com o registro.";

        return Enfileirar(email, "Seus dados foram eliminados", "Eliminação concluída", mensagem, null, Mascote.Acenando, ct);
    }

    private Task<Result<Guid>> Enfileirar(
        string email,
        string assunto,
        string titulo,
        string mensagemHtml,
        string? botao,
        Mascote mascote,
        CancellationToken ct
    ) =>
        emailService.Enfileirar(
            new NovoEmail(
                email,
                $"{assunto} — {_aplicacao.Nome}",
                ModeloDeEmail.Montar(_aplicacao.Nome, titulo, mensagemHtml, botao, botao is null ? null : LinkDoPortal, mascote)
            ),
            ct
        );
}
