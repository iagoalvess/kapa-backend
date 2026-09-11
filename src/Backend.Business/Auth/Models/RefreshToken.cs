using Backend.Business.Abstractions;

namespace Backend.Business.Auth.Models;

/// <summary>
/// Refresh token emitido para um usuário.
/// </summary>
/// <remarks>
/// Guarda o **hash** do token, nunca o token. Se o banco vazar, o atacante fica com hashes
/// inúteis em vez de sessões prontas para usar — a mesma razão pela qual senha não é guardada
/// em texto puro.
/// <para>
/// <see cref="SubstituidoPorHash"/> encadeia a rotação e é o que permite detectar reúso: um
/// token já rotacionado sendo apresentado de novo significa que alguém copiou a credencial.
/// </para>
/// </remarks>
public class RefreshToken : Entity
{
    /// <summary>Dono do token.</summary>
    public Guid UsuarioId { get; set; }

    /// <summary>Hash SHA-256 do token entregue ao cliente.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Momento em que o token deixa de valer, em UTC.</summary>
    public DateTime ExpiraEm { get; set; }

    /// <summary>Momento da revogação, em UTC. Nulo enquanto o token estiver válido.</summary>
    public DateTime? RevogadoEm { get; set; }

    /// <summary>Hash do token que substituiu este na rotação.</summary>
    public string? SubstituidoPorHash { get; set; }

    /// <summary>Endereço de origem do pedido que gerou o token, para auditoria.</summary>
    public string? CriadoPorIp { get; set; }

    /// <summary>
    /// Formatura selecionada nesta sessão. Nulo enquanto o usuário não escolheu uma.
    /// </summary>
    /// <remarks>
    /// Fica aqui, e não só no access token, porque a renovação precisa saber para qual turma
    /// reemitir. Sem isso, o usuário voltaria para a tela de seleção a cada quinze minutos.
    /// </remarks>
    public Guid? FormaturaId { get; set; }

    /// <summary>Indica se o token ainda pode ser usado.</summary>
    public bool Ativo(DateTime agoraUtc) => RevogadoEm is null && agoraUtc < ExpiraEm;
}
