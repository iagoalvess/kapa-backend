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
using Backend.Business.Recebimentos.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Loja.Services;

/// <summary>
/// Os e-mails para quem comprou na loja: a reserva, a confirmação, o pagamento sem lugar e o link reenviado.
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
            $"O pagamento de <strong>{Convites(compra)}</strong> foi confirmado. Os convites são nominais: pelo link abaixo você "
                + "informa o nome e o documento de quem vai usar cada um, manda para a pessoa e baixa o PDF. Dá para trocar até 24 horas antes da festa.",
            "Nomear meus convites",
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

/// <summary>Quem vende, como o comprador precisa ler (P5).</summary>
/// <param name="Turma">Nome da turma.</param>
/// <param name="Contato">E-mail da comissão, se houver.</param>
public sealed record Vendedor(string Turma, string? Contato);
