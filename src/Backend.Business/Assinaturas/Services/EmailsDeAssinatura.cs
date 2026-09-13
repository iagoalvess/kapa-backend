using System.Globalization;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Formaturas.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Assinaturas.Services;

/// <summary>
/// Monta e enfileira os e-mails da assinatura, sempre para os presidentes da turma.
/// </summary>
/// <remarks>
/// Só enfileira — quem salva é o service que originou o e-mail, na mesma transação da mudança de
/// status. Sem interface: uma implementação, e o teste a constrói com um <c>IEmailService</c>
/// substituto.
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="aplicacao">Identidade da aplicação.</param>
public sealed class EmailsDeAssinatura(IEmailService emailService, IOptions<AplicacaoSettings> aplicacao)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    private string LinkDaAssinatura => $"{_aplicacao.UrlDoFrontend.TrimEnd('/')}/assinatura";

    /// <summary>Pagamento confirmado: a turma está ativa.</summary>
    /// <param name="formatura">Formatura ativada.</param>
    /// <param name="presidentes">E-mails dos presidentes.</param>
    /// <param name="vigenteAte">Fim da vigência paga.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public Task BoasVindas(Formatura formatura, IReadOnlyList<string> presidentes, DateTime vigenteAte, CancellationToken ct = default) =>
        Enfileirar(
            presidentes,
            $"{formatura.Nome} está ativa",
            "Pagamento confirmado",
            $"A assinatura da formatura <strong>{ModeloDeEmail.Texto(formatura.Nome)}</strong> foi confirmada e a turma já está ativa. "
                + $"A licença vale até {Data(vigenteAte)}.",
            ct
        );

    /// <summary>O provedor recusou o pagamento.</summary>
    /// <param name="formatura">Formatura da assinatura.</param>
    /// <param name="presidentes">E-mails dos presidentes.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public Task PagamentoRecusado(Formatura formatura, IReadOnlyList<string> presidentes, CancellationToken ct = default) =>
        Enfileirar(
            presidentes,
            $"Pagamento recusado — {formatura.Nome}",
            "Pagamento recusado",
            $"O pagamento da assinatura de <strong>{ModeloDeEmail.Texto(formatura.Nome)}</strong> foi recusado. "
                + "Confira os dados do cartão e tente de novo.",
            ct
        );

    /// <summary>Vigência e carência acabaram: a turma entrou em modo leitura.</summary>
    /// <param name="formatura">Formatura suspensa.</param>
    /// <param name="presidentes">E-mails dos presidentes.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public Task Suspensao(Formatura formatura, IReadOnlyList<string> presidentes, CancellationToken ct = default) =>
        Enfileirar(
            presidentes,
            $"{formatura.Nome} está em modo leitura",
            "Assinatura vencida",
            $"A assinatura de <strong>{ModeloDeEmail.Texto(formatura.Nome)}</strong> venceu e a turma entrou em modo leitura. "
                + "Nada foi apagado: todos continuam consultando. Para voltar a registrar, renove a assinatura.",
            ct
        );

    /// <summary>Aviso de vencimento em D-7, D-3 ou D+1.</summary>
    /// <param name="formatura">Formatura da assinatura.</param>
    /// <param name="presidentes">E-mails dos presidentes.</param>
    /// <param name="marco">Dias até o vencimento; negativo é depois.</param>
    /// <param name="vigenteAte">Fim da vigência.</param>
    /// <param name="suspensaoEm">Quando a turma vira leitura se nada mudar.</param>
    /// <param name="renovacaoCancelada">Se não há renovação automática por vir.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public Task AvisoDeVencimento(
        Formatura formatura,
        IReadOnlyList<string> presidentes,
        int marco,
        DateTime vigenteAte,
        DateTime suspensaoEm,
        bool renovacaoCancelada,
        CancellationToken ct = default
    )
    {
        var nome = ModeloDeEmail.Texto(formatura.Nome);

        var mensagem = (marco, renovacaoCancelada) switch
        {
            (< 0, _) => $"A assinatura de <strong>{nome}</strong> venceu em {Data(vigenteAte)} e a renovação não foi confirmada. "
                + $"Se nada mudar, a turma entra em modo leitura em {Data(suspensaoEm)}.",
            (_, true) => $"A assinatura de <strong>{nome}</strong> foi cancelada e o acesso completo termina em {Data(vigenteAte)}. "
                + "Depois disso a turma fica em modo leitura, sem perder nada.",
            _ => $"A assinatura de <strong>{nome}</strong> renova em {Data(vigenteAte)}. Se o cartão estiver em dia, nada muda.",
        };

        return Enfileirar(presidentes, $"Assinatura de {formatura.Nome}", marco < 0 ? "Assinatura vencida" : "Vencimento próximo", mensagem, ct);
    }

    private static string Data(DateTime utc) => DataUtils.ParaExibicao(utc).ToString("dd/MM/yyyy", PtBr);

    private async Task Enfileirar(IReadOnlyList<string> presidentes, string assunto, string titulo, string mensagem, CancellationToken ct)
    {
        var corpo = ModeloDeEmail.Montar(_aplicacao.Nome, titulo, mensagem, "Ver assinatura", LinkDaAssinatura);

        foreach (var email in presidentes)
            await emailService.Enfileirar(new NovoEmail(email, $"{assunto} — {_aplicacao.Nome}", corpo), ct);
    }
}
