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
public sealed record ItemDaFestaDTO(
    Guid Id,
    string Titulo,
    CategoriaDeDespesa Categoria,
    string? OQueInclui,
    string? Fornecedor,
    DocumentoDoItem? Documento,
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
    int Ordem
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
