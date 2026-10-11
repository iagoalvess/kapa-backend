using Backend.Business.Abstractions;
using Backend.Business.Legal.Models;

namespace Backend.Business.Auth.Models;

/// <summary>Identidade obtida de um ID token validado.</summary>
public sealed record IdentidadeGoogle(string Sub, string Email, string Nome, bool AutoridadeSobreEmail);

/// <summary>Cadastro sem senha, com identidade validada pelo servidor.</summary>
public sealed record RegistroComGoogle(
    string Nome,
    string Email,
    string Sub,
    bool EmailConfirmado,
    IReadOnlyList<AceiteDeDocumento> Aceites,
    bool ReceberComunicacaoDoKapa
);

/// <summary>Tentativa curta, vinculada ao navegador e consumida sob trava no banco.</summary>
public sealed class TentativaGoogle : Entity
{
    /// <summary>Hash da credencial opaca entregue apenas no corpo.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Hash do cookie HttpOnly do navegador que iniciou o fluxo.</summary>
    public string NavegadorHash { get; set; } = string.Empty;

    /// <summary>Nonce que precisa constar no ID token.</summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>Expiração UTC; renovada apenas ao avançar para um contexto novo.</summary>
    public DateTime ExpiraEm { get; set; }

    /// <summary>entrada, cadastro ou vinculo; uma etapa nunca autoriza outra.</summary>
    public string Etapa { get; set; } = "entrada";

    /// <summary>Identificador estável do Google, depois da validação.</summary>
    public string? Sub { get; set; }

    /// <summary>E-mail validado no token.</summary>
    public string? Email { get; set; }

    /// <summary>Nome sugerido pelo Google.</summary>
    public string? Nome { get; set; }

    /// <summary>Se o Google tem autoridade sobre o endereço.</summary>
    public bool AutoridadeSobreEmail { get; set; }

    /// <summary>Conta alvo do vínculo, decidida pelo servidor.</summary>
    public Guid? UsuarioId { get; set; }

    /// <summary>Carimbo na origem; conta antiga não confirmada requer recuperação que o altere.</summary>
    public string? SecurityStamp { get; set; }

    /// <summary>Se a conta precisa recuperar a senha preexistente antes do vínculo.</summary>
    public bool ExigeRecuperacao { get; set; }

    /// <summary>Hash do comprovante específico recebido por e-mail.</summary>
    public string? ComprovanteHash { get; set; }

    /// <summary>Convite público a retomar, inclusive quando o e-mail abre outra aba.</summary>
    public string? Convite { get; set; }

    /// <summary>Consumo persistido na mesma transação da ação.</summary>
    public DateTime? ConsumidaEm { get; set; }
}

/// <summary>Nonce e tentativa que o botão Google utiliza.</summary>
public sealed record InicioGoogle(string Tentativa, string Nonce);

/// <summary>Próxima etapa, sem credencial Google nem sessão em URL.</summary>
public sealed record ContextoGoogle(string Contexto, string Nome, string Email, bool ExigeRecuperacao);

/// <summary>Sessão ou próximo passo; o contexto persiste mesmo quando a borda responde 409.</summary>
public sealed record ResultadoGoogle(ParDeTokens? Sessao, ContextoGoogle? ProximoPasso, Erro? ErroDoProximoPasso);

/// <summary>Campos editáveis da conclusão do cadastro.</summary>
public sealed record CadastroGoogle(string Contexto, string Nome, IReadOnlyList<AceiteDeDocumento> Aceites, bool ReceberComunicacaoDoKapa);
