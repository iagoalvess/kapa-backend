using Backend.Business.Festa.Models;
using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Loja.Models;

/// <summary>O que o comprador informa para comprar (P2).</summary>
/// <param name="ItemDeCobrancaId">O convite à venda.</param>
/// <param name="Quantidade">Quantos.</param>
/// <param name="Nome">Nome de quem compra.</param>
/// <param name="Email">Para onde vai o link da compra e dos convites.</param>
/// <param name="Cpf">CPF de quem compra — o limite por pessoa (P3). Normalizado para só dígitos.</param>
/// <param name="Meio">Como paga — um de <see cref="MeiosDePagamento.Ligados"/>.</param>
/// <param name="ChaveDeIdempotencia">O id que a tela sorteou ao abrir o formulário (decisão 7).</param>
public sealed record DadosDaCompra(
    Guid ItemDeCobrancaId,
    int Quantidade,
    string Nome,
    string Email,
    string Cpf,
    MeioDePagamento Meio,
    Guid ChaveDeIdempotencia
);

/// <summary>A loja da turma, como a página pública a mostra.</summary>
/// <param name="Turma">Nome da turma — quem vende (P5).</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Festa">A festa, se a agenda já a tem.</param>
/// <param name="ContatoDaComissao">Com quem falar sobre troca, devolução e cancelamento (P5).</param>
/// <param name="Meios">Como o comprador pode pagar; vazio quando a turma está sem Mercado Pago.</param>
/// <param name="Agora">O relógio do servidor, em UTC: a tela libera o botão por ele, nunca pelo do celular (decisão 8).</param>
/// <param name="Itens">Os convites à venda.</param>
public sealed record LojaDaTurma(
    string Turma,
    string Instituicao,
    EventoDoConvite? Festa,
    string? ContatoDaComissao,
    IReadOnlyList<MeioDePagamento> Meios,
    DateTime Agora,
    IReadOnlyList<ItemDaLoja> Itens
);

/// <summary>Um convite à venda na loja.</summary>
/// <param name="Id">Item de cobrança.</param>
/// <param name="Descricao">Nome na vitrine.</param>
/// <param name="PrecoEmCentavos">Preço de uma unidade na loja.</param>
/// <param name="Disponivel">Quantos ainda cabem; nulo no item sem teto.</param>
/// <param name="LimitePorPessoa">Quantos um CPF leva; nulo, sem limite.</param>
/// <param name="AberturaDeVendas">Quando abre, em UTC; nulo, aberto.</param>
/// <param name="VendasAte">Último dia de venda; nulo, sem prazo.</param>
/// <param name="Aberto">Se vende agora — pelo relógio do servidor.</param>
public sealed record ItemDaLoja(
    Guid Id,
    string Descricao,
    long PrecoEmCentavos,
    int? Disponivel,
    int? LimitePorPessoa,
    DateTime? AberturaDeVendas,
    DateOnly? VendasAte,
    bool Aberto
);

/// <summary>A cobrança para pagar a compra.</summary>
/// <param name="Meio">Como paga.</param>
/// <param name="CopiaECola">O BR Code do PIX.</param>
/// <param name="ExpiraEm">Até quando o PIX aceita pagamento, em UTC.</param>
public sealed record CobrancaDaCompra(MeioDePagamento Meio, string? CopiaECola, DateTime ExpiraEm);

/// <summary>A compra como o comprador a vê pelo link (decisão 10).</summary>
/// <param name="Id">Compra.</param>
/// <param name="Status">Situação.</param>
/// <param name="Item">Nome do convite.</param>
/// <param name="Quantidade">Quantos.</param>
/// <param name="ValorEmCentavos">Total.</param>
/// <param name="Meio">Como paga.</param>
/// <param name="ExpiraEm">Até quando a reserva vale sem pagamento, em UTC.</param>
/// <param name="PagaEm">Quando o pagamento foi confirmado.</param>
/// <param name="NomeDoComprador">Quem comprou; nulo depois da exclusão.</param>
/// <param name="Email">E-mail da compra, mascarado.</param>
/// <param name="Turma">Quem vende (P5).</param>
/// <param name="ContatoDaComissao">Com quem falar sobre devolução (P5).</param>
/// <param name="Festa">A festa, se a agenda já a tem.</param>
/// <param name="Cobranca">A cobrança para pagar, enquanto pendente; nulo se a emissão falhou — a tela oferece tentar de novo.</param>
/// <param name="ListaAberta">Se o comprador ainda nomeia e transfere (até 24 h antes da festa).</param>
/// <param name="PodeApagarDados">Se a festa já passou e os dados ainda existem: a exclusão fica disponível (decisão 5).</param>
/// <param name="Convites">Os convites válidos, por posição.</param>
/// <param name="FormaturaId">A turma — o caminho de volta para a loja.</param>
public sealed record CompraParaOComprador(
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
    EventoDoConvite? Festa,
    CobrancaDaCompra? Cobranca,
    bool ListaAberta,
    bool PodeApagarDados,
    IReadOnlyList<MeuConvite> Convites,
    Guid FormaturaId
);

/// <summary>A compra recém-criada, com o link que só a resposta leva.</summary>
/// <param name="Token">O segredo do link de acesso — a tela navega para ele.</param>
/// <param name="Compra">A compra.</param>
public sealed record CompraCriada(string Token, CompraParaOComprador Compra);

/// <summary>Filtro da lista de compras da Gestão.</summary>
/// <param name="Status">Só um status.</param>
/// <param name="Busca">Trecho do nome ou do e-mail.</param>
public sealed record FiltroDeCompras(StatusDaCompra? Status = null, string? Busca = null);

/// <summary>Uma compra na lista da Gestão — a que sustenta a devolução (P5).</summary>
/// <param name="Id">Compra.</param>
/// <param name="CriadaEm">Quando, em UTC.</param>
/// <param name="Nome">Comprador; nulo depois da exclusão.</param>
/// <param name="Email">E-mail inteiro — a comissão precisa dele para devolver.</param>
/// <param name="Cpf">CPF mascarado.</param>
/// <param name="Item">Convite comprado.</param>
/// <param name="Quantidade">Quantos.</param>
/// <param name="ValorEmCentavos">Total da compra.</param>
/// <param name="Meio">Como pagou.</param>
/// <param name="Status">Situação.</param>
/// <param name="ExpiraEm">Fim da reserva, na pendente.</param>
/// <param name="PagaEm">Quando pagou.</param>
/// <param name="ValorPagoEmCentavos">O que entrou.</param>
/// <param name="PagadorDiferente">O CPF de quem pagou não é o da compra — o sinal da P6.</param>
public sealed record CompraNaGestao(
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

/// <summary>A conta da loja para a Gestão: o que vendeu e o que está preso esperando (decisão 3).</summary>
/// <param name="ConvitesVendidos">Convites em compras pagas.</param>
/// <param name="AguardandoPix">Convites presos em compras pendentes de PIX.</param>
/// <param name="ComprasADevolver">Compras pagas sem lugar — a devolução é da comissão (P5).</param>
/// <param name="ArrecadadoEmCentavos">O que entrou pela loja.</param>
public sealed record ResumoDaLoja(int ConvitesVendidos, int AguardandoPix, int ComprasADevolver, long ArrecadadoEmCentavos);

/// <summary>Uma compra pendente vencida, para o job expirar — e a turma dela, para apontar o escopo.</summary>
/// <param name="CompraId">Compra.</param>
/// <param name="FormaturaId">Turma.</param>
public sealed record CompraAExpirar(Guid CompraId, Guid FormaturaId);
