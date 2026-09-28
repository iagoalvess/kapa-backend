using Backend.Api.DTOs.Festa;
using Backend.Business.Loja.Models;
using Backend.Business.Pagamentos.Models;

namespace Backend.Api.DTOs.Loja;

/// <summary>Corpo da compra na loja.</summary>
/// <param name="ItemDeCobrancaId">O convite à venda.</param>
/// <param name="Quantidade">Quantos.</param>
/// <param name="Nome">Nome de quem compra.</param>
/// <param name="Email">Para onde vão o link da compra e os convites.</param>
/// <param name="Cpf">CPF de quem compra, com ou sem pontuação — o limite por pessoa (P3). Dígito inválido é 400.</param>
/// <param name="Meio">Um dos <c>meios</c> da loja — hoje, <c>Pix</c>.</param>
/// <param name="ChaveDeIdempotencia">
/// Id sorteado pela tela ao abrir o formulário (<c>crypto.randomUUID()</c>) e repetido em toda nova tentativa: a mesma
/// chave devolve a mesma compra (decisão 7).
/// </param>
public sealed record CompraRequestDTO(
    Guid ItemDeCobrancaId,
    int Quantidade,
    string? Nome,
    string? Email,
    string? Cpf,
    MeioDePagamento Meio,
    Guid ChaveDeIdempotencia
)
{
    /// <summary>O corpo como o service o recebe.</summary>
    public DadosDaCompra ParaModelo() =>
        new(ItemDeCobrancaId, Quantidade, Nome ?? string.Empty, Email ?? string.Empty, Cpf ?? string.Empty, Meio, ChaveDeIdempotencia);
}

/// <summary>Corpo do pedido de reenvio do link.</summary>
/// <param name="Email">E-mail da compra.</param>
public sealed record ReenvioDoLinkRequestDTO(string? Email);

/// <summary>Um convite à venda.</summary>
/// <param name="Id">Item.</param>
/// <param name="Descricao">Nome na vitrine.</param>
/// <param name="PrecoEmCentavos">Preço de uma unidade.</param>
/// <param name="Disponivel">Quantos ainda cabem; nulo sem teto.</param>
/// <param name="LimitePorPessoa">Quantos um CPF leva; nulo sem limite.</param>
/// <param name="AberturaDeVendas">Quando abre, em UTC.</param>
/// <param name="VendasAte">Último dia de venda.</param>
/// <param name="Aberto">Se vende agora, pelo relógio do servidor.</param>
public sealed record ItemDaLojaDTO(
    Guid Id,
    string Descricao,
    long PrecoEmCentavos,
    int? Disponivel,
    int? LimitePorPessoa,
    DateTime? AberturaDeVendas,
    DateOnly? VendasAte,
    bool Aberto
);

/// <summary>A loja da turma.</summary>
/// <param name="Turma">Quem vende (P5).</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Festa">A festa, se a agenda já a tem.</param>
/// <param name="ContatoDaComissao">Com quem falar sobre troca e devolução.</param>
/// <param name="Meios">Como o comprador pode pagar (<c>Pix</c> ou <c>Cartao</c>); vazio sem Mercado Pago.</param>
/// <param name="Agora">O relógio do servidor, em UTC — a contagem regressiva sai dele (decisão 8).</param>
/// <param name="Itens">Os convites à venda.</param>
public sealed record LojaDTO(
    string Turma,
    string Instituicao,
    EventoDoConviteDTO? Festa,
    string? ContatoDaComissao,
    IEnumerable<MeioDePagamento> Meios,
    DateTime Agora,
    IEnumerable<ItemDaLojaDTO> Itens
);

/// <summary>A cobrança para pagar.</summary>
/// <param name="Meio">Como paga.</param>
/// <param name="CopiaECola">O BR Code do PIX.</param>
/// <param name="ExpiraEm">Até quando aceita pagamento, em UTC.</param>
public sealed record CobrancaDaCompraDTO(MeioDePagamento Meio, string? CopiaECola, DateTime ExpiraEm);

/// <summary>A compra, pelo link.</summary>
/// <param name="Id">Compra.</param>
/// <param name="Status"><c>Pendente</c>, <c>Paga</c>, <c>Expirada</c> ou <c>ADevolver</c>.</param>
/// <param name="Item">Nome do convite.</param>
/// <param name="Quantidade">Quantos.</param>
/// <param name="ValorEmCentavos">Total.</param>
/// <param name="Meio">Como paga.</param>
/// <param name="ExpiraEm">Até quando a reserva vale, em UTC.</param>
/// <param name="PagaEm">Quando pagou.</param>
/// <param name="NomeDoComprador">Quem comprou; nulo depois da exclusão.</param>
/// <param name="Email">E-mail mascarado.</param>
/// <param name="Turma">Quem vende (P5).</param>
/// <param name="ContatoDaComissao">Com quem falar sobre devolução.</param>
/// <param name="Festa">A festa.</param>
/// <param name="Cobranca">A cobrança para pagar, enquanto pendente; nulo se a emissão falhou.</param>
/// <param name="ListaAberta">Se ainda nomeia e transfere.</param>
/// <param name="PodeApagarDados">Se a exclusão dos dados já está disponível.</param>
/// <param name="Convites">Os convites, por posição.</param>
/// <param name="FormaturaId">A turma — a tela volta para <c>/loja/{formatura_id}</c>.</param>
public sealed record CompraDTO(
    Guid Id,
    StatusDaCompra Status,
    string Item,
    int Quantidade,
    long ValorEmCentavos,
    MeioDePagamento Meio,
    DateTime ExpiraEm,
    DateTime? PagaEm,
    string? NomeDoComprador,
    string? Email,
    string Turma,
    string? ContatoDaComissao,
    EventoDoConviteDTO? Festa,
    CobrancaDaCompraDTO? Cobranca,
    bool ListaAberta,
    bool PodeApagarDados,
    IEnumerable<MeuConviteDTO> Convites,
    Guid FormaturaId
);

/// <summary>A compra recém-criada.</summary>
/// <param name="Token">O segredo do link: a tela navega para <c>/compra/{token}</c>.</param>
/// <param name="Compra">A compra.</param>
public sealed record CompraCriadaDTO(string Token, CompraDTO Compra);

/// <summary>Uma compra na lista da Gestão.</summary>
/// <param name="Id">Compra.</param>
/// <param name="CriadaEm">Quando.</param>
/// <param name="Nome">Comprador; nulo depois da exclusão.</param>
/// <param name="Email">E-mail inteiro — a comissão devolve por ele.</param>
/// <param name="Cpf">CPF mascarado.</param>
/// <param name="Item">Convite.</param>
/// <param name="Quantidade">Quantos.</param>
/// <param name="ValorEmCentavos">Total.</param>
/// <param name="Meio">Como pagou.</param>
/// <param name="Status">Situação.</param>
/// <param name="ExpiraEm">Fim da reserva.</param>
/// <param name="PagaEm">Quando pagou.</param>
/// <param name="ValorPagoEmCentavos">O que entrou.</param>
/// <param name="PagadorDiferente">O CPF de quem pagou não é o da compra (P6).</param>
public sealed record CompraNaGestaoDTO(
    Guid Id,
    DateTime CriadaEm,
    string? Nome,
    string? Email,
    string? Cpf,
    string Item,
    int Quantidade,
    long ValorEmCentavos,
    MeioDePagamento Meio,
    StatusDaCompra Status,
    DateTime ExpiraEm,
    DateTime? PagaEm,
    long? ValorPagoEmCentavos,
    bool PagadorDiferente
);

/// <summary>A conta da loja.</summary>
/// <param name="ConvitesVendidos">Convites em compras pagas.</param>
/// <param name="AguardandoPix">Convites presos esperando PIX.</param>
/// <param name="ComprasADevolver">Compras pagas sem lugar (P5).</param>
/// <param name="ArrecadadoEmCentavos">O que entrou pela loja.</param>
public sealed record ResumoDaLojaDTO(int ConvitesVendidos, int AguardandoPix, int ComprasADevolver, long ArrecadadoEmCentavos);
