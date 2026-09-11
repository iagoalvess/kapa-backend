namespace Backend.Business.Abstractions;

/// <summary>
/// Base de toda entidade persistida do domínio.
/// </summary>
/// <remarks>
/// O identificador é um GUID versão 7: tem prefixo de timestamp, então os inserts caem em
/// páginas sequenciais do índice em vez de espalhados como no GUID v4. Mantém a vantagem de
/// gerar o id na aplicação (sem ida ao banco) e de não ser enumerável por terceiros.
/// <para>
/// <c>AtualizadoEm</c> é escrito automaticamente pelo <c>AppDbContext</c> a cada
/// <c>SaveChangesAsync</c>. Não atribua na mão.
/// </para>
/// </remarks>
public abstract class Entity
{
    /// <summary>Inicializa a entidade com identificador e datas de auditoria.</summary>
    protected Entity()
    {
        Id = Guid.CreateVersion7();
        CriadoEm = DateTime.UtcNow;
        AtualizadoEm = CriadoEm;
    }

    /// <summary>Identificador único da entidade.</summary>
    public Guid Id { get; protected set; }

    /// <summary>Momento de criação, em UTC.</summary>
    public DateTime CriadoEm { get; protected set; }

    /// <summary>Momento da última alteração, em UTC. Mantido pelo contexto de dados.</summary>
    public DateTime AtualizadoEm { get; set; }
}
