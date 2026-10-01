namespace Backend.Business.Eventos.Models;

/// <summary>
/// Um evento de uso registrado pela aplicação.
/// </summary>
/// <remarks>
/// Não herda de <c>Entity</c> de propósito. Evento é **append-only e imutável**: nasce, é
/// gravado e nunca muda. Um campo <c>AtualizadoEm</c> aqui seria uma promessa falsa, e a tabela
/// é a que mais cresce no banco — cada coluna sem uso custa em disco e em índice.
/// <para>
/// <see cref="Dados"/> é um JSON livre, gravado como <c>jsonb</c>. É o que permite acrescentar
/// um campo a um evento sem migration: o formato de um evento de produto muda muito mais rápido
/// que o esquema do domínio.
/// </para>
/// </remarks>
public class Evento
{
    /// <summary>Identificador do evento.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// Nome do evento, no formato <c>recurso.acao</c> — por exemplo <c>produto.criado</c>.
    /// </summary>
    /// <remarks>
    /// Use um vocabulário estável. Renomear um evento parte a série histórica em duas, e a
    /// resposta de "quantas vezes isso aconteceu no ano passado" passa a depender de lembrar
    /// dos dois nomes.
    /// </remarks>
    public string Nome { get; init; } = string.Empty;

    /// <summary>Usuário que originou o evento. Nulo em ação anônima ou de sistema.</summary>
    public Guid? UsuarioId { get; init; }

    /// <summary>
    /// Formatura a que o evento pertence. Nula no evento que não é de turma nenhuma.
    /// </summary>
    /// <remarks>
    /// Coluna, e não uma chave dentro de <see cref="Dados"/> (Sprint 14): a trilha de auditoria é
    /// por turma, e o filtro precisa de índice. Extrair a turma do <c>jsonb</c> a cada consulta
    /// significaria varrer a maior tabela do banco toda vez que a assembleia abre a tela.
    /// <para>
    /// A entidade <b>não</b> herda de <c>EntidadeDaFormatura</c>, e isso é deliberado: eventos são
    /// da plataforma inteira — login, bloqueio por tentativas, ação de administrador —, e um filtro
    /// global aqui esconderia justamente os que não pertencem a turma nenhuma. Quem preenche a
    /// coluna é <c>Auditoria.Auditar</c>, a partir do próprio corpo do evento.
    /// </para>
    /// </remarks>
    public Guid? FormaturaId { get; init; }

    /// <summary>Momento em que o evento aconteceu, em UTC.</summary>
    /// <remarks>
    /// Capturado no momento do evento, e não na gravação: a fila grava em lote, então os dois
    /// instantes diferem em alguns segundos.
    /// </remarks>
    public DateTime OcorridoEm { get; init; } = DateTime.UtcNow;

    /// <summary>Dados adicionais em JSON, gravados como <c>jsonb</c>.</summary>
    public string? Dados { get; init; }
}
