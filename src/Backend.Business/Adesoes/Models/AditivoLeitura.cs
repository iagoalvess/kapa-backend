using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Adesoes.Models;

/// <summary>O detalhe livre que o formando escreve para um pacote da cesta — "beca M" (Sprint 48, D40).</summary>
/// <param name="PacoteId">Pacote.</param>
/// <param name="Texto">O detalhe; vazio é nenhum.</param>
public sealed record ObservacaoDoPacote(Guid PacoteId, string? Texto);

/// <summary>Um pacote na cesta do formando, como a tela "Minha cesta" o mostra.</summary>
/// <param name="ItemDeCobrancaId">Pacote.</param>
/// <param name="Tipo">Categoria.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="Grupo">Grupo de faixas; nulo é pacote avulso.</param>
/// <param name="ContratadoEmCentavos">
/// O que as parcelas dele somam para o formando — o preço que ele aceitou, e não o de hoje. No pacote de grupo, somadas as
/// faixas anteriores do aditivo.
/// </param>
/// <param name="ConvitesDaFesta">Convites da festa que concede.</param>
/// <param name="ConvitesDaColacao">Convites da colação que concede.</param>
/// <param name="Observacao">O detalhe livre que ele escreveu.</param>
/// <param name="CancelavelAte">Último dia para pedir o cancelamento; nulo, sem trava (D36).</param>
/// <param name="CancelamentoSolicitado">Há solicitação esperando a comissão.</param>
public sealed record PacoteNaCesta(
    Guid ItemDeCobrancaId,
    TipoDeCobranca Tipo,
    string? Descricao,
    string? Grupo,
    long ContratadoEmCentavos,
    int ConvitesDaFesta,
    int ConvitesDaColacao,
    string? Observacao,
    DateOnly? CancelavelAte,
    bool CancelamentoSolicitado
);

/// <summary>Um pacote do catálogo que o aditivo pode acrescentar (D38).</summary>
/// <param name="ItemDeCobrancaId">Pacote.</param>
/// <param name="Tipo">Categoria.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="Grupo">Grupo de faixas; nulo é pacote avulso.</param>
/// <param name="ValorEmCentavos">Preço de hoje no catálogo.</param>
/// <param name="DiferencaEmCentavos">O que o aditivo cobraria: o preço menos o já contratado na faixa que ele substitui.</param>
/// <param name="ConvitesDaFesta">Convites da festa que concede.</param>
/// <param name="ConvitesDaColacao">Convites da colação que concede.</param>
/// <param name="Substitui">A faixa do mesmo grupo que sai da cesta; nulo quando é pacote novo.</param>
public sealed record PacoteDisponivel(
    Guid ItemDeCobrancaId,
    TipoDeCobranca Tipo,
    string? Descricao,
    string? Grupo,
    long ValorEmCentavos,
    long DiferencaEmCentavos,
    int ConvitesDaFesta,
    int ConvitesDaColacao,
    Guid? Substitui
);

/// <summary>A cesta do formando e o que ele ainda pode acrescentar.</summary>
/// <param name="Pacotes">O que está na cesta hoje.</param>
/// <param name="Disponiveis">O que o aditivo pode acrescentar: faixas acima no mesmo grupo e pacotes novos.</param>
public sealed record MinhaCesta(IReadOnlyList<PacoteNaCesta> Pacotes, IReadOnlyList<PacoteDisponivel> Disponiveis);

/// <summary>O aditivo antes do aceite: o que muda e o hash que a tela devolve ao aceitar.</summary>
/// <param name="Aditivo">O snapshot que vai ser aceito.</param>
/// <param name="HashDoConteudo">SHA-256 do termo e do snapshot.</param>
public sealed record PreviaDoAditivo(SnapshotDoAditivo Aditivo, string HashDoConteudo);

/// <summary>O pedido de aceite do aditivo.</summary>
/// <param name="Pacotes">Os pacotes que entram.</param>
/// <param name="HashDoConteudo">O hash da prévia que a tela mostrou.</param>
/// <param name="Codigo">Os seis dígitos do e-mail.</param>
/// <param name="Observacoes">O detalhe livre de cada pacote novo, se houver.</param>
public sealed record AceitarAditivo(
    IReadOnlyList<Guid> Pacotes,
    string HashDoConteudo,
    string Codigo,
    IReadOnlyList<ObservacaoDoPacote>? Observacoes = null
);
