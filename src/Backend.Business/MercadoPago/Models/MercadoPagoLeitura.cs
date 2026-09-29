using Backend.Business.Pagamentos.Models;

namespace Backend.Business.MercadoPago.Models;

/// <summary>A conta do Mercado Pago dona de um token, como a API dele responde.</summary>
/// <param name="Id">Id do usuário lá.</param>
/// <param name="Nome">E-mail ou apelido — o que a tela mostra para a conta ser reconhecida.</param>
/// <param name="Teste">Se é usuário de teste do sandbox.</param>
public sealed record ContaNoMercadoPago(long Id, string Nome, bool Teste);

/// <summary>Os tokens que o OAuth devolve, na autorização ou na renovação.</summary>
/// <param name="AccessToken">Token de acesso.</param>
/// <param name="RefreshToken">Token de renovação.</param>
/// <param name="IdDoUsuario">A conta que autorizou.</param>
/// <param name="ExpiraEm">Validade do token de acesso, em UTC.</param>
/// <param name="ChavePublica">
/// A <c>public_key</c> da conta que autorizou: o formulário de cartão do navegador tokeniza com ela, e o token só
/// vale para cobrar nessa conta (Sprint 39). Não é segredo.
/// </param>
public sealed record TokensDoMercadoPago(string AccessToken, string RefreshToken, long IdDoUsuario, DateTime ExpiraEm, string? ChavePublica = null);

/// <summary>Quem paga. O Mercado Pago exige o e-mail em todo pedido; o nome ajuda a turma a reconhecer o comprador.</summary>
/// <param name="Email">E-mail.</param>
/// <param name="Nome">Primeiro nome.</param>
/// <param name="Sobrenome">O resto do nome.</param>
public sealed record PagadorNoMercadoPago(string Email, string? Nome = null, string? Sobrenome = null);

/// <summary>O cartão como o SDK do Mercado Pago o devolve no navegador — o número nunca passa pelo Kapa.</summary>
/// <param name="Token">O token de uso único do cartão.</param>
/// <param name="Bandeira">O <c>payment_method_id</c> que o SDK identificou (<c>visa</c>, <c>master</c>…).</param>
/// <param name="Parcelas">Em quantas vezes.</param>
public sealed record CartaoTokenizado(string Token, string Bandeira, int Parcelas);

/// <summary>O que o Kapa pede ao Mercado Pago para cobrar uma vez, pela API de Orders.</summary>
/// <param name="Referencia">O id da cobrança no Kapa: chave de idempotência e referência externa.</param>
/// <param name="Meio">PIX ou cartão. A cobrança recorrente vai por <see cref="PedidoDeRecorrencia"/>.</param>
/// <param name="ValorEmCentavos">Valor.</param>
/// <param name="Pagador">Quem paga.</param>
/// <param name="Validade">Por quanto tempo o PIX aceita pagamento; o cartão ignora.</param>
/// <param name="Cartao">O cartão tokenizado, só no cartão.</param>
public sealed record PedidoDeCobranca(
    Guid Referencia,
    MeioDePagamento Meio,
    long ValorEmCentavos,
    PagadorNoMercadoPago Pagador,
    TimeSpan Validade,
    CartaoTokenizado? Cartao = null
);

/// <summary>O pedido que o Mercado Pago criou.</summary>
/// <param name="IdExterno">Id do pedido lá.</param>
/// <param name="CopiaECola">O BR Code, no PIX; nulo no cartão.</param>
/// <param name="Situacao">No cartão a resposta já diz se foi aprovado; o PIX nasce aberto.</param>
public sealed record DocumentoEmitido(string IdExterno, string? CopiaECola, SituacaoDoPedido Situacao = SituacaoDoPedido.Aberto);

/// <summary>Em que pé está um pedido, pelo que o Mercado Pago responde agora.</summary>
public enum SituacaoDoPedido
{
    /// <summary>Aguardando o pagamento.</summary>
    Aberto,

    /// <summary>Pago e creditado na conta.</summary>
    Pago,

    /// <summary>Venceu, foi cancelado ou recusado: não vai mais ser pago.</summary>
    Encerrado,

    /// <summary>Foi pago e devolvido por inteiro ao pagador — pela turma, no painel do Mercado Pago (Sprint 39).</summary>
    Devolvido,

    /// <summary>Foi pago e o pagador contestou no cartão (chargeback): o dinheiro saiu da conta da turma (Sprint 39, P4).</summary>
    Contestado,
}

/// <summary>Um pedido consultado no Mercado Pago — a única fonte do valor pago (decisão 12 da Sprint 25).</summary>
/// <param name="IdExterno">Id do pedido lá.</param>
/// <param name="Referencia">A referência externa — o id da cobrança no Kapa.</param>
/// <param name="Situacao">Aberto, pago ou encerrado.</param>
/// <param name="ValorPagoEmCentavos">O que foi creditado; zero enquanto não pago.</param>
/// <param name="PagoEm">Quando o pagamento foi aprovado, em UTC; nulo enquanto não pago.</param>
/// <param name="CpfDoPagador">O CPF do pagador, quando o Mercado Pago informa — o sinal da P6 da Sprint 26.</param>
public sealed record PedidoConsultado(
    string IdExterno,
    string? Referencia,
    SituacaoDoPedido Situacao,
    long ValorPagoEmCentavos,
    DateTime? PagoEm,
    string? CpfDoPagador = null
);

/// <summary>
/// Uma cobrança recorrente de valor fixo (<c>preapproval</c>): a pessoa cadastra o cartão uma vez, na página do
/// Mercado Pago, e o débito sai a cada ciclo. A Sprint 37 usa primeiro, nos planos.
/// </summary>
/// <param name="Referencia">O id no Kapa — a referência externa, que volta nos avisos.</param>
/// <param name="Motivo">O que aparece para quem paga ("Kapa — plano Essencial").</param>
/// <param name="ValorEmCentavos">O valor de cada ciclo.</param>
/// <param name="EmailDoPagador">E-mail de quem paga.</param>
/// <param name="MesesPorCiclo">De quantos em quantos meses cobra — 1 no mensal, 12 no anual.</param>
/// <param name="UrlDeRetorno">Para onde o navegador volta depois de autorizar.</param>
/// <param name="ComecaEm">Primeiro débito, em UTC; nulo é na autorização.</param>
public sealed record PedidoDeRecorrencia(
    Guid Referencia,
    string Motivo,
    long ValorEmCentavos,
    string EmailDoPagador,
    int MesesPorCiclo,
    string UrlDeRetorno,
    DateTime? ComecaEm = null
);

/// <summary>
/// Um pagamento avulso na página do Mercado Pago (Checkout Pro): o PIX de um ciclo do plano ou a diferença da
/// subida de plano (Sprint 37). O Kapa não vê cartão nem desenha QR — a página é deles (P1).
/// </summary>
/// <param name="Referencia">O id no Kapa — referência externa e chave de idempotência.</param>
/// <param name="Titulo">O que aparece para quem paga ("Kapa — plano Premium").</param>
/// <param name="ValorEmCentavos">Valor.</param>
/// <param name="Meio">Só PIX, ou só cartão de crédito à vista.</param>
/// <param name="EmailDoPagador">Quem paga, quando se sabe.</param>
/// <param name="UrlDeRetorno">Para onde o navegador volta.</param>
/// <param name="Validade">Até quando a página aceita pagamento, em UTC.</param>
public sealed record PedidoDePagamentoAvulso(
    Guid Referencia,
    string Titulo,
    long ValorEmCentavos,
    MeioDePagamento Meio,
    string? EmailDoPagador,
    string UrlDeRetorno,
    DateTime Validade
);

/// <summary>A página de pagamento criada no Mercado Pago.</summary>
/// <param name="IdExterno">Id da preferência.</param>
/// <param name="Url">A página (<c>init_point</c>).</param>
public sealed record PaginaDePagamento(string IdExterno, string Url);

/// <summary>Em que pé está um pagamento (<c>v1/payments</c>).</summary>
public enum SituacaoDoPagamento
{
    /// <summary>Esperando: PIX não pago, cartão em análise.</summary>
    Pendente,

    /// <summary>Aprovado e creditado.</summary>
    Aprovado,

    /// <summary>Recusado, cancelado ou vencido.</summary>
    Recusado,

    /// <summary>Devolvido, total ou em parte, ou contestado.</summary>
    Devolvido,
}

/// <summary>Um pagamento consultado no Mercado Pago.</summary>
/// <param name="Id">Id do pagamento lá.</param>
/// <param name="Referencia">A referência externa.</param>
/// <param name="Situacao">Pendente, aprovado, recusado ou devolvido.</param>
/// <param name="ValorEmCentavos">O valor da transação.</param>
/// <param name="AprovadoEm">Quando foi aprovado, em UTC.</param>
/// <param name="DaRecorrencia">Se foi gerado por uma recorrência (<c>recurring_payment</c>) — esse chega também pelo aviso do débito.</param>
/// <param name="LiquidoEmCentavos">
/// O que cai na conta depois da tarifa (<c>net_received_amount</c>); nulo quando o Mercado Pago não informa. A
/// diferença para <see cref="ValorEmCentavos"/> é a tarifa que o caixa lança (Sprint 39, P6).
/// </param>
public sealed record PagamentoNoMercadoPago(
    string Id,
    string? Referencia,
    SituacaoDoPagamento Situacao,
    long ValorEmCentavos,
    DateTime? AprovadoEm,
    bool DaRecorrencia,
    long? LiquidoEmCentavos = null
)
{
    /// <summary>A tarifa do Mercado Pago — o valor menos o líquido; zero quando o líquido não veio.</summary>
    public long TarifaEmCentavos => LiquidoEmCentavos is { } liquido ? Math.Max(0, ValorEmCentavos - liquido) : 0;
}

/// <summary>Um débito de uma recorrência (<c>authorized_payments</c>): a fatura de um ciclo e o pagamento dela.</summary>
/// <param name="Id">Id do débito.</param>
/// <param name="IdDaRecorrencia">A recorrência.</param>
/// <param name="IdDoPagamento">O pagamento, quando já houve tentativa.</param>
/// <param name="Situacao">Situação do pagamento; pendente enquanto não houve tentativa.</param>
/// <param name="ValorEmCentavos">Valor do débito.</param>
/// <param name="OcorridoEm">Última mudança, em UTC.</param>
public sealed record DebitoDaRecorrencia(
    string Id,
    string? IdDaRecorrencia,
    string? IdDoPagamento,
    SituacaoDoPagamento Situacao,
    long ValorEmCentavos,
    DateTime? OcorridoEm
);

/// <summary>Em que pé está uma recorrência no Mercado Pago.</summary>
public enum SituacaoDaRecorrencia
{
    /// <summary>Criada, esperando a pessoa autorizar.</summary>
    Pendente,

    /// <summary>Autorizada: cobra sozinha a cada ciclo.</summary>
    Autorizada,

    /// <summary>Pausada pelo dono da conta.</summary>
    Pausada,

    /// <summary>Cancelada: não cobra mais.</summary>
    Cancelada,
}

/// <summary>Uma recorrência, criada ou consultada.</summary>
/// <param name="IdExterno">Id da recorrência lá.</param>
/// <param name="Referencia">A referência externa — o id no Kapa.</param>
/// <param name="Situacao">Pendente, autorizada, pausada ou cancelada.</param>
/// <param name="UrlParaAutorizar">A página onde a pessoa autoriza (<c>init_point</c>).</param>
/// <param name="ProximaCobrancaEm">Quando sai o próximo débito, em UTC; nulo se não houver.</param>
public sealed record RecorrenciaNoMercadoPago(
    string IdExterno,
    string? Referencia,
    SituacaoDaRecorrencia Situacao,
    string? UrlParaAutorizar,
    DateTime? ProximaCobrancaEm
);
