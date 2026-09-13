namespace Backend.Business.Legal.Models;

/// <summary>Prova de que um usuário aceitou uma versão específica de um documento.</summary>
/// <remarks>
/// Nunca é atualizado. Revogar é gravar uma nova linha com <see cref="Revogado"/> verdadeiro —
/// registro de consentimento que pode ser editado não prova nada. Pelo mesmo motivo não herda
/// de <c>Entity</c> (sem <c>AtualizadoEm</c>) e o banco recusa <c>UPDATE</c> e <c>DELETE</c>.
/// </remarks>
public class ConsentimentoRegistrado
{
    /// <summary>Identificador do registro.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Quem aceitou.</summary>
    public Guid UsuarioId { get; init; }

    /// <summary>A versão exata do documento aceita.</summary>
    public Guid DocumentoLegalId { get; init; }

    /// <summary>
    /// Rótulo da versão, copiado do documento de propósito.
    /// </summary>
    /// <remarks>
    /// O documento é imutável hoje; a cópia garante que o registro continue legível mesmo se
    /// alguém, um dia, resolver "só corrigir uma vírgula" no original.
    /// </remarks>
    public string Versao { get; init; } = string.Empty;

    /// <summary>Momento do aceite, em UTC.</summary>
    public DateTime AceitoEm { get; init; }

    /// <summary>IP de onde veio o aceite.</summary>
    public string EnderecoIp { get; init; } = string.Empty;

    /// <summary>Navegador ou cliente que enviou o aceite.</summary>
    public string UserAgent { get; init; } = string.Empty;

    /// <summary>Se esta linha registra uma revogação, e não um aceite.</summary>
    public bool Revogado { get; init; }
}
