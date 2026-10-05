using Backend.Business.Abstractions;

namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// Um pacote na cesta de um formando: o que ele escolheu contratar na adesão (Sprint 47).
/// </summary>
/// <remarks>
/// A prova do que foi contratado é o snapshot da adesão, no hash; esta linha é o mesmo fato em forma de consulta —
/// é ela que a emissão de convites, a portaria, o painel do evento e o rateio escopado (Sprint 48, D19) leem, sem
/// abrir JSON de cada adesão. Nasce na adesão ou no aditivo (D38), na mesma transação das parcelas, e sai quando a
/// comissão aprova o cancelamento do pacote (D8).
/// </remarks>
public class EscolhaDaCesta : EntidadeDaFormatura
{
    /// <summary>Quem escolheu.</summary>
    public Guid VinculoId { get; private set; }

    /// <summary>O pacote escolhido.</summary>
    public Guid ItemDeCobrancaId { get; private set; }

    /// <summary>
    /// Detalhe livre de quem escolheu — tamanho da beca, nome no convite (Sprint 48, D40).
    /// </summary>
    /// <remarks>Fora do snapshot e do hash: é detalhe de entrega, não preço, e corrigi-lo não muda o contrato.</remarks>
    public string? Observacao { get; private set; }

    /// <summary>A escolha de um pacote por um vínculo.</summary>
    /// <param name="vinculoId">Quem escolheu.</param>
    /// <param name="itemDeCobrancaId">Pacote.</param>
    /// <param name="observacao">Detalhe livre, se houver.</param>
    public static EscolhaDaCesta Nova(Guid vinculoId, Guid itemDeCobrancaId, string? observacao = null) =>
        new()
        {
            VinculoId = vinculoId,
            ItemDeCobrancaId = itemDeCobrancaId,
            Observacao = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim(),
        };
}
