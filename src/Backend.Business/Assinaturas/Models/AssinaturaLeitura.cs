using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Assinaturas.Models;

/// <summary>Plano como a tela de planos o mostra.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Codigo">Código estável, enviado no checkout.</param>
/// <param name="Nome">Nome do plano.</param>
/// <param name="Descricao">Para que turma o plano serve.</param>
/// <param name="PrecoEmCentavos">Preço de um ciclo, em centavos.</param>
/// <param name="PrecoCheioEmCentavos">Preço sem desconto, em centavos. Nulo quando não há desconto.</param>
/// <param name="Ciclo">Periodicidade.</param>
/// <param name="LimiteDeFormandos">Quantos formandos cabem.</param>
/// <param name="Modulos">Módulos incluídos, na ordem de exibição.</param>
/// <param name="Recomendado">Destacado na tela.</param>
public sealed record PlanoResumo(
    Guid Id,
    string Codigo,
    string Nome,
    string Descricao,
    long PrecoEmCentavos,
    long? PrecoCheioEmCentavos,
    CicloDeCobranca Ciclo,
    int LimiteDeFormandos,
    IReadOnlyList<string> Modulos,
    bool Recomendado
);

/// <summary>O plano que vale para a turma agora: o que a tela lê para trancar área.</summary>
/// <remarks>
/// Diferente de <see cref="AssinaturaDetalhe"/>, que é a assinatura (e não existe no gratuito): aqui sempre há
/// resposta, porque toda turma tem um plano valendo — o contratado ou o gratuito (Sprint 45).
/// </remarks>
/// <param name="Codigo">Código do plano, como no catálogo.</param>
/// <param name="Nome">Nome exibido.</param>
/// <param name="Modulos">Módulos que o plano libera, pelos códigos de <see cref="Modulo"/>.</param>
/// <param name="Pago">Se é um plano contratado em vigor. Falso é o gratuito, e a turma vencida volta a ele.</param>
public sealed record PlanoDaTurma(string Codigo, string Nome, IReadOnlyList<string> Modulos, bool Pago);

/// <summary>A assinatura mais recente da formatura.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Status">Situação.</param>
/// <param name="Plano">Plano contratado.</param>
/// <param name="VigenteAte">Fim da vigência paga, em UTC.</param>
/// <param name="ProximaCobrancaEm">Próxima cobrança automática, em UTC. Nulo se não houver renovação por vir.</param>
/// <param name="CanceladaEm">Quando a renovação foi cancelada, em UTC.</param>
/// <param name="Meio">Cartão recorrente ou PIX avulso.</param>
/// <param name="ProximoPlano">Plano que vale a partir da próxima renovação, quando a turma agendou a descida.</param>
/// <param name="CartaoAguardandoAutorizacao">Se a troca para o cartão espera a autorização na página do provedor.</param>
public sealed record AssinaturaDetalhe(
    Guid Id,
    StatusDaAssinatura Status,
    PlanoResumo Plano,
    DateTime? VigenteAte,
    DateTime? ProximaCobrancaEm,
    DateTime? CanceladaEm,
    MeioDePagamento Meio,
    PlanoResumo? ProximoPlano,
    bool CartaoAguardandoAutorizacao
);

/// <summary>Um pagamento do plano, como o histórico da tela o mostra.</summary>
/// <param name="Id">Cobrança.</param>
/// <param name="PlanoNome">Plano pago.</param>
/// <param name="Motivo">Ciclo ou diferença de plano.</param>
/// <param name="Meio">Meio.</param>
/// <param name="ValorEmCentavos">Valor.</param>
/// <param name="Situacao">Aberta, paga, cancelada ou estornada.</param>
/// <param name="Url">Página de pagamento, enquanto aberta.</param>
/// <param name="CriadaEm">Quando nasceu, em UTC.</param>
/// <param name="PagaEm">Quando foi paga, em UTC.</param>
/// <param name="ValorEstornadoEmCentavos">Quanto voltou, se estornada.</param>
/// <param name="EstornadaEm">Quando foi estornada, em UTC.</param>
public sealed record CobrancaDoPlanoResumo(
    Guid Id,
    string PlanoNome,
    MotivoDaCobranca Motivo,
    MeioDePagamento Meio,
    long ValorEmCentavos,
    SituacaoDaCobrancaDoPlano Situacao,
    string? Url,
    DateTime CriadaEm,
    DateTime? PagaEm,
    long? ValorEstornadoEmCentavos,
    DateTime? EstornadaEm
);

/// <summary>Pedido de checkout vindo da tela de planos.</summary>
/// <param name="PlanoCodigo">Plano escolhido.</param>
/// <param name="Meio">Cartão recorrente ou PIX avulso. Nulo é cartão — o contrato de antes da Sprint 37.</param>
/// <param name="EmailDoPagador">E-mail de quem contrata: o provedor exige na recorrência.</param>
public sealed record IniciarCheckout(string PlanoCodigo, MeioDePagamento? Meio = null, string? EmailDoPagador = null);

/// <summary>Pedido de troca de plano vindo do cartão da assinatura.</summary>
/// <param name="PlanoCodigo">Plano novo, do mesmo ciclo.</param>
public sealed record TrocaDePlano(string PlanoCodigo);

/// <summary>O que a troca de plano ou de meio deu.</summary>
/// <param name="Url">Página do provedor para pagar a diferença ou autorizar o cartão; nula quando nada precisa ser pago agora.</param>
public sealed record ResultadoDaTroca(string? Url);

/// <summary>O que o provedor precisa para montar a página de pagamento.</summary>
/// <remarks>
/// Desacoplado das entidades de propósito: o provedor não precisa, e não deve, conhecer o modelo
/// de dados. <see cref="AssinaturaId"/> vai como referência externa — é por ele que o webhook
/// encontra a assinatura de volta.
/// <para>
/// Dois pedidos num tipo só (Sprint 37): sem <see cref="CobrancaId"/> é a recorrência no cartão; com ele, uma
/// cobrança avulsa — o PIX de um ciclo ou a diferença da subida de plano —, e a referência externa é a cobrança.
/// </para>
/// </remarks>
/// <param name="AssinaturaId">Referência que volta nos eventos.</param>
/// <param name="PlanoCodigo">Plano escolhido.</param>
/// <param name="PlanoNome">Nome do plano, para a página do provedor.</param>
/// <param name="PrecoEmCentavos">Valor de um ciclo, em centavos.</param>
/// <param name="Ciclo">Periodicidade.</param>
/// <param name="UrlDeRetorno">Para onde o provedor devolve o navegador.</param>
/// <param name="Meio">Cartão ou PIX.</param>
/// <param name="EmailDoPagador">Quem paga; a recorrência exige.</param>
/// <param name="CobrancaId">A cobrança avulsa; nulo na recorrência.</param>
/// <param name="ComecaEm">Primeiro débito da recorrência, em UTC; nulo é agora. É a troca de meio (P5): sem cobrança em dobro.</param>
public sealed record PedidoDeCheckout(
    Guid AssinaturaId,
    string PlanoCodigo,
    string PlanoNome,
    long PrecoEmCentavos,
    CicloDeCobranca Ciclo,
    string UrlDeRetorno,
    MeioDePagamento Meio = MeioDePagamento.Cartao,
    string? EmailDoPagador = null,
    Guid? CobrancaId = null,
    DateTime? ComecaEm = null
);

/// <summary>Sessão de pagamento criada no provedor.</summary>
/// <param name="IdExterno">Id da sessão (ou assinatura) no provedor.</param>
/// <param name="Url">Página hospedada do provedor, para onde o navegador vai.</param>
public sealed record SessaoDeCheckout(string IdExterno, string Url);

/// <summary>Evento do provedor, já verificado e traduzido para o vocabulário do domínio.</summary>
/// <param name="Id">Id do evento no provedor — a chave de idempotência.</param>
/// <param name="Tipo">Tipo, em <see cref="TiposDeEvento"/> quando reconhecido.</param>
/// <param name="AssinaturaId">Referência enviada no checkout.</param>
/// <param name="IdExternoDaAssinatura">Id da assinatura no provedor, quando ele informa.</param>
/// <param name="OcorridoEm">
/// Quando o evento aconteceu <b>no provedor</b>, em UTC. É o que ordena os eventos entre si.
/// </param>
/// <param name="CobrancaId">A cobrança avulsa paga, quando o pagamento é de uma (PIX do ciclo, diferença).</param>
/// <param name="IdDoPagamento">O id do pagamento no provedor — o que o estorno usa.</param>
/// <param name="ValorEmCentavos">O que o provedor creditou.</param>
/// <remarks>
/// <see cref="OcorridoEm"/> não é o momento em que o webhook chegou: reentrega, fila do PSP e
/// retentativa fazem um evento antigo chegar depois de um novo, e aplicar na ordem de chegada
/// desfaz o estado — "fatura criada" depois de "paga" devolve a turma para pendente. Quem compara
/// é <c>WebhookService</c>, contra <c>Assinatura.UltimoEventoEm</c>.
/// <para>
/// Nulo quando o PSP não informa data. Aí não há como ordenar, e o evento é aplicado: recusar todo
/// evento sem data pararia a cobrança inteira de um provedor que simplesmente não manda o campo.
/// </para>
/// </remarks>
public sealed record EventoDoProvedor(
    string Id,
    string Tipo,
    Guid? AssinaturaId,
    string? IdExternoDaAssinatura,
    DateTime? OcorridoEm = null,
    Guid? CobrancaId = null,
    string? IdDoPagamento = null,
    long? ValorEmCentavos = null
);

/// <summary>Resposta do webhook.</summary>
/// <param name="EventoId">Id do evento recebido.</param>
/// <param name="Duplicado">Se o evento já tinha chegado antes e não foi reprocessado.</param>
public sealed record ReciboDeWebhook(string EventoId, bool Duplicado);

/// <summary>O que uma rodada de conciliação fez.</summary>
/// <param name="Confirmadas">Pagamentos achados no provedor sem webhook.</param>
/// <param name="Renovadas">Renovações achadas no provedor sem webhook, pouco antes de suspender.</param>
/// <param name="Vencidas">Assinaturas vencidas e formaturas suspensas.</param>
/// <param name="Avisos">Avisos de vencimento enfileirados.</param>
public sealed record ResumoDaConciliacao(int Confirmadas, int Renovadas, int Vencidas, int Avisos);
