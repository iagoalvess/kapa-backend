using System.Globalization;
using Backend.Business.Adesoes.Models;
using Backend.Business.Common;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Microsoft.Extensions.Options;

namespace Backend.Business.Adesoes.Services;

/// <summary>
/// Monta e enfileira os e-mails da adesão: o código do aceite, a confirmação com o resumo financeiro
/// e o lembrete da comissão.
/// </summary>
/// <remarks>
/// Só enfileira — quem salva é o service, na mesma transação da adesão. Sem interface, como
/// <c>EmailsDeAssinatura</c>: uma implementação, e o teste a constrói com um <c>IEmailService</c> substituto.
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="aplicacao">Identidade da aplicação.</param>
public sealed class EmailsDeAdesao(IEmailService emailService, IOptions<AplicacaoSettings> aplicacao)
{
    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    /// <summary>A tela do termo no front — o mesmo caminho de <c>ROTAS.adesao</c>.</summary>
    private string LinkDoTermo => _aplicacao.Link(RotasDoFront.MeuTermo);

    /// <summary>Adesão registrada: o que a pessoa aceitou pagar, com a primeira parcela e as regras de atraso.</summary>
    /// <param name="email">Quem aderiu.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="versao">Versão do termo aceita.</param>
    /// <param name="plano">Plano aceito.</param>
    public Task Confirmacao(string email, string formatura, int versao, SnapshotDoPlano plano, CancellationToken ct = default)
    {
        var primeira = plano.Parcelas.Count > 0 ? plano.Parcelas[0] : null;
        var detalhe = primeira is null
            ? string.Empty
            : $" A primeira vence em {primeira.Vencimento.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}, "
                + $"no valor de {FormatosBrasileiros.Reais(primeira.ValorEmCentavos)}.";

        var mensagem =
            $"Você aderiu à versão {versao} do termo de <strong>{ModeloDeEmail.Texto(formatura)}</strong>. "
            + $"Total de {FormatosBrasileiros.Reais(plano.TotalEmCentavos)} em {plano.Parcelas.Count} parcelas.{detalhe} "
            + ModeloDeEmail.Texto(plano.RegrasDeAtrasoPorExtenso());

        return Enfileirar(email, $"Adesão registrada — {formatura}", "Adesão registrada", mensagem, "Ver meu termo", Mascote.Canudo, ct);
    }

    /// <summary>
    /// O código de seis dígitos que o aceite pede.
    /// </summary>
    /// <remarks>
    /// O código vai no corpo e no assunto: assim a pessoa o lê na notificação do celular sem abrir a
    /// mensagem, que é como ela vai querer usá-lo com o termo aberto na outra tela. Sem link para
    /// clicar — código digitado numa aba que a própria pessoa abriu não dá ao phishing o que ele quer.
    /// </remarks>
    /// <param name="email">E-mail da conta que vai aderir.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="codigo">Os seis dígitos.</param>
    /// <param name="minutos">Por quanto tempo vale.</param>
    public Task Codigo(string email, string formatura, string codigo, int minutos, CancellationToken ct = default) =>
        Enfileirar(
            email,
            $"{codigo} é o seu código de adesão — {formatura}",
            "Código para assinar o termo",
            $"Use o código <strong>{ModeloDeEmail.Texto(codigo)}</strong> para confirmar a sua adesão em "
                + $"<strong>{ModeloDeEmail.Texto(formatura)}</strong>. Ele vale por poucos minutos — conte com {minutos}. "
                + "Se não foi você quem pediu, ignore esta mensagem — sem o código, nada é assinado.",
            "Voltar ao termo",
            Mascote.Celular,
            ct
        );

    /// <summary>Lembrete da comissão para quem ainda não aderiu à versão vigente.</summary>
    /// <param name="email">Membro lembrado.</param>
    /// <param name="formatura">Nome da turma.</param>
    public Task Lembrete(string email, string formatura, CancellationToken ct = default) =>
        Enfileirar(
            email,
            $"Falta a sua adesão — {formatura}",
            "Falta a sua adesão",
            $"A comissão de <strong>{ModeloDeEmail.Texto(formatura)}</strong> pede que você leia o termo de adesão e o plano de "
                + "pagamento da turma e registre o seu aceite. Leva poucos minutos.",
            "Ler o termo",
            Mascote.Checklist,
            ct
        );

    private async Task Enfileirar(
        string email,
        string assunto,
        string titulo,
        string mensagem,
        string botao,
        Mascote mascote,
        CancellationToken ct
    ) =>
        await emailService.Enfileirar(
            new NovoEmail(email, $"{assunto} — {_aplicacao.Nome}", ModeloDeEmail.Montar(_aplicacao, titulo, mensagem, botao, LinkDoTermo, mascote)),
            ct
        );
}
