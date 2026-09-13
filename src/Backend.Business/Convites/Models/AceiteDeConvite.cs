namespace Backend.Business.Convites.Models;

/// <summary>Registro de quem entrou por qual convite, de onde e quando.</summary>
/// <remarks>
/// Append-only, como o consentimento: não herda de <c>Entity</c> (sem <c>AtualizadoEm</c>) e não
/// herda de <c>EntidadeDaFormatura</c> — o aceite acontece com a sessão ainda sem a formatura do
/// convite, e a formatura já está no convite apontado.
/// </remarks>
public class AceiteDeConvite
{
    /// <summary>Identificador do registro.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Convite usado.</summary>
    public Guid ConviteId { get; init; }

    /// <summary>Quem entrou.</summary>
    public Guid UsuarioId { get; init; }

    /// <summary>Momento do aceite, em UTC.</summary>
    public DateTime AceitoEm { get; init; }

    /// <summary>IP de onde veio o aceite.</summary>
    public string? EnderecoIp { get; init; }

    /// <summary>Navegador ou cliente que aceitou.</summary>
    public string? UserAgent { get; init; }
}
