namespace Backend.Business.Common;

/// <summary>
/// As telas do front-end para onde os e-mails e as notificações apontam.
/// </summary>
/// <remarks>
/// O back-end não tem tela, mas manda link para várias. Com o caminho escrito à mão em cada e-mail,
/// uma rota renomeada no front deixava metade dos links quebrados e a outra metade certa. Monte a URL
/// com <see cref="AplicacaoSettings.Link"/>.
/// </remarks>
public static class RotasDoFront
{
    /// <summary>Extrato do formando.</summary>
    public const string MinhasParcelas = "/minhas-parcelas";

    /// <summary>O recibo de um recebimento (Sprint 22) — <c>/recibos/:id</c>; o id vai logo depois da barra.</summary>
    public const string Recibo = "/recibos/";

    /// <summary>Termo de adesão do formando.</summary>
    public const string MeuTermo = "/meu-termo";

    /// <summary>Painel da formatura.</summary>
    public const string Formatura = "/formatura";

    /// <summary>Membros da formatura.</summary>
    public const string MembrosDaFormatura = "/formatura/membros";

    /// <summary>Conferência de pagamentos da tesouraria.</summary>
    public const string Conferencia = "/financeiro/conferencia";

    /// <summary>As parcelas da turma, na gestão — onde a tesouraria vê o estorno do Mercado Pago (Sprint 39).</summary>
    public const string ParcelasDaTurma = "/cobrancas/parcelas";

    /// <summary>Lembretes da régua de cobrança.</summary>
    public const string Lembretes = "/notificacoes/lembretes";

    /// <summary>Portal de privacidade do titular.</summary>
    public const string MinhaPrivacidade = "/minha-privacidade";

    /// <summary>O plano de cobrança da turma — o próximo passo de "criou e não voltou" (Sprint 40).</summary>
    public const string PlanoDeCobranca = "/cobrancas";

    /// <summary>Os planos do Kapa, de onde a turma contrata.</summary>
    public const string Planos = "/assinatura/planos";

    /// <summary>A página pública "Não quero mais receber" do e-mail de marketing (Sprint 40), com o token na query.</summary>
    public const string Descadastro = "/descadastro";

    /// <summary>Tela de aceite de convite no front-end (<c>ROTAS.convite</c>); o token vai logo depois da barra.</summary>
    public const string Convite = "/convite/";

    /// <summary>A página pública do convite da festa, com o token no fim (Sprint 21, decisão 1) — <c>/ingresso/:token</c>.</summary>
    public const string Ingresso = "/ingresso/";

    /// <summary>A loja pública da turma, com o id dela no fim (Sprint 26) — <c>/loja/:formaturaId</c>.</summary>
    public const string Loja = "/loja/";

    /// <summary>A compra da loja pelo link de acesso, com o token no fim (Sprint 26, decisão 10) — <c>/compra/:token</c>.</summary>
    public const string Compra = "/compra/";

    /// <summary>As compras da loja, do lado da Gestão — a fila de pedidos de cancelamento (Sprint 38).</summary>
    public const string ComprasDaLoja = "/cobrancas/loja";
}

/// <summary>
/// As páginas do site (<c>kapaformaturas.com.br</c>) para onde os e-mails apontam — as que o front
/// lista em <c>ROTAS_DO_SITE</c>. Monte a URL com <see cref="AplicacaoSettings.LinkDoSite"/>.
/// </summary>
public static class RotasDoSite
{
    /// <summary>A Política de Privacidade vigente.</summary>
    public const string Privacidade = "/privacidade";
}
