namespace Backend.Business.Formandos.Models;

/// <summary>Registro de que a comissão alterou o cadastro de um formando: quem, quando e que seções.</summary>
/// <remarks>
/// Gravado na mesma transação da correção, e não na fila de eventos — a fila descarta quando
/// enche, e registro de quem mexeu no cadastro alheio não pode ser perdido.
/// <para>
/// Append-only, como o aceite de convite: não herda de <c>Entity</c>. Guarda só os nomes das
/// seções, nunca os valores — CPF antigo numa tabela de histórico é o dado sensível fora da cifra.
/// </para>
/// </remarks>
public class CorrecaoDePerfil
{
    /// <summary>Identificador do registro.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Cadastro corrigido.</summary>
    public Guid PerfilId { get; init; }

    /// <summary>Quem corrigiu.</summary>
    public Guid AutorUsuarioId { get; init; }

    /// <summary>Momento da correção, em UTC.</summary>
    public DateTime CorrigidoEm { get; init; }

    /// <summary>Seções alteradas, separadas por vírgula.</summary>
    public string Secoes { get; init; } = string.Empty;
}
