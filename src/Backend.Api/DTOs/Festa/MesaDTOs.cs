using Backend.Business.Festa.Models;

namespace Backend.Api.DTOs.Festa;

/// <summary>Corpo do cadastro de uma mesa.</summary>
/// <param name="Identificacao">"Mesa 12", "Mesa dos pais".</param>
/// <param name="Lugares">Quantos lugares.</param>
/// <param name="Observacao">Observação livre.</param>
/// <param name="Reservada">Fora da venda; ausente vale falso.</param>
/// <param name="Formato">Redonda ou retangular; ausente vale redonda.</param>
public sealed record MesaRequestDTO(string? Identificacao, int Lugares, string? Observacao, bool? Reservada, FormatoDaMesa? Formato = null);

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
/// <param name="Formato">Redonda ou retangular.</param>
/// <param name="X">Centro no salão, em centímetros; nulo fora do mapa.</param>
/// <param name="Y">Centro no salão, em centímetros; nulo fora do mapa.</param>
/// <param name="Girada">Retangular em pé.</param>
public sealed record MesaDTO(
    Guid Id,
    string Identificacao,
    int Lugares,
    string? Observacao,
    bool Reservada,
    Guid? VinculoId,
    string? Dono,
    FormatoDaMesa Formato,
    int? X,
    int? Y,
    bool Girada
);

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
/// <param name="Salao">Tamanho do salão e o que há nele.</param>
public sealed record MapaDeMesasDTO(
    int Mesas,
    int Lugares,
    int Reservadas,
    int ComDono,
    int MesasPorAtribuir,
    IReadOnlyList<MesaDTO> Lista,
    IReadOnlyList<CompradorDeMesaDTO> Compradores,
    PlantaDoSalaoDTO Salao
);

/// <summary>Um retângulo do salão: palco, pista, entrada, área.</summary>
/// <param name="Tipo">O que ele é.</param>
/// <param name="Rotulo">O nome escrito no mapa.</param>
/// <param name="X">Canto esquerdo, em centímetros.</param>
/// <param name="Y">Canto de cima, em centímetros.</param>
/// <param name="Largura">Largura, em centímetros.</param>
/// <param name="Altura">Altura, em centímetros.</param>
/// <param name="Cor">Cor da área; nula no resto.</param>
public sealed record ElementoDoSalaoDTO(TipoDeElemento Tipo, string? Rotulo, int X, int Y, int Largura, int Altura, CorDaArea? Cor);

/// <summary>O salão: tamanho e elementos.</summary>
/// <param name="Largura">Largura, em centímetros.</param>
/// <param name="Altura">Profundidade, em centímetros.</param>
/// <param name="Elementos">Palco, pista e o resto, na ordem de desenho.</param>
public sealed record PlantaDoSalaoDTO(int Largura, int Altura, IReadOnlyList<ElementoDoSalaoDTO> Elementos);

/// <summary>Onde fica uma mesa.</summary>
/// <param name="MesaId">Mesa.</param>
/// <param name="X">Centro; nulo tira a mesa do mapa.</param>
/// <param name="Y">Centro; nulo tira a mesa do mapa.</param>
/// <param name="Girada">Retangular em pé; ausente vale falso.</param>
public sealed record PosicaoDaMesaDTO(Guid MesaId, int? X, int? Y, bool? Girada);

/// <summary>Corpo do "Salvar mapa": os elementos e o lugar das mesas que mudaram.</summary>
/// <param name="Elementos">Todos os elementos; os que não vêm saem do mapa.</param>
/// <param name="Posicoes">As mesas a mover; a que não vem fica onde está.</param>
public sealed record SalaoRequestDTO(IReadOnlyList<ElementoDoSalaoDTO>? Elementos, IReadOnlyList<PosicaoDaMesaDTO>? Posicoes);

/// <summary>Uma mesa no mapa do formando, sem o dono.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Identificacao">"Mesa 12".</param>
/// <param name="Lugares">Quantos lugares.</param>
/// <param name="Reservada">Fora da venda.</param>
/// <param name="Formato">Redonda ou retangular.</param>
/// <param name="X">Centro; nulo fora do mapa.</param>
/// <param name="Y">Centro; nulo fora do mapa.</param>
/// <param name="Girada">Retangular em pé.</param>
/// <param name="Minha">A mesa é de quem lê.</param>
public sealed record MesaNoSalaoDTO(
    Guid Id,
    string Identificacao,
    int Lugares,
    bool Reservada,
    FormatoDaMesa Formato,
    int? X,
    int? Y,
    bool Girada,
    bool Minha
);

/// <summary>O mapa do formando: o salão e as mesas, com as dele marcadas.</summary>
/// <param name="Salao">Tamanho e elementos.</param>
/// <param name="Mesas">Todas as mesas da turma, sem o dono.</param>
public sealed record SalaoDoFormandoDTO(PlantaDoSalaoDTO Salao, IReadOnlyList<MesaNoSalaoDTO> Mesas);
