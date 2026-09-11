namespace Backend.Business.Abstractions;

/// <summary>
/// Base de toda entidade que pertence a uma formatura.
/// </summary>
/// <remarks>
/// Herdar daqui é a única coisa necessária para a linha ficar isolada: o <c>AppDbContext</c>
/// varre o modelo e aplica o filtro global e o índice de <c>FormaturaId</c> sozinho, sem uma
/// linha de configuração por entidade.
/// <para>
/// <c>FormaturaId</c> é escrito pelo contexto a cada <c>SaveChangesAsync</c>. O setter é
/// privado de propósito — atribuição manual é o caminho para gravar na turma errada, e aqui
/// isso nem compila.
/// </para>
/// </remarks>
public abstract class EntidadeDaFormatura : Entity
{
    /// <summary>Formatura dona da linha. Mantido pelo contexto de dados.</summary>
    public Guid FormaturaId { get; private set; }
}
