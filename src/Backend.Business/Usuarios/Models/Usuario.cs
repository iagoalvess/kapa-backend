using Microsoft.AspNetCore.Identity;

namespace Backend.Business.Usuarios.Models;

/// <summary>
/// Usuário da aplicação.
/// </summary>
/// <remarks>
/// Herda de <see cref="IdentityUser{TKey}"/> em vez de <c>Entity</c> porque o ASP.NET Identity
/// exige essa base — e com ela vêm de graça hash de senha com PBKDF2, bloqueio por tentativas,
/// carimbo de segurança para invalidar sessões, confirmação de e-mail e 2FA. Reescrever isso à
/// mão é o caminho mais curto para uma falha de autenticação.
/// </remarks>
public class Usuario : IdentityUser<Guid>
{
    /// <summary>Inicializa o usuário com identificador sequencial e datas de auditoria.</summary>
    public Usuario()
    {
        Id = Guid.CreateVersion7();
        CriadoEm = DateTime.UtcNow;
        AtualizadoEm = CriadoEm;
    }

    /// <summary>Nome de exibição.</summary>
    public string Nome { get; set; } = string.Empty;

    /// <summary>
    /// Indica se o usuário pode autenticar. Desativar é o caminho de desligamento —
    /// apagar quebraria o histórico que referencia o usuário.
    /// </summary>
    public bool Ativo { get; set; } = true;

    /// <summary>Momento de criação, em UTC.</summary>
    public DateTime CriadoEm { get; set; }

    /// <summary>Momento da última alteração, em UTC. Mantido pelo contexto de dados.</summary>
    public DateTime AtualizadoEm { get; set; }
}
