using Backend.Business.Comunicacao.Models;
using Backend.Business.Festa.Models;
using Backend.Business.Financeiro.Models;

namespace Backend.Api.DTOs.Festa;

/// <summary>Corpo do cadastro de um item da festa.</summary>
/// <param name="Titulo">Como a turma chama o item.</param>
/// <param name="Categoria">Gaveta, a mesma das despesas.</param>
/// <param name="OQueInclui">O que vai ter, em Markdown curto.</param>
/// <param name="DocumentoId">O contrato no acervo. Só documento visível para a turma é aceito.</param>
/// <param name="Rateio">A turma inteira paga, ou só quem quiser. Ausente, vale <c>Turma</c>.</param>
/// <param name="ValorPrevistoEmCentavos">O total do contrato, ou o preço de cada formando.</param>
/// <param name="QuantidadeEstimada">Quantos devem comprar; ignorado no item rateado pela turma.</param>
public sealed record ItemDaFestaRequestDTO(
    string? Titulo,
    CategoriaDeDespesa Categoria,
    string? OQueInclui,
    Guid? DocumentoId,
    TipoDeRateio? Rateio,
    long ValorPrevistoEmCentavos,
    int? QuantidadeEstimada
);

/// <summary>
/// Um item da festa, como a turma o vê.
/// </summary>
/// <remarks>
/// <paramref name="Estado"/>, <paramref name="CustoEmCentavos"/> e
/// <paramref name="CustoPrevistoEmCentavos"/> são calculados na leitura, nunca gravados: é o que faz
/// lançar ou pagar uma despesa mudar o cartão sem ninguém editar o item.
/// </remarks>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">Como a turma chama o item.</param>
/// <param name="Categoria">Gaveta, a mesma das despesas.</param>
/// <param name="OQueInclui">O que vai ter, em Markdown.</param>
/// <param name="Fornecedor">Nome de quem foi contratado, lido das despesas do item.</param>
/// <param name="Documento">O contrato no acervo, quando ligado e visível para a turma.</param>
/// <param name="Rateio">A turma inteira paga, ou só quem quiser.</param>
/// <param name="ValorPrevistoEmCentavos">O total do contrato, ou o preço de cada formando.</param>
/// <param name="QuantidadeEstimada">Quantos devem comprar; 1 no item rateado pela turma.</param>
/// <param name="CustoPrevistoEmCentavos">Valor vezes quantidade — o que o item deve custar antes de haver despesa.</param>
/// <param name="ContratadoEmCentavos">Soma das despesas vinculadas, canceladas de fora.</param>
/// <param name="PagoEmCentavos">Soma das despesas já pagas.</param>
/// <param name="CustoEmCentavos">Quanto o item pesa no custo da festa: o contratado, ou o previsto.</param>
/// <param name="QuantidadeDeDespesas">Despesas vinculadas — com alguma, a exclusão devolve 409.</param>
/// <param name="Estado">A contratar, contratado, pago ou cancelado.</param>
/// <param name="Cancelado">A turma desistiu: sai do custo, fica na lista.</param>
/// <param name="Ordem">Posição na tela.</param>
/// <param name="PrecoDeVendaEmCentavos">
/// Preço unitário do item opcional ligado a este (Sprint 20, decisão 11). Nulo: a venda não foi
/// aberta, e o custo continua saindo da estimativa.
/// </param>
/// <param name="PedidosConfirmados">Unidades já pedidas pelos formandos — o "37" de "R$ 350,00 × 37 pedidos".</param>
/// <param name="ItemDeCobrancaId">O item opcional ligado, se houver.</param>
public sealed record ItemDaFestaDTO(
    Guid Id,
    string Titulo,
    CategoriaDeDespesa Categoria,
    string? OQueInclui,
    string? Fornecedor,
    DocumentoDoAcervo? Documento,
    TipoDeRateio Rateio,
    long ValorPrevistoEmCentavos,
    int QuantidadeEstimada,
    long CustoPrevistoEmCentavos,
    long ContratadoEmCentavos,
    long PagoEmCentavos,
    long CustoEmCentavos,
    int QuantidadeDeDespesas,
    EstadoDoItem Estado,
    bool Cancelado,
    int Ordem,
    long? PrecoDeVendaEmCentavos,
    int PedidosConfirmados,
    Guid? ItemDeCobrancaId
);

/// <summary>A meta da turma: quanto a festa custa, quanto já entrou e quanto dela já foi paga.</summary>
/// <param name="CustoEmCentavos">O que a festa inteira vai custar.</param>
/// <param name="PagoEmCentavos">O que já saiu para os fornecedores dos itens.</param>
/// <param name="ArrecadadoEmCentavos">O que a turma já recebeu — o mesmo número da tela do Caixa.</param>
/// <param name="FaltaArrecadarEmCentavos">Quanto ainda falta juntar; nunca negativo.</param>
/// <param name="Itens">Itens de pé, cancelados de fora.</param>
/// <param name="AContratar">Quantos ainda não têm despesa.</param>
/// <param name="Pagos">Quantos já estão quitados.</param>
public sealed record MetaDaFestaDTO(
    long CustoEmCentavos,
    long PagoEmCentavos,
    long ArrecadadoEmCentavos,
    long FaltaArrecadarEmCentavos,
    int Itens,
    int AContratar,
    int Pagos
);

/// <summary>Corpo do cadastro de uma proposta.</summary>
/// <param name="Titulo">Quem está propondo ("Banda X").</param>
/// <param name="ValorEmCentavos">Quanto ela custa; zero enquanto a comissão não tem o preço.</param>
/// <param name="OQueInclui">O que ela entrega, em Markdown curto.</param>
public sealed record PropostaRequestDTO(string? Titulo, long ValorEmCentavos, string? OQueInclui);

/// <summary>
/// Uma proposta como a turma a vê.
/// </summary>
/// <remarks>
/// <paramref name="Votos"/> é a contagem das linhas de voto, e não um contador gravado: é a mesma
/// regra do estado do item — o que a turma lê não pode depender de alguém lembrar de atualizar.
/// </remarks>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">Quem está propondo.</param>
/// <param name="ValorEmCentavos">Quanto ela custa.</param>
/// <param name="OQueInclui">O que ela entrega, em Markdown.</param>
/// <param name="Votos">Quantos formandos escolheram esta.</param>
/// <param name="MeuVoto">Se o voto de quem está lendo é nesta proposta.</param>
public sealed record PropostaDTO(Guid Id, string Titulo, long ValorEmCentavos, string? OQueInclui, int Votos, bool MeuVoto);

/// <summary>Um item com as candidatas levantadas para ele — o painel de detalhe da tela.</summary>
/// <param name="Item">O item, como a lista o mostra.</param>
/// <param name="Propostas">As candidatas, da mais votada para a menos.</param>
public sealed record ItemDaFestaDetalheDTO(ItemDaFestaDTO Item, IEnumerable<PropostaDTO> Propostas);
