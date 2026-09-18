namespace Backend.Business.Eventos.Models;

/// <summary>
/// Uma linha da trilha de auditoria, como a assembleia a lê.
/// </summary>
/// <remarks>
/// O autor vem resolvido em nome — a tela existe para responder "<i>quem</i> baixou esta parcela",
/// e um GUID não responde isso. Autor nulo é ação de sistema (o worker, a régua); autor anonimizado
/// aparece pelo marcador, como em todo o resto do produto.
/// <para>
/// <see cref="Pessoas"/> faz o mesmo com <i>quem sofreu</i> a operação — o corpo do evento guarda o
/// id, e a tela mostrava "Pessoa 01a09d04-…". A resolução é <b>na leitura</b>, e não gravando o nome
/// no corpo: o corpo é imutável, e nome gravado ali sobreviveria à anonimização do titular, que é
/// exatamente o que a Sprint 14 promete apagar.
/// </para>
/// </remarks>
/// <param name="Id">Identificador do evento.</param>
/// <param name="Nome">Nome estável do evento, <c>recurso.acao</c>.</param>
/// <param name="OcorridoEm">Quando aconteceu, em UTC.</param>
/// <param name="AutorUsuarioId">Quem fez. Nulo em ação de sistema.</param>
/// <param name="Autor">Nome de quem fez. Nulo em ação de sistema.</param>
/// <param name="Dados">Corpo do evento em JSON, com o antes e o depois quando houver.</param>
/// <param name="Pessoas">
/// Nome de cada pessoa citada no corpo, por id. Vazio quando o evento não cita ninguém.
/// </param>
public sealed record LinhaDeAuditoria(
    Guid Id,
    string Nome,
    DateTime OcorridoEm,
    Guid? AutorUsuarioId,
    string? Autor,
    string? Dados,
    IReadOnlyDictionary<string, string> Pessoas = null!
);

/// <summary>
/// O recorte pedido pela tela de Auditoria.
/// </summary>
/// <remarks>
/// Sem filtro nenhum, a consulta traz a turma inteira do mais recente para o mais antigo — que é a
/// pergunta que a assembleia faz primeiro. As pontas do período são dias, convertidos para UTC na
/// borda do repositório.
/// <para>
/// <c>ponytail: a busca é um <c>ILIKE</c> sobre <c>dados::text</c>, sem índice.</c> O corpo é
/// <c>jsonb</c> e o identificador mora em chaves diferentes conforme o evento (<c>parcelaId</c>,
/// <c>membroUsuarioId</c>, <c>itemId</c>), então não há uma coluna para indexar — e o
/// <c>LIKE</c> direto o Postgres nem aceita em <c>jsonb</c>, daí o <c>::text</c>. A varredura é da
/// turma, não da tabela: o recorte por formatura entra na mesma consulta e usa o índice parcial.
/// Quando uma turma tiver dezenas de milhares de eventos, o caminho é um índice GIN de trigrama
/// sobre <c>dados::text</c>.
/// </para>
/// </remarks>
/// <param name="De">Primeiro dia do período, inclusive.</param>
/// <param name="Ate">Último dia do período, inclusive.</param>
/// <param name="AutorUsuarioId">Só o que esta pessoa fez.</param>
/// <param name="Nome">Só este tipo de evento.</param>
/// <param name="Busca">
/// Um nome de pessoa — de quem fez ou de quem sofreu — ou qualquer texto escrito no corpo do
/// evento (título, justificativa, motivo).
/// </param>
public sealed record FiltroDeAuditoria(DateOnly? De, DateOnly? Ate, Guid? AutorUsuarioId, string? Nome, string? Busca = null);

/// <summary>Um autor que aparece na trilha da turma, para o seletor da tela.</summary>
/// <param name="UsuarioId">Autor.</param>
/// <param name="Nome">Nome de exibição.</param>
public sealed record AutorDeAuditoria(Guid UsuarioId, string Nome);

/// <summary>O que os seletores da tela de Auditoria oferecem.</summary>
/// <remarks>
/// Endpoint próprio em vez de a tela deduzir da página corrente: o seletor precisa dos autores da
/// turma inteira, e não só dos que aparecem nas vinte linhas visíveis.
/// </remarks>
/// <param name="Autores">Quem já fez alguma coisa auditável nesta turma.</param>
/// <param name="Nomes">Os nomes de evento que a turma tem registrados.</param>
public sealed record OpcoesDeAuditoria(IReadOnlyList<AutorDeAuditoria> Autores, IReadOnlyList<string> Nomes);

/// <summary>Um item contado da trilha: o rótulo e quantas vezes ele aparece.</summary>
/// <param name="Rotulo">Nome da pessoa ou nome estável do evento.</param>
/// <param name="Quantidade">Quantas linhas da trilha são dele.</param>
public sealed record ContagemDaAuditoria(string Rotulo, int Quantidade);

/// <summary>
/// O resumo da trilha, para a faixa de números do topo da tela.
/// </summary>
/// <remarks>
/// <b>Ignora os filtros da tela de propósito.</b> A faixa responde "como está a trilha desta turma",
/// e o recorte filtrado já tem a própria contagem, ao lado da busca — um resumo que mudasse a cada
/// filtro diria duas coisas diferentes na mesma tela.
/// <para>
/// Tudo nulo com a trilha vazia: turma nova não tem "quem mais fez", e inventar um zero ali seria
/// dizer que alguém fez nada.
/// </para>
/// </remarks>
/// <param name="Total">Quantas ações a turma tem registradas.</param>
/// <param name="NosUltimosTrintaDias">Quantas aconteceram nos últimos trinta dias.</param>
/// <param name="UltimaEm">Quando foi a última, em UTC.</param>
/// <param name="UltimoNome">O nome estável do evento mais recente.</param>
/// <param name="QuemMaisFez">Quem mais aparece como autor. Ação de sistema não entra: ela não é "quem".</param>
/// <param name="AcaoMaisComum">O nome de evento mais frequente.</param>
public sealed record ResumoDaAuditoria(
    long Total,
    int NosUltimosTrintaDias,
    DateTime? UltimaEm,
    string? UltimoNome,
    ContagemDaAuditoria? QuemMaisFez,
    ContagemDaAuditoria? AcaoMaisComum
);
