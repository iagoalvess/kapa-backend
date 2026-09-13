namespace Backend.Business.Assinaturas.Models;

/// <summary>Plano como a tela de planos o mostra.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Codigo">Código estável, enviado no checkout.</param>
/// <param name="Nome">Nome do plano.</param>
/// <param name="PrecoEmCentavos">Preço de um ciclo, em centavos.</param>
/// <param name="Ciclo">Periodicidade.</param>
/// <param name="LimiteDeFormandos">Quantos formandos cabem.</param>
/// <param name="Recomendado">Destacado na tela.</param>
public sealed record PlanoResumo(
    Guid Id,
    string Codigo,
    string Nome,
    long PrecoEmCentavos,
    CicloDeCobranca Ciclo,
    int LimiteDeFormandos,
    bool Recomendado
);

/// <summary>A assinatura mais recente da formatura.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Status">Situação.</param>
/// <param name="Plano">Plano contratado.</param>
/// <param name="VigenteAte">Fim da vigência paga, em UTC.</param>
/// <param name="ProximaCobrancaEm">Próxima cobrança automática, em UTC. Nulo se não houver renovação por vir.</param>
/// <param name="CanceladaEm">Quando a renovação foi cancelada, em UTC.</param>
/// <param name="CriadoEm">Início do checkout, em UTC.</param>
public sealed record AssinaturaDetalhe(
    Guid Id,
    StatusDaAssinatura Status,
    PlanoResumo Plano,
    DateTime? VigenteAte,
    DateTime? ProximaCobrancaEm,
    DateTime? CanceladaEm,
    DateTime CriadoEm
);

/// <summary>Pedido de checkout vindo da tela de planos.</summary>
/// <param name="PlanoCodigo">Plano escolhido.</param>
public sealed record IniciarCheckout(string PlanoCodigo);

/// <summary>O que o provedor precisa para montar a página de pagamento.</summary>
/// <remarks>
/// Desacoplado das entidades de propósito: o provedor não precisa, e não deve, conhecer o modelo
/// de dados. <see cref="AssinaturaId"/> vai como referência externa — é por ele que o webhook
/// encontra a assinatura de volta.
/// </remarks>
/// <param name="AssinaturaId">Referência que volta nos eventos.</param>
/// <param name="PlanoCodigo">Plano escolhido.</param>
/// <param name="PlanoNome">Nome do plano, para a página do provedor.</param>
/// <param name="PrecoEmCentavos">Valor de um ciclo, em centavos.</param>
/// <param name="Ciclo">Periodicidade.</param>
/// <param name="UrlDeRetorno">Para onde o provedor devolve o navegador.</param>
public sealed record PedidoDeCheckout(
    Guid AssinaturaId,
    string PlanoCodigo,
    string PlanoNome,
    long PrecoEmCentavos,
    CicloDeCobranca Ciclo,
    string UrlDeRetorno
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
public sealed record EventoDoProvedor(string Id, string Tipo, Guid? AssinaturaId, string? IdExternoDaAssinatura);

/// <summary>Resposta do webhook.</summary>
/// <param name="EventoId">Id do evento recebido.</param>
/// <param name="Duplicado">Se o evento já tinha chegado antes e não foi reprocessado.</param>
public sealed record ReciboDeWebhook(string EventoId, bool Duplicado);

/// <summary>O que uma rodada de conciliação fez.</summary>
/// <param name="Confirmadas">Pagamentos achados no provedor sem webhook.</param>
/// <param name="Vencidas">Assinaturas vencidas e formaturas suspensas.</param>
/// <param name="Avisos">Avisos de vencimento enfileirados.</param>
public sealed record ResumoDaConciliacao(int Confirmadas, int Vencidas, int Avisos);
