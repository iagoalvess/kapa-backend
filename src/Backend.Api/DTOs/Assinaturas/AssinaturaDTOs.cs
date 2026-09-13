using Backend.Business.Assinaturas.Models;

namespace Backend.Api.DTOs.Assinaturas;

/// <summary>Plano contratável.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Codigo">Código enviado no checkout.</param>
/// <param name="Nome">Nome do plano.</param>
/// <param name="PrecoEmCentavos">Preço de um ciclo, em centavos — <c>34990</c> é R$ 349,90.</param>
/// <param name="Ciclo"><c>Mensal</c> ou <c>Anual</c>.</param>
/// <param name="LimiteDeFormandos">Quantos formandos cabem.</param>
/// <param name="Recomendado">Destacado na tela.</param>
public sealed record PlanoDTO(
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
/// <param name="Status"><c>Pendente</c>, <c>Ativa</c>, <c>Vencida</c> ou <c>Cancelada</c>.</param>
/// <param name="Plano">Plano contratado.</param>
/// <param name="VigenteAte">Fim da vigência paga, em UTC.</param>
/// <param name="ProximaCobrancaEm">Próxima cobrança automática, em UTC; nulo se não houver.</param>
/// <param name="CanceladaEm">Quando a renovação foi cancelada, em UTC.</param>
/// <param name="CriadoEm">Início do checkout, em UTC.</param>
public sealed record AssinaturaDTO(
    Guid Id,
    StatusDaAssinatura Status,
    PlanoDTO Plano,
    DateTime? VigenteAte,
    DateTime? ProximaCobrancaEm,
    DateTime? CanceladaEm,
    DateTime CriadoEm
);

/// <summary>Corpo do checkout.</summary>
/// <param name="PlanoCodigo">Plano escolhido.</param>
public sealed record IniciarCheckoutRequestDTO(string PlanoCodigo);

/// <summary>Sessão de pagamento criada.</summary>
/// <param name="Url">Página do provedor, para onde o navegador deve ir.</param>
public sealed record CheckoutDTO(string Url);

/// <summary>Recibo do webhook.</summary>
/// <param name="EventoId">Id do evento recebido.</param>
/// <param name="Duplicado">Se já tinha chegado antes e não foi reprocessado.</param>
public sealed record ReciboDeWebhookDTO(string EventoId, bool Duplicado);
