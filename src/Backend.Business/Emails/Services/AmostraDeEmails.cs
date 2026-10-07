using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Models;
using Backend.Business.Adesoes.Services;
using Backend.Business.Assinaturas.Services;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Formaturas.Services;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Privacidade.Models;
using Backend.Business.Privacidade.Services;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Recebimentos.Services;
using Backend.Business.Usuarios.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Emails.Services;

/// <summary>
/// Enfileira uma amostra de cada e-mail do produto, com dados de exemplo.
/// </summary>
/// <remarks>
/// Ferramenta de desenvolvimento, não regra do produto: mexer no <see cref="ModeloDeEmail"/> é mexer em vinte
/// mensagens de uma vez, e a única forma de conferir isso é olhando as vinte na caixa de entrada — nenhum teste
/// vê um mascote fora do lugar no Gmail do celular. Quem decide se ela pode rodar é o host, pelo ambiente.
/// <para>
/// Chama os <c>EmailsDe*</c> de verdade, com dados de exemplo: amostra que monta o próprio HTML
/// mostraria o modelo certo com um texto que ninguém recebe. A exceção está em
/// <see cref="Avulso"/> — o convite monta o corpo dentro do service que o
/// origina, e chegar até ele exigiria uma turma inteira no banco.
/// </para>
/// </remarks>
/// <param name="conta">E-mails do ciclo de vida da conta.</param>
/// <param name="adesao">E-mails da adesão.</param>
/// <param name="assinatura">E-mails da assinatura.</param>
/// <param name="pagamento">E-mails de pagamento.</param>
/// <param name="privacidade">E-mails da LGPD.</param>
/// <param name="desligamento">E-mails da saída de formando.</param>
/// <param name="recebimento">E-mails da conta de recebimento.</param>
/// <param name="canal">Canal da régua de cobrança.</param>
/// <param name="emailService">Fila de e-mails, para a amostra avulsa.</param>
/// <param name="unitOfWork">Quem salva a fila — os <c>EmailsDe*</c> só enfileiram.</param>
/// <param name="aplicacao">Identidade da aplicação.</param>
public sealed class AmostraDeEmails(
    IEmailsDeConta conta,
    EmailsDeAdesao adesao,
    EmailsDeAssinatura assinatura,
    EmailsDePagamento pagamento,
    EmailsDePrivacidade privacidade,
    EmailsDeDesligamento desligamento,
    EmailsDeRecebimento recebimento,
    ICanalDeNotificacao canal,
    IEmailService emailService,
    IUnitOfWork unitOfWork,
    IOptions<AplicacaoSettings> aplicacao
) : IAmostraDeEmails
{
    private const string Turma = "Odontologia 2027";

    /// <inheritdoc />
    public async Task<Result<DateTime>> Enfileirar(string? para, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(para))
            return Erro.Validacao("validacao.invalido", "Informe ?para=endereco@exemplo.com.", "para");

        var antes = DateTime.UtcNow;

        await DaConta(para, ct);
        await DaAdesao(para, ct);
        await DaAssinatura(para, ct);
        await DoPagamento(para, ct);
        await DaPrivacidade(para, ct);
        await DoDesligamento(para, ct);
        await DoRecebimento(para, ct);
        await DaRegua(para, ct);
        await Avulso(para, ct);

        await unitOfWork.SalvarAsync(ct);

        return antes;
    }

    private async Task DaConta(string para, CancellationToken ct)
    {
        var usuario = new Usuario { Nome = "Ana Beatriz", Email = para };

        await conta.EnfileirarConfirmacao(usuario, "amostra-de-token", ct);
        await conta.EnfileirarRedefinicaoDeSenha(usuario, "amostra-de-token", ct);
        await conta.EnfileirarAvisoDeSenhaAlterada(usuario, ct);
    }

    private async Task DaAdesao(string para, CancellationToken ct)
    {
        await adesao.Codigo(para, Turma, "418293", 10, ct);
        await adesao.Confirmacao(para, Turma, 3, Plano(), ct);
        await adesao.Lembrete(para, Turma, ct);
    }

    private async Task DaAssinatura(string para, CancellationToken ct)
    {
        var formatura = new Formatura { Nome = Turma };
        var presidentes = new[] { para };
        var hoje = DateTime.UtcNow;

        await assinatura.BoasVindas(formatura, presidentes, hoje.AddYears(1), ct);
        await assinatura.PagamentoRecusado(formatura, presidentes, ct);
        await assinatura.Suspensao(formatura, presidentes, ct);
        await assinatura.AvisoDeVencimento(formatura, presidentes, 7, hoje.AddDays(7), hoje.AddDays(14), false, ct);
        await assinatura.AvisoDeVencimento(formatura, presidentes, -1, hoje.AddDays(-1), hoje.AddDays(6), true, ct);
    }

    private async Task DoPagamento(string para, CancellationToken ct)
    {
        var vencimento = DataUtils.Hoje();

        await pagamento.Confirmados([new(para, Turma, vencimento, 35_000, vencimento, 0, Guid.CreateVersion7())], ct);
        await pagamento.Confirmados([new(para, Turma, vencimento, 20_000, vencimento, 15_000, Guid.CreateVersion7())], ct);
        await pagamento.Confirmados(
            [
                new(para, Turma, vencimento, 35_000, vencimento, 0, Guid.CreateVersion7()),
                new(para, Turma, vencimento.AddMonths(1), 35_000, vencimento, 0, Guid.CreateVersion7()),
            ],
            ct
        );
        await pagamento.Recusado(para, Turma, vencimento, 35_000, "Não encontramos este PIX no extrato da turma.", ct);
        await pagamento.Estornado(para, Turma, vencimento, 35_000, "Baixa lançada na parcela errada.", ct);
    }

    private async Task DaPrivacidade(string para, CancellationToken ct)
    {
        var prazo = DateTime.UtcNow.AddDays(15);

        await privacidade.ExportacaoPronta(para, 7, ct);
        await privacidade.ExclusaoSolicitada(para, prazo, ct);
        await privacidade.ExclusaoParaOPresidente(new PresidenteParaAviso(para, Turma, 140_000), "Ana Beatriz", prazo, ct);
        await privacidade.ExclusaoConcluida(para, "FORMANDO-3F9A21", ct);
    }

    private async Task DoDesligamento(string para, CancellationToken ct)
    {
        var cancelado = new CancelamentoDaSaida(4, 140_000);

        await desligamento.Confirmacao(para, Turma, cancelado, 210_000, ct);
        await desligamento.Aviso([para], Turma, "Ana Beatriz", "Dificuldade financeira", cancelado, ct);
    }

    /// <summary>
    /// A amostra mostra o pior caso: a chave PIX da comissão saiu e sobrou o dinheiro em mãos.
    /// </summary>
    /// <remarks>
    /// É o desenho da fraude que este e-mail existe para flagrar, e o que ele precisa gritar. Vai junto o
    /// aviso que a turma recebe na mesma troca (Sprint 22, P1).
    /// </remarks>
    private async Task DoRecebimento(string para, CancellationToken ct)
    {
        var antes = new MeiosDaConta(
            new ChavePixDaConta(TipoDeChavePix.Email, "tesouraria@odonto.kapa.dev", "Comissão de Formatura Odontologia", "Curitiba", "Nubank"),
            new DadosBancarios("Banco do Brasil", "1234-5", "98765-4", "Corrente", "Comissão de Formatura Odontologia"),
            null
        );

        var depois = new MeiosDaConta(null, antes.Transferencia, new DinheiroComAlguem("Lucas", "no bloco A"));

        await recebimento.ContaAlterada(para, Turma, "Ana Beatriz", antes, depois, ct);
        await recebimento.ContaAlteradaParaATurma(para, Turma, "Ana Beatriz", ct);
    }

    private async Task DaRegua(string para, CancellationToken ct) =>
        await canal.Enviar(
            new MensagemDeNotificacao(
                para,
                $"Lembrete da sua parcela — {Turma}",
                "Olá, Ana Beatriz. A sua parcela de <strong>R$ 350,00</strong> vence em <strong>20/09/2026</strong>. "
                    + "Depois do vencimento entram multa de 2% e juros de 1% ao mês.",
                aplicacao.Value.Link(RotasDoFront.MinhasParcelas),
                "Ver meu extrato"
            ),
            ct
        );

    /// <summary>A amostra cujo corpo mora dentro do service que a origina.</summary>
    /// <remarks>
    /// Repete o texto do <c>ConviteService</c>. É cópia, e some no dia em
    /// que ele ganhar um <c>EmailsDe*</c> como os outros — até lá, é o único jeito de o
    /// convite, que é o e-mail mais visto do produto, aparecer na amostra.
    /// </remarks>
    private async Task Avulso(string para, CancellationToken ct)
    {
        var nome = aplicacao.Value.Nome;

        var convite = ModeloDeEmail.Montar(
            aplicacao.Value,
            $"Você foi convidado para {Turma}",
            $"A comissão de <strong>{Turma}</strong> (UFPR) convidou você para entrar na turma como Formando. "
                + "O convite é pessoal e vale até 30/09/2026.",
            "Aceitar convite",
            aplicacao.Value.Link(RotasDoFront.Convite + "amostra"),
            Mascote.Acenando
        );

        await emailService.Enfileirar(new NovoEmail(para, $"Convite para {Turma} — {nome}", convite), ct);
    }

    /// <summary>Um plano de exemplo: uma entrada e três mensalidades.</summary>
    private static SnapshotDoPlano Plano()
    {
        var primeiro = DataUtils.Hoje().AddMonths(1);

        var parcelas = Enumerable
            .Range(0, 4)
            .Select(indice => new ParcelaSimulada(TipoDeCobranca.Mensalidade, "Mensalidade", indice + 1, 4, primeiro.AddMonths(indice), 35_000))
            .ToArray();

        return new SnapshotDoPlano(
            SnapshotDoPlano.EsquemaAtual,
            Guid.NewGuid(),
            "Plano 2027",
            PercentualDeMulta: 200,
            PercentualDeJurosAoMes: 100,
            CarenciaEmDias: 3,
            PercentualDeDescontoPorAntecipacao: 0,
            Itens: [new DadosDoItem(TipoDeCobranca.Mensalidade, "Mensalidade", 35_000, 4, 10, primeiro)],
            Parcelas: parcelas,
            TotalEmCentavos: parcelas.Sum(parcela => parcela.ValorEmCentavos)
        );
    }
}
