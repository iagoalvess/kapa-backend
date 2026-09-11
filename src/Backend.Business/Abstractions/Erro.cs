namespace Backend.Business.Abstractions;

/// <summary>
/// Natureza da falha. Define o status HTTP na borda — o service nunca conhece status code.
/// </summary>
public enum ETipoErro
{
    /// <summary>Dado de entrada inválido. Vira 400.</summary>
    Validacao,

    /// <summary>Recurso inexistente. Vira 404.</summary>
    NaoEncontrado,

    /// <summary>Estado atual impede a operação (duplicidade, concorrência). Vira 409.</summary>
    Conflito,

    /// <summary>Credencial ausente ou inválida. Vira 401.</summary>
    NaoAutenticado,

    /// <summary>Autenticado, mas sem permissão. Vira 403.</summary>
    Proibido,

    /// <summary>Dependência externa indisponível. Vira 503.</summary>
    Indisponivel,
}

/// <summary>
/// Uma falha esperada de negócio.
/// </summary>
/// <param name="Codigo">
/// Identificador estável e legível por máquina (ex.: <c>usuario.email_em_uso</c>).
/// O consumidor da API deve ramificar por ele, nunca pela mensagem.
/// </param>
/// <param name="Mensagem">Texto voltado ao usuário final, em português.</param>
/// <param name="Tipo">Natureza da falha, que determina o status HTTP.</param>
/// <param name="Campo">Campo do payload que originou a falha, quando aplicável.</param>
public sealed record Erro(string Codigo, string Mensagem, ETipoErro Tipo, string? Campo = null)
{
    /// <summary>Cria uma falha de validação de entrada (400).</summary>
    public static Erro Validacao(string codigo, string mensagem, string? campo = null) => new(codigo, mensagem, ETipoErro.Validacao, campo);

    /// <summary>Cria uma falha de recurso inexistente (404).</summary>
    public static Erro NaoEncontrado(string codigo, string mensagem) => new(codigo, mensagem, ETipoErro.NaoEncontrado);

    /// <summary>Cria uma falha de conflito com o estado atual (409).</summary>
    public static Erro Conflito(string codigo, string mensagem) => new(codigo, mensagem, ETipoErro.Conflito);

    /// <summary>Cria uma falha de autenticação (401).</summary>
    public static Erro NaoAutenticado(string codigo, string mensagem) => new(codigo, mensagem, ETipoErro.NaoAutenticado);

    /// <summary>Cria uma falha de autorização (403).</summary>
    public static Erro Proibido(string codigo, string mensagem) => new(codigo, mensagem, ETipoErro.Proibido);

    /// <summary>Cria uma falha de dependência externa indisponível (503).</summary>
    public static Erro Indisponivel(string codigo, string mensagem) => new(codigo, mensagem, ETipoErro.Indisponivel);
}
