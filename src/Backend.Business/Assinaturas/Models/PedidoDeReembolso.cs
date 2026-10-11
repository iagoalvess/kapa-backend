using Backend.Business.Abstractions;
using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Assinaturas.Models;

/// <summary>Solicitação de devolução da assinatura, registrada para análise manual pelo suporte.</summary>
/// <remarks>Os dados do pagamento são copiados do servidor. Registrar o pedido não altera a assinatura nem chama o provedor.</remarks>
public sealed class PedidoDeReembolso : EntidadeDaFormatura
{
    /// <summary>Assinatura solicitada.</summary>
    public Guid AssinaturaId { get; init; }

    /// <summary>Ciclo pago a que se refere o pedido.</summary>
    public Guid CobrancaId { get; init; }

    /// <summary>Presidente que solicitou.</summary>
    public Guid SolicitanteId { get; init; }

    /// <summary>E-mail da sessão no momento do pedido, para o contato do suporte.</summary>
    public string? EmailDoSolicitante { get; init; }

    /// <summary>Motivo informado pelo solicitante.</summary>
    public string Motivo { get; init; } = string.Empty;

    /// <summary>Referência do pagamento no provedor.</summary>
    public string IdDoPagamento { get; init; } = string.Empty;

    /// <summary>Valor efetivamente pago, incluindo desconto, em centavos.</summary>
    public long ValorEmCentavos { get; init; }

    /// <summary>Meio do pagamento original.</summary>
    public MeioDePagamento Meio { get; init; }

    /// <summary>Data do pagamento original, em UTC.</summary>
    public DateTime PagaEm { get; init; }

    /// <summary>Situação da análise manual.</summary>
    public StatusDoPedidoDeReembolso Status { get; set; } = StatusDoPedidoDeReembolso.Pendente;
}

/// <summary>Estados do pedido de análise manual.</summary>
public enum StatusDoPedidoDeReembolso
{
    Pendente,
    Concluido,
    Recusado,
}

/// <summary>Motivo vindo da tela e contato obtido da sessão autenticada.</summary>
public sealed record PedidoDeReembolsoDados(string? Motivo, string? EmailDoSolicitante);

/// <summary>Protocolo e situação do pedido, para acompanhamento nos planos.</summary>
public sealed record PedidoDeReembolsoResumo(Guid Id, StatusDoPedidoDeReembolso Status, DateTime SolicitadoEm);
