using Backend.Business.Financeiro.Models;

namespace Backend.Business.Festa.Models;

/// <summary>Um item da festa, como a comissão o informa.</summary>
/// <param name="Titulo">Como a turma chama o item.</param>
/// <param name="Categoria">Gaveta, a mesma das despesas.</param>
/// <param name="OQueInclui">O que vai ter, em Markdown curto.</param>
/// <param name="DocumentoId">O contrato no acervo. Só documento visível para a turma é aceito.</param>
/// <param name="Rateio">A turma inteira paga, ou só quem quiser.</param>
/// <param name="ValorPrevistoEmCentavos">O total do contrato, ou o preço de cada formando.</param>
/// <param name="QuantidadeEstimada">Quantos devem comprar; ignorado no item rateado pela turma.</param>
public sealed record DadosDoItemDaFesta(
    string Titulo,
    CategoriaDeDespesa Categoria,
    string? OQueInclui,
    Guid? DocumentoId = null,
    TipoDeRateio Rateio = TipoDeRateio.Turma,
    long ValorPrevistoEmCentavos = 0,
    int QuantidadeEstimada = 1
);

/// <summary>Uma proposta, como a comissão a informa.</summary>
/// <param name="Titulo">Quem está propondo.</param>
/// <param name="ValorEmCentavos">Quanto ela custa; zero enquanto não há preço.</param>
/// <param name="OQueInclui">O que ela entrega, em Markdown curto.</param>
public sealed record DadosDaProposta(string Titulo, long ValorEmCentavos, string? OQueInclui);

/// <summary>
/// Uma proposta como a turma a vê, com quantos votos tem e se o voto de quem lê é dela.
/// </summary>
/// <remarks>
/// <paramref name="Votos"/> é a contagem das linhas de <see cref="VotoNaProposta"/>, e não um
/// contador gravado — é a decisão 2 desta sprint aplicada ao placar, pelo mesmo motivo: contador que
/// alguém esquece de decrementar é um placar que mente para a turma inteira.
/// </remarks>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">Quem está propondo.</param>
/// <param name="ValorEmCentavos">Quanto ela custa.</param>
/// <param name="OQueInclui">O que ela entrega, em Markdown.</param>
/// <param name="Votos">Quantos formandos escolheram esta.</param>
/// <param name="MeuVoto">Se o voto de quem está lendo é nesta proposta.</param>
public sealed record PropostaResumo(Guid Id, string Titulo, long ValorEmCentavos, string? OQueInclui, int Votos, bool MeuVoto);

/// <summary>
/// O contrato de um item, como o cartão o abre.
/// </summary>
/// <remarks>
/// O arquivo continua no acervo (Sprint 11) e é baixado pelo endpoint de lá, que confere formatura e
/// visibilidade de novo. O que vem aqui é só o necessário para desenhar o link e decidir entre abrir
/// numa aba (PDF, imagem) e baixar com o nome original (Word, planilha).
/// </remarks>
/// <param name="Id">Documento no acervo.</param>
/// <param name="Titulo">Como a turma o chama ("Contrato do buffet").</param>
/// <param name="NomeDoArquivo">Nome original, para o download.</param>
/// <param name="ContentType">Tipo do arquivo.</param>
public sealed record DocumentoDoItem(Guid Id, string Titulo, string NomeDoArquivo, string ContentType);

/// <summary>
/// Um item da festa como a turma o vê, com o que já foi contratado e pago.
/// </summary>
/// <remarks>
/// <see cref="Estado"/> e <see cref="CustoEmCentavos"/> não vêm do banco: são lidos das somas de
/// despesa que a consulta projeta (decisões 2 e 3). É o que garante que lançar ou pagar uma despesa
/// mude o cartão sem ninguém editar o item.
/// </remarks>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">Como a turma chama o item.</param>
/// <param name="Categoria">Gaveta, a mesma das despesas.</param>
/// <param name="OQueInclui">O que vai ter, em Markdown.</param>
/// <param name="Fornecedor">Nome de quem foi contratado, lido das despesas do item — e só o nome (decisões 6 e 15).</param>
/// <param name="Documento">O contrato no acervo, quando ligado e visível para a turma.</param>
/// <param name="Rateio">A turma inteira paga, ou só quem quiser.</param>
/// <param name="ValorPrevistoEmCentavos">O total do contrato, ou o preço de cada formando.</param>
/// <param name="QuantidadeEstimada">Quantos devem comprar; 1 no item rateado pela turma.</param>
/// <param name="ContratadoEmCentavos">Soma das despesas vinculadas, canceladas de fora.</param>
/// <param name="PagoEmCentavos">Soma das despesas já pagas.</param>
/// <param name="QuantidadeDeDespesas">Despesas vinculadas, canceladas de fora — é o que define o estado.</param>
/// <param name="QuantidadeDePropostas">Candidatas levantadas pela comissão; o detalhe traz cada uma.</param>
/// <param name="Cancelado">A turma desistiu: sai do custo, fica na lista.</param>
/// <param name="Ordem">Posição na tela.</param>
public sealed record ItemDaFestaResumo(
    Guid Id,
    string Titulo,
    CategoriaDeDespesa Categoria,
    string? OQueInclui,
    string? Fornecedor,
    DocumentoDoItem? Documento,
    TipoDeRateio Rateio,
    long ValorPrevistoEmCentavos,
    int QuantidadeEstimada,
    long ContratadoEmCentavos,
    long PagoEmCentavos,
    int QuantidadeDeDespesas,
    int QuantidadeDePropostas,
    bool Cancelado,
    int Ordem
)
{
    /// <summary>O que este item deve custar à turma antes de haver despesa.</summary>
    public long CustoPrevistoEmCentavos => ValorPrevistoEmCentavos * QuantidadeEstimada;

    /// <summary>
    /// Quanto este item pesa no custo da festa: o contratado, se houver; senão, o previsto.
    /// </summary>
    /// <remarks>Item cancelado pesa zero — foi o que a decisão 13 combinou com a turma.</remarks>
    public long CustoEmCentavos =>
        Cancelado ? 0
        : QuantidadeDeDespesas > 0 ? ContratadoEmCentavos
        : CustoPrevistoEmCentavos;

    /// <summary>Em que pé está, lido das despesas (decisão 2).</summary>
    public EstadoDoItem Estado =>
        Cancelado ? EstadoDoItem.Cancelado
        : QuantidadeDeDespesas == 0 ? EstadoDoItem.AContratar
        : PagoEmCentavos >= ContratadoEmCentavos ? EstadoDoItem.Pago
        : EstadoDoItem.Contratado;
}

/// <summary>
/// Um item com as propostas levantadas para ele — o que a tela mostra no painel da direita.
/// </summary>
/// <remarks>
/// As propostas não vêm na listagem de propósito: a lista da esquerda só precisa saber que existem
/// (<see cref="ItemDaFestaResumo.QuantidadeDePropostas"/>), e trazer todas em toda abertura da tela
/// carregaria a consulta com o que só um item por vez mostra. O detalhe é o único lugar que sabe
/// <see cref="PropostaResumo.MeuVoto"/>, porque é o único que recebe quem está lendo.
/// </remarks>
/// <param name="Item">O item, como a lista o mostra.</param>
/// <param name="Propostas">As candidatas, da mais votada para a menos.</param>
public sealed record ItemDaFestaDetalhe(ItemDaFestaResumo Item, IReadOnlyList<PropostaResumo> Propostas);

/// <summary>
/// A meta da turma: quanto a festa custa, quanto já entrou e quanto dela já foi paga.
/// </summary>
/// <remarks>
/// Nada aqui é coluna (decisão 3). O custo é a soma dos itens; o arrecadado é o mesmo número da tela
/// do Caixa, vindo do mesmo repositório — duas telas que discordam sobre quanto a turma arrecadou é
/// o pior defeito que esta sprint poderia introduzir.
/// </remarks>
/// <param name="CustoEmCentavos">O que a festa inteira vai custar.</param>
/// <param name="PagoEmCentavos">O que já saiu para os fornecedores dos itens.</param>
/// <param name="ArrecadadoEmCentavos">O que a turma já recebeu — recebimentos não estornados.</param>
/// <param name="Itens">Itens de pé, cancelados de fora.</param>
/// <param name="AContratar">Quantos ainda não têm despesa.</param>
/// <param name="Pagos">Quantos já estão quitados.</param>
public sealed record MetaDaFesta(long CustoEmCentavos, long PagoEmCentavos, long ArrecadadoEmCentavos, int Itens, int AContratar, int Pagos)
{
    /// <summary>Quanto ainda falta a turma juntar. Nunca negativo: arrecadar a mais não é dívida.</summary>
    public long FaltaArrecadarEmCentavos => Math.Max(0, CustoEmCentavos - ArrecadadoEmCentavos);
}
