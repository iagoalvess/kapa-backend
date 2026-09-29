using Backend.Business.Abstractions;
using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Recebimentos.Models;

/// <summary>
/// A autorização que a turma deu ao Kapa no Mercado Pago, por OAuth: com ela o Kapa emite cobrança na
/// conta da turma (Sprint 25, P2). Uma por formatura, em tabela própria.
/// </summary>
/// <remarks>
/// OAuth, e não token colado (decisão de 24/09/2026): o presidente clica, entra na conta da turma e
/// autoriza — e pode revogar lá sem trocar nada aqui. A cobrança emitida com esse token pertence à
/// aplicação do Kapa, então o aviso de pagamento chega ao webhook do Kapa, assinado com o segredo do
/// Kapa, sem a turma configurar nada.
/// <para>
/// Segredo, não dado: os dois tokens são cifrados na coluna (a <c>CifraDeCampo</c> do CPF), e nenhuma
/// rota os devolve. A tela recebe a conta que o Mercado Pago disse ser a dona da autorização.
/// </para>
/// <para>
/// O token vale 180 dias; o worker o renova com o <see cref="RefreshToken"/> antes de vencer. Conectar de
/// novo substitui a autorização inteira — nunca se edita um pedaço.
/// </para>
/// </remarks>
public class CredencialDeProvedor : EntidadeDaFormatura
{
    /// <summary>O Access Token, em claro só na memória — a coluna guarda cifrado.</summary>
    public string AccessToken { get; private set; } = string.Empty;

    /// <summary>O Refresh Token, cifrado como o outro.</summary>
    public string RefreshToken { get; private set; } = string.Empty;

    /// <summary>Até quando o <see cref="AccessToken"/> vale, em UTC.</summary>
    public DateTime ExpiraEm { get; private set; }

    /// <summary>O id da conta da turma no Mercado Pago — o <c>user_id</c> que vem no aviso de pagamento.</summary>
    public long IdNoProvedor { get; private set; }

    /// <summary>Como o Mercado Pago identifica a conta (e-mail ou apelido), para a turma reconhecê-la.</summary>
    public string ContaNoProvedor { get; private set; } = string.Empty;

    /// <summary>Quem autorizou a conexão atual.</summary>
    public Guid CadastradaPorUsuarioId { get; private set; }

    /// <summary>
    /// A <c>public_key</c> da conta, que o formulário de cartão do navegador usa para tokenizar (Sprint 39). Nula na
    /// conexão anterior à sprint: a turma conecta de novo para ligar o cartão.
    /// </summary>
    public string? ChavePublica { get; private set; }

    /// <summary>Quando o cartão foi ligado (P7); nulo: desligado, e a opção não aparece em lugar nenhum.</summary>
    public DateTime? CartaoLigadoEm { get; private set; }

    /// <summary>Quem ligou o cartão — ligar é aceitar a taxa e o risco de contestação (P2 e P4).</summary>
    public Guid? CartaoLigadoPorUsuarioId { get; private set; }

    /// <summary>
    /// A taxa do cartão que a turma repassa ao formando, base 10.000 (P2); nula: a turma absorve, o padrão. O
    /// Kapa não sabe se a conta recebe na hora ou em 30 dias (P8), então quem diz o percentual é a turma.
    /// </summary>
    public int? TaxaDoCartaoRepassada { get; private set; }

    /// <summary>
    /// Desde quando a turma cobra as parcelas só pelo Mercado Pago (29/09/2026); nulo: modo manual, e o formando vê
    /// só os meios da comissão e o aviso de pagamento. A loja pública não depende disto — ela é sempre Mercado Pago.
    /// </summary>
    /// <remarks>
    /// Um modo ou outro, nunca os dois: com as duas listas na tela o formando escolhia o PIX da chave, sem taxa, e a
    /// turma pagava o Mercado Pago sem se livrar da conferência. Mora na credencial porque o automático não existe
    /// sem ela — desconectar exige voltar ao manual antes.
    /// </remarks>
    public DateTime? CobrancaAutomaticaEm { get; private set; }

    /// <summary>Se as parcelas e os opcionais se pagam só pelo Mercado Pago.</summary>
    public bool CobrancaAutomatica => CobrancaAutomaticaEm is not null;

    /// <summary>Se o cartão aparece para o formando e para o comprador da loja.</summary>
    public bool CartaoLigado => CartaoLigadoEm is not null && ChavePublica is not null;

    /// <summary>Troca o modo de cobrança das parcelas. Quem confere se pode trocar é o service.</summary>
    /// <param name="automatica">Liga ou desliga.</param>
    /// <param name="agoraUtc">Quando.</param>
    public void DefinirCobrancaAutomatica(bool automatica, DateTime agoraUtc) => CobrancaAutomaticaEm = automatica ? agoraUtc : null;

    /// <summary>Grava uma autorização recém-concedida, no lugar da que houver.</summary>
    /// <param name="accessToken">Token de acesso.</param>
    /// <param name="refreshToken">Token de renovação.</param>
    /// <param name="expiraEm">Validade do token de acesso, em UTC.</param>
    /// <param name="idNoProvedor">Id da conta no Mercado Pago.</param>
    /// <param name="contaNoProvedor">Nome da conta para a tela.</param>
    /// <param name="usuarioId">Quem autorizou.</param>
    /// <param name="chavePublica">A <c>public_key</c> da conta, quando o Mercado Pago a devolveu.</param>
    /// <remarks>Outra conta desliga o cartão: a taxa e o risco aceitos eram os da conta anterior.</remarks>
    public void Conectar(
        string accessToken,
        string refreshToken,
        DateTime expiraEm,
        long idNoProvedor,
        string contaNoProvedor,
        Guid usuarioId,
        string? chavePublica = null
    )
    {
        if (idNoProvedor != IdNoProvedor)
            DesligarCartao();

        Renovar(accessToken, refreshToken, expiraEm);
        ChavePublica = chavePublica ?? (idNoProvedor == IdNoProvedor ? ChavePublica : null);
        IdNoProvedor = idNoProvedor;
        ContaNoProvedor = contaNoProvedor;
        CadastradaPorUsuarioId = usuarioId;
    }

    /// <summary>Troca os tokens pelos da renovação. A conta e quem autorizou continuam os mesmos.</summary>
    /// <param name="accessToken">Token de acesso novo.</param>
    /// <param name="refreshToken">Token de renovação novo — o Mercado Pago troca os dois.</param>
    /// <param name="expiraEm">Validade nova, em UTC.</param>
    public void Renovar(string accessToken, string refreshToken, DateTime expiraEm)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiraEm = expiraEm;
    }

    /// <summary>Liga o cartão (P7), com a taxa repassada ou absorvida (P2).</summary>
    /// <param name="taxaRepassada">Base 10.000; nula, a turma absorve.</param>
    /// <param name="usuarioId">Quem ligou.</param>
    /// <param name="agoraUtc">Quando.</param>
    public void LigarCartao(int? taxaRepassada, Guid usuarioId, DateTime agoraUtc)
    {
        TaxaDoCartaoRepassada = taxaRepassada;
        CartaoLigadoPorUsuarioId = usuarioId;
        CartaoLigadoEm = agoraUtc;
    }

    /// <summary>Desliga o cartão: some das telas. O que já foi cobrado segue conciliado pelo pedido.</summary>
    public void DesligarCartao()
    {
        CartaoLigadoEm = null;
        CartaoLigadoPorUsuarioId = null;
        TaxaDoCartaoRepassada = null;
    }

    /// <summary>O cartão pronto para a tela de pagar este valor; nulo com o cartão desligado.</summary>
    /// <param name="valorEmCentavos">O valor do PIX.</param>
    public CartaoParaPagar? CartaoPara(long valorEmCentavos) =>
        CartaoLigado && valorEmCentavos > 0
            ? new CartaoParaPagar(
                ChavePublica!,
                valorEmCentavos + AcrescimoDoCartao(valorEmCentavos),
                AcrescimoDoCartao(valorEmCentavos),
                MeiosDePagamento.ParcelasNoCartao
            )
            : null;

    /// <summary>
    /// Quanto o cartão cobra a mais para a turma receber o valor cheio com a taxa repassada (P2): o valor bruto é
    /// o líquido dividido por (1 − taxa), arredondado para cima; zero quando a turma absorve.
    /// </summary>
    /// <param name="valorEmCentavos">O valor do PIX — o que a turma quer receber.</param>
    public long AcrescimoDoCartao(long valorEmCentavos) =>
        TaxaDoCartaoRepassada is { } taxa && valorEmCentavos > 0
            ? (long)Math.Ceiling(valorEmCentavos * 10_000m / (10_000 - taxa)) - valorEmCentavos
            : 0;
}
