using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Common;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Marketing.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Marketing.Services;

/// <summary>
/// Monta e enfileira o e-mail de cada jornada, com o rodapé e os cabeçalhos do descadastro.
/// </summary>
/// <remarks>
/// Texto fixo no código, como a régua (<c>ReguaDoKapa</c>, decisão de 24/09/2026), e no tom da landing: leve,
/// sem termo técnico (P8). Mudar uma frase é editar aqui. Sem desconto (P6): o preço é o do catálogo.
/// <para>
/// Só enfileira — quem salva é a rodada das jornadas, na mesma transação do registro do envio. Sem interface:
/// uma implementação, e o teste a constrói com um <c>IEmailService</c> substituto.
/// </para>
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="assinaturaRepository">O catálogo de planos, para o preço de "montou e parou".</param>
/// <param name="link">Token e endereços do descadastro.</param>
/// <param name="aplicacao">Identidade da aplicação.</param>
public sealed class EmailsDeMarketing(
    IEmailService emailService,
    IAssinaturaRepository assinaturaRepository,
    LinkDeDescadastro link,
    IOptions<AplicacaoSettings> aplicacao
)
{
    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    private IReadOnlyList<PlanoResumo>? _planos;

    /// <summary>Enfileira o e-mail da jornada para o candidato.</summary>
    /// <param name="candidato">Quem recebe e sobre qual turma.</param>
    /// <param name="jornada">Um de <see cref="JornadaDeMarketing"/>.</param>
    /// <param name="agoraUtc">Momento da rodada — de onde conta a validade do link de descadastro.</param>
    public async Task<Result<Guid>> Enfileirar(CandidatoDeMarketing candidato, string jornada, DateTime agoraUtc, CancellationToken ct = default)
    {
        var token = link.Token(candidato.UsuarioId, agoraUtc);
        var turma = $"<strong>{ModeloDeEmail.Texto(candidato.Formatura)}</strong>";
        var oi = $"Oi, {ModeloDeEmail.Texto(PrimeiroNome(candidato.Nome))}! ";

        var (assunto, titulo, mensagem, botao, rota, mascote) = jornada switch
        {
            JornadaDeMarketing.CriouENaoVoltou => (
                $"{candidato.Formatura}: o próximo passo é rapidinho",
                "Sua turma está esperando você",
                oi
                    + $"Você criou a {turma} no Kapa há alguns dias. O próximo passo é montar o plano de cobrança: quanto "
                    + "cada um paga, em quantas vezes e em que dia vence. Leva uns minutinhos — e depois disso quem lembra "
                    + "a turma de pagar é o Kapa, não você.",
                "Montar o plano de cobrança",
                RotasDoFront.PlanoDeCobranca,
                Mascote.Checklist
            ),
            JornadaDeMarketing.MontouEParou => (
                $"{candidato.Formatura}: agora falta a turma",
                "Falta só chamar a galera",
                oi
                    + $"O plano de cobrança da {turma} está pronto. Agora é chamar os formandos: com um plano pago, cada um "
                    + "entra pelo link da turma, recebe as cobranças e vê para onde vai o dinheiro, e a comissão confere os "
                    + $"pagamentos num lugar só. {await Precos(ct)}",
                "Ver os planos",
                RotasDoFront.Planos,
                Mascote.Foguete
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(jornada), jornada, "Jornada sem e-mail."),
        };

        var motivo =
            $"Você recebe este e-mail porque {(candidato.Criador ? "criou a turma" : "faz parte da comissão da turma")} "
            + $"{ModeloDeEmail.Texto(candidato.Formatura)} no Kapa e pediu para receber novidades.";

        var corpo = ModeloDeEmail.Montar(
            _aplicacao,
            titulo,
            mensagem,
            botao,
            _aplicacao.Link(rota),
            mascote,
            new RodapeDeMarketing(motivo, link.PaginaNoApp(token))
        );

        return await emailService.Enfileirar(
            new NovoEmail(candidato.Email, assunto, corpo, Marketing: new MarketingDoEmail(candidato.UsuarioId, link.UrlDaApi(token))),
            ct
        );
    }

    /// <summary>"Os planos pagos começam em R$ 29,90 por mês." — o menor mensal do catálogo, lido uma vez por rodada.</summary>
    /// <remarks>
    /// Um preço só: com o catálogo em mensal e anual, a lista de todos virava quatro preços numa frase. O resto
    /// está a um clique, no botão.
    /// </remarks>
    private async Task<string> Precos(CancellationToken ct)
    {
        _planos ??= await assinaturaRepository.ListarPlanosAtivos(ct);

        var mensais = _planos.Where(p => p.Ciclo == CicloDeCobranca.Mensal).ToList();

        return mensais.Count == 0
            ? string.Empty
            : $"Os planos pagos começam em {FormatosBrasileiros.Reais(mensais.Min(p => p.PrecoEmCentavos))} por mês.";
    }

    private static string PrimeiroNome(string nome) => nome.Trim().Split(' ', 2)[0];
}
