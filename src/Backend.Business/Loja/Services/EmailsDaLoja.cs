using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Loja.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Loja.Services;

/// <summary>
/// Os e-mails para quem comprou na loja: a reserva, a confirmação, o pagamento sem lugar, o link reenviado e o
/// cancelamento — e o aviso à comissão do pedido de cancelamento (Sprint 38).
/// </summary>
/// <remarks>
/// Todos levam o link da compra — é por ele que o comprador sem conta volta (decisão 10) — e todos dizem
/// que quem vende é a turma, e com quem falar sobre devolução (P5): quem cancelar vai reclamar com o nome que
/// viu no e-mail. Só enfileiram, na transação de quem chama. Sem interface, como <c>EmailsDoConvite</c>.
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="link">O link assinado da compra.</param>
/// <param name="aplicacao">Nome da aplicação e endereço do front.</param>
/// <param name="formaturas">O nome da turma que vende.</param>
/// <param name="vinculos">O e-mail da presidência, o contato da comissão (P5).</param>
public sealed class EmailsDaLoja(
    IEmailService emailService,
    LinkDaCompra link,
    IOptions<AplicacaoSettings> aplicacao,
    IFormaturaRepository formaturas,
    IVinculoRepository vinculos
)
{
    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    /// <summary>Quem vende: o nome da turma e o e-mail de quem a preside — o contato que a P5 exige na tela e no e-mail.</summary>
    /// <param name="formaturaId">Turma.</param>
    public async Task<Vendedor> Vendedor(Guid formaturaId, CancellationToken ct = default) =>
        new(
            await formaturas.ObterNome(formaturaId, ct) ?? string.Empty,
            (await vinculos.ListarEmailsDosPresidentes(formaturaId, ct)).Order(StringComparer.Ordinal).FirstOrDefault()
        );

    /// <summary>O endereço da compra no front.</summary>
    /// <param name="compra">Compra, com a versão atual do link.</param>
    public string Link(CompraDeConvite compra) => _aplicacao.Link($"{RotasDoFront.Compra}{link.Token(compra)}");

    /// <summary>A reserva feita: pague até tal hora, e o link para voltar.</summary>
    /// <param name="compra">A compra recém-criada.</param>
    /// <param name="vendedor">Nome da turma e contato da comissão.</param>
    public Task Reservada(CompraDeConvite compra, Vendedor vendedor, CancellationToken ct = default)
    {
        var ate = DataUtils.ParaExibicao(compra.ExpiraEm).ToString("dd/MM 'às' HH:mm", CultureInfo.InvariantCulture);

        return Enviar(
            compra,
            vendedor,
            $"Sua reserva: {Convites(compra)} — {vendedor.Turma}",
            "Seus convites estão reservados",
            $"Reservamos <strong>{Convites(compra)}</strong> ({ModeloDeEmail.Texto(FormatosBrasileiros.Reais(compra.ValorEmCentavos))}) para você. "
                + $"Pague o PIX até <strong>{ate}</strong>; depois disso, a reserva volta para a venda. "
                + "Assim que o pagamento cair, você recebe outro e-mail com os convites.",
            "Ver minha compra",
            Mascote.Checklist,
            ct
        );
    }

    /// <summary>O pagamento confirmou: os convites estão prontos para nomear.</summary>
    /// <param name="compra">A compra paga.</param>
    /// <param name="vendedor">Nome da turma e contato da comissão.</param>
    public Task Confirmada(CompraDeConvite compra, Vendedor vendedor, CancellationToken ct = default) =>
        Enviar(
            compra,
            vendedor,
            $"Pagamento confirmado: {Convites(compra)} — {vendedor.Turma}",
            "Seus convites estão prontos",
            $"O pagamento de <strong>{Convites(compra)}</strong> foi confirmado. Pelo link abaixo você vê cada convite, manda para "
                + "quem vai usar e baixa o PDF. Se alguém não puder ir, dá para trocar o nome até 24 horas antes da festa.",
            "Ver meus convites",
            Mascote.Feliz,
            ct
        );

    /// <summary>O pagamento chegou depois de a reserva vencer, e não havia mais lugar (decisão 9).</summary>
    /// <param name="compra">A compra a devolver.</param>
    /// <param name="vendedor">Nome da turma e contato da comissão.</param>
    public Task SemLugar(CompraDeConvite compra, Vendedor vendedor, CancellationToken ct = default) =>
        Enviar(
            compra,
            vendedor,
            $"Seu pagamento chegou sem convite disponível — {vendedor.Turma}",
            "Os convites esgotaram antes do seu pagamento",
            "Seu pagamento chegou depois de a reserva vencer, e os convites já tinham acabado. O dinheiro foi para a conta da turma, "
                + "e a devolução é feita por ela: a comissão já tem o seu pedido na lista de devoluções.",
            "Ver minha compra",
            Mascote.Erro,
            ct
        );

    /// <summary>O link novo, a pedido — o anterior deixou de abrir.</summary>
    /// <param name="compra">A compra, com o link já girado.</param>
    /// <param name="vendedor">Nome da turma e contato da comissão.</param>
    public Task LinkReenviado(CompraDeConvite compra, Vendedor vendedor, CancellationToken ct = default) =>
        Enviar(
            compra,
            vendedor,
            $"Seu link de acesso à compra — {vendedor.Turma}",
            "Seu link de acesso",
            $"Você pediu o link da sua compra de <strong>{Convites(compra)}</strong>. Os links enviados antes deixaram de funcionar.",
            "Abrir minha compra",
            Mascote.Cadeado,
            ct
        );

    /// <summary>
    /// Convites cancelados pela turma (Sprint 38, decisão 4): quais deixaram de valer, quanto volta e quem devolve.
    /// </summary>
    /// <param name="compra">A compra, já cancelada.</param>
    /// <param name="lugares">Quantos convites deixaram de valer agora.</param>
    /// <param name="estornoEmCentavos">Quanto a turma devolve por eles.</param>
    /// <param name="motivo">O motivo que a comissão escreveu.</param>
    /// <param name="vendedor">Nome da turma e contato da comissão.</param>
    public Task Cancelada(
        CompraDeConvite compra,
        int lugares,
        long estornoEmCentavos,
        string motivo,
        Vendedor vendedor,
        CancellationToken ct = default
    )
    {
        var quantos = lugares == 1 ? "1 convite" : $"{lugares} convites";
        var sobraram = compra.LugaresValendo switch
        {
            0 => string.Empty,
            1 => " O outro convite da compra continua valendo, com o mesmo código.",
            var n => $" Os outros {n} convites da compra continuam valendo, com o mesmo código.",
        };

        return Enviar(
            compra,
            vendedor,
            $"Convite cancelado: {quantos} — {vendedor.Turma}",
            lugares == 1 ? "Seu convite foi cancelado" : "Seus convites foram cancelados",
            $"A comissão da turma cancelou <strong>{quantos}</strong> da sua compra (motivo: {ModeloDeEmail.Texto(motivo)}), e o código não vale mais na portaria.{sobraram} "
                + $"A turma vai devolver <strong>{ModeloDeEmail.Texto(FormatosBrasileiros.Reais(estornoEmCentavos))}</strong> por PIX, da conta dela — o {ModeloDeEmail.Texto(_aplicacao.Nome)} não movimenta o dinheiro da venda.",
            "Ver minha compra",
            Mascote.Erro,
            ct
        );
    }

    /// <summary>A comissão recusou o pedido de cancelamento (Sprint 38, P1): os convites continuam valendo.</summary>
    /// <param name="compra">A compra.</param>
    /// <param name="motivo">Por que a comissão recusou.</param>
    /// <param name="vendedor">Nome da turma e contato da comissão.</param>
    public Task PedidoRecusado(CompraDeConvite compra, string motivo, Vendedor vendedor, CancellationToken ct = default) =>
        Enviar(
            compra,
            vendedor,
            $"Seu pedido de cancelamento foi recusado — {vendedor.Turma}",
            "Seu pedido de cancelamento foi recusado",
            $"A comissão da turma recusou o cancelamento que você pediu. Motivo: <strong>{ModeloDeEmail.Texto(motivo)}</strong>. "
                + "Seus convites continuam valendo, com os mesmos códigos.",
            "Ver minha compra",
            Mascote.Checklist,
            ct
        );

    /// <summary>O comprador pediu cancelamento pelo link (Sprint 38, P1): a comissão tem uma pendência.</summary>
    /// <param name="compra">A compra.</param>
    /// <param name="convites">Quantos convites ele pede para cancelar.</param>
    /// <param name="formaturaId">A turma — o e-mail vai para a comissão dela.</param>
    public async Task PedidoRecebido(CompraDeConvite compra, int convites, Guid formaturaId, CancellationToken ct = default)
    {
        var quantos = convites == 1 ? "1 convite" : $"{convites} convites";
        var html = ModeloDeEmail.Montar(
            _aplicacao,
            "Pedido de cancelamento na loja",
            $"<strong>{ModeloDeEmail.Texto(compra.NomeDoComprador ?? "Um comprador")}</strong> pediu o cancelamento de <strong>{quantos}</strong> "
                + "comprados na loja da turma. Os convites continuam valendo até a comissão aprovar ou recusar o pedido.",
            "Responder o pedido",
            _aplicacao.Link(RotasDoFront.ComprasDaLoja),
            Mascote.Checklist
        );

        foreach (var email in await vinculos.ListarEmailsDaComissao(formaturaId, ct))
            await emailService.Enfileirar(new NovoEmail(email, $"Pedido de cancelamento: {quantos} — loja da turma", html), ct);
    }

    private Task<Result<Guid>> Enviar(
        CompraDeConvite compra,
        Vendedor vendedor,
        string assunto,
        string titulo,
        string mensagemHtml,
        string botao,
        Mascote mascote,
        CancellationToken ct
    ) =>
        emailService.Enfileirar(
            new NovoEmail(
                compra.Email ?? throw new InvalidOperationException($"E-mail da compra {compra.Id} pedido depois da exclusão dos dados."),
                assunto,
                ModeloDeEmail.Montar(_aplicacao, titulo, $"{mensagemHtml}<br><br>{QuemVende(vendedor)}", botao, Link(compra), mascote)
            ),
            ct
        );

    /// <summary>A linha da P5: a turma vende, e reclamação vai para a comissão.</summary>
    private string QuemVende(Vendedor vendedor) =>
        $"Quem vende estes convites é a <strong>{ModeloDeEmail.Texto(vendedor.Turma)}</strong>, e não o {ModeloDeEmail.Texto(_aplicacao.Nome)}. "
        + (
            vendedor.Contato is { } contato
                ? $"Troca, cancelamento ou devolução: fale com a comissão em {ModeloDeEmail.Texto(contato)}."
                : "Troca, cancelamento ou devolução: fale com a comissão da turma."
        );

    private static string Convites(CompraDeConvite compra) => compra.Quantidade == 1 ? "1 convite" : $"{compra.Quantidade} convites";
}
