namespace Backend.Api.DTOs.Festa;

/// <summary>Corpo do cadastro de uma mesa.</summary>
/// <param name="Identificacao">"Mesa 12", "Mesa dos pais".</param>
/// <param name="Lugares">Quantos lugares.</param>
/// <param name="Observacao">Observação livre.</param>
/// <param name="Reservada">Fora da venda; ausente vale falso.</param>
public sealed record MesaRequestDTO(string? Identificacao, int Lugares, string? Observacao, bool? Reservada);

/// <summary>Corpo da atribuição da mesa.</summary>
/// <param name="VinculoId">Formando que comprou; nulo solta a mesa.</param>
public sealed record DonoDaMesaRequestDTO(Guid? VinculoId);

/// <summary>Uma mesa.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Identificacao">"Mesa 12".</param>
/// <param name="Lugares">Quantos lugares.</param>
/// <param name="Observacao">Observação.</param>
/// <param name="Reservada">Fora da venda.</param>
/// <param name="VinculoId">Dono; nulo sem dono.</param>
/// <param name="Dono">Nome do dono.</param>
public sealed record MesaDTO(Guid Id, string Identificacao, int Lugares, string? Observacao, bool Reservada, Guid? VinculoId, string? Dono);

/// <summary>Quem comprou mesa, e quantas já tem no mapa.</summary>
/// <param name="VinculoId">Formando.</param>
/// <param name="Nome">Nome.</param>
/// <param name="Compradas">Quantidade confirmada nos pedidos de mesa.</param>
/// <param name="Atribuidas">Mesas já dele.</param>
public sealed record CompradorDeMesaDTO(Guid VinculoId, string Nome, int Compradas, int Atribuidas);

/// <summary>O mapa de mesas: a faixa, as mesas e os compradores.</summary>
/// <param name="Mesas">Quantas mesas.</param>
/// <param name="Lugares">Soma dos lugares.</param>
/// <param name="Reservadas">Mesas fora da venda.</param>
/// <param name="ComDono">Mesas vendidas já atribuídas.</param>
/// <param name="MesasPorAtribuir">Mesas compradas ainda sem mesa no mapa.</param>
/// <param name="Lista">As mesas.</param>
/// <param name="Compradores">Quem comprou mesa.</param>
public sealed record MapaDeMesasDTO(
    int Mesas,
    int Lugares,
    int Reservadas,
    int ComDono,
    int MesasPorAtribuir,
    IReadOnlyList<MesaDTO> Lista,
    IReadOnlyList<CompradorDeMesaDTO> Compradores
);
