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

    /// <summary>
    /// Momento em que a conta foi anonimizada por pedido de eliminação, em UTC. Nulo no caso normal.
    /// </summary>
    /// <remarks>
    /// Não é "conta apagada" — é o carimbo de que o direito do art. 18, VI foi atendido. A linha
    /// continua porque tudo o que a pessoa pagou aponta para ela; o que some é o que a identifica.
    /// <para>
    /// É também o que as leituras consultam para não servir o nome e o CPF guardados nas tabelas
    /// <i>append-only</i> — a adesão é contrato assinado e o banco recusa alterá-la, então quem
    /// mascara é a borda de leitura (Sprint 14).
    /// </para>
    /// </remarks>
    public DateTime? AnonimizadoEm { get; set; }

    /// <summary>Momento de criação, em UTC.</summary>
    public DateTime CriadoEm { get; set; }

    /// <summary>Momento da última alteração, em UTC. Mantido pelo contexto de dados.</summary>
    public DateTime AtualizadoEm { get; set; }
}
