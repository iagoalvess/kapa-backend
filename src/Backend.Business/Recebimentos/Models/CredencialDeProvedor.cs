using Backend.Business.Abstractions;

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

    /// <summary>Grava uma autorização recém-concedida, no lugar da que houver.</summary>
    /// <param name="accessToken">Token de acesso.</param>
    /// <param name="refreshToken">Token de renovação.</param>
    /// <param name="expiraEm">Validade do token de acesso, em UTC.</param>
    /// <param name="idNoProvedor">Id da conta no Mercado Pago.</param>
    /// <param name="contaNoProvedor">Nome da conta para a tela.</param>
    /// <param name="usuarioId">Quem autorizou.</param>
    public void Conectar(string accessToken, string refreshToken, DateTime expiraEm, long idNoProvedor, string contaNoProvedor, Guid usuarioId)
    {
        Renovar(accessToken, refreshToken, expiraEm);
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
}
