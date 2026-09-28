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

    private string LinkDaFormatura => _aplicacao.Link(RotasDoFront.Formatura);

    /// <summary>Pagamento confirmado: a turma está ativa.</summary>
    /// <param name="formatura">Formatura ativada.</param>
    /// <param name="presidentes">E-mails dos presidentes.</param>
    /// <param name="vigenteAte">Fim da vigência paga.</param>
    public Task BoasVindas(Formatura formatura, IReadOnlyList<string> presidentes, DateTime vigenteAte, CancellationToken ct = default) =>
        Enfileirar(
            presidentes,
            $"{formatura.Nome} está ativa",
            "Pagamento confirmado",
            $"A assinatura da formatura <strong>{ModeloDeEmail.Texto(formatura.Nome)}</strong> foi confirmada e a turma já está ativa. "
                + $"A licença vale até {Data(vigenteAte)}.",
            Mascote.Foguete,
            ct
        );

    /// <summary>O provedor recusou o pagamento.</summary>
    /// <param name="formatura">Formatura da assinatura.</param>
    /// <param name="presidentes">E-mails dos presidentes.</param>
    public Task PagamentoRecusado(Formatura formatura, IReadOnlyList<string> presidentes, CancellationToken ct = default) =>
        Enfileirar(
            presidentes,
            $"Pagamento recusado — {formatura.Nome}",
            "Pagamento recusado",
            $"O pagamento da assinatura de <strong>{ModeloDeEmail.Texto(formatura.Nome)}</strong> foi recusado. "
                + "Refaça o pagamento pelo painel da turma para ativá-la.",
            Mascote.Erro,
            ct
        );

    /// <summary>Vigência e carência acabaram: a turma entrou em modo leitura.</summary>
    /// <param name="formatura">Formatura suspensa.</param>
    /// <param name="presidentes">E-mails dos presidentes.</param>
    public Task Suspensao(Formatura formatura, IReadOnlyList<string> presidentes, CancellationToken ct = default) =>
        Enfileirar(
            presidentes,
            $"{formatura.Nome} está em modo leitura",
            "Assinatura vencida",
            $"A assinatura de <strong>{ModeloDeEmail.Texto(formatura.Nome)}</strong> venceu e a turma entrou em modo leitura. "
                + "Nada foi apagado: todos continuam consultando. Para voltar a registrar, renove a assinatura.",
            Mascote.Alerta,
            ct
        );

    /// <summary>Aviso de vencimento em D-7, D-3 ou D+1.</summary>
    /// <param name="formatura">Formatura da assinatura.</param>
    /// <param name="presidentes">E-mails dos presidentes.</param>
    /// <param name="marco">Dias até o vencimento; negativo é depois.</param>
    /// <param name="vigenteAte">Fim da vigência.</param>
    /// <param name="suspensaoEm">Quando a turma vira leitura se nada mudar.</param>
    /// <param name="renovacaoCancelada">Se não há renovação automática por vir.</param>
    /// <param name="pagaPorPix">
    /// Se a turma paga no PIX avulso: a renovação não é automática, e o e-mail pede o PIX do ciclo, que se paga pela
    /// tela da assinatura (Sprint 37).
    /// </param>
    public Task AvisoDeVencimento(
        Formatura formatura,
        IReadOnlyList<string> presidentes,
        int marco,
        DateTime vigenteAte,
        DateTime suspensaoEm,
        bool renovacaoCancelada,
        bool pagaPorPix,
        CancellationToken ct = default
    )
    {
        var nome = ModeloDeEmail.Texto(formatura.Nome);

        var mensagem = (marco, renovacaoCancelada) switch
        {
            (< 0, _) => $"A assinatura de <strong>{nome}</strong> venceu em {Data(vigenteAte)} e a renovação não foi confirmada. "
                + $"Se nada mudar, a turma entra em modo leitura em {Data(suspensaoEm)}.",
            (_, false) when pagaPorPix => $"A assinatura de <strong>{nome}</strong> vence em {Data(vigenteAte)}. "
                + "Pague o PIX da renovação pela tela da assinatura, em Ver assinatura, para a turma seguir sem interrupção.",
            (_, true) => $"A assinatura de <strong>{nome}</strong> foi cancelada e o acesso completo termina em {Data(vigenteAte)}. "
                + "Depois disso a turma fica em modo leitura, sem perder nada.",
            _ => $"A assinatura de <strong>{nome}</strong> renova em {Data(vigenteAte)}. Se o pagamento estiver em dia, nada muda.",
        };

        return Enfileirar(
            presidentes,
            $"Assinatura de {formatura.Nome}",
            marco < 0 ? "Assinatura vencida" : "Vencimento próximo",
            mensagem,
            marco < 0 ? Mascote.Alerta : Mascote.Checklist,
            ct
        );
    }

    private static string Data(DateTime utc) => DataUtils.ParaExibicao(utc).ToString("dd/MM/yyyy", PtBr);

    private async Task Enfileirar(
        IReadOnlyList<string> presidentes,
        string assunto,
        string titulo,
        string mensagem,
        Mascote mascote,
        CancellationToken ct
    )
    {
        var corpo = ModeloDeEmail.Montar(_aplicacao, titulo, mensagem, "Ver assinatura", LinkDaFormatura, mascote);

        foreach (var email in presidentes)
            await emailService.Enfileirar(new NovoEmail(email, $"{assunto} — {_aplicacao.Nome}", corpo), ct);
    }
}
