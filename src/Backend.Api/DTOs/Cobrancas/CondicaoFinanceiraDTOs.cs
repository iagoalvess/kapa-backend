namespace Backend.Api.DTOs.Cobrancas;

/// <summary>Um benefício da condição financeira (Sprint 54): um pacote e quanto desconta.</summary>
/// <param name="ItemDeCobrancaId">Pacote; num grupo de faixas, a faixa até cujo preço o benefício vale.</param>
/// <param name="Percentual">Percentual, base 10.000 (<c>10000</c> é isenção). Informe este ou o valor.</param>
/// <param name="ValorEmCentavos">Valor fixo, em centavos. Informe este ou o percentual.</param>
public sealed record BeneficioDaCondicaoDTO(Guid ItemDeCobrancaId, int? Percentual, long? ValorEmCentavos);

/// <summary>Corpo para conceder ou alterar a condição financeira.</summary>
/// <param name="Beneficios">Os pacotes alcançados, um por pacote ou grupo de faixas.</param>
public sealed record CondicaoFinanceiraRequestDTO(IReadOnlyList<BeneficioDaCondicaoDTO>? Beneficios);

/// <summary>A condição da pessoa; <c>condicao</c> nula quando não há.</summary>
/// <param name="Condicao">A condição, se houver.</param>
public sealed record CondicaoDaPessoaDTO(CondicaoLidaDTO? Condicao);

/// <summary>Uma condição financeira, com o efeito de cada benefício no catálogo de hoje.</summary>
/// <param name="Beneficios">Os pacotes alcançados.</param>
/// <param name="AtualizadaEm">Última alteração, em UTC.</param>
public sealed record CondicaoLidaDTO(IReadOnlyList<BeneficioLidoDTO> Beneficios, DateTime AtualizadaEm);

/// <summary>Um benefício, com o pacote e o desconto que dá nele.</summary>
/// <param name="ItemDeCobrancaId">Pacote apontado.</param>
/// <param name="Rotulo">"Festa — 10 pessoas"; nulo se o pacote saiu do catálogo.</param>
/// <param name="Percentual">Percentual, base 10.000.</param>
/// <param name="ValorEmCentavos">Valor fixo.</param>
/// <param name="PrecoEmCentavos">Preço do pacote hoje.</param>
/// <param name="DescontoEmCentavos">Quanto o benefício desconta dele.</param>
public sealed record BeneficioLidoDTO(
    Guid ItemDeCobrancaId,
    string? Rotulo,
    int? Percentual,
    long? ValorEmCentavos,
    long PrecoEmCentavos,
    long DescontoEmCentavos
);
