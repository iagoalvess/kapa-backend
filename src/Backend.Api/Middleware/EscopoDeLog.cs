using System.Security.Claims;
using Backend.Business.Auth.Services;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Backend.Api.Middleware;

/// <summary>
/// Põe quem fez a requisição no escopo de <b>todo</b> log dela: usuário, formatura e id de correlação.
/// </summary>
/// <remarks>
/// O log estruturado já registra os campos onde o código os escreve, e o <c>ProblemDetails</c> devolve
/// o <c>trace_id</c>. O que faltava era o "quem" aparecer sozinho em cada linha, para o agregador
/// filtrar por usuário sem depender de cada chamador lembrar de passar o campo.
/// <para>
/// Lê as claims direto do <see cref="ClaimsPrincipal"/>: roda <b>depois</b> da autenticação, então o
/// token já foi validado e o escopo é empilhado para o resto do pipeline. O escopo vale para os outros
/// loggers porque a implementação padrão do <c>ILoggerFactory</c> guarda os escopos num
/// <c>AsyncLocal</c> do fluxo.
/// </para>
/// <para>
/// Requisição anônima não ganha <c>UsuarioId</c> nem <c>FormaturaId</c>, e continua com o
/// <c>TraceId</c> para correlacionar. A formatura apontada por rota assinada (loja, webhook) é
/// resolvida dentro do service, depois deste ponto, e por isso não entra no escopo.
/// </para>
/// </remarks>
/// <param name="proximo">O restante do pipeline.</param>
public sealed class EscopoDeLog(RequestDelegate proximo)
{
    /// <summary>Chave do identificador do usuário autenticado no escopo de log.</summary>
    public const string ChaveDeUsuario = "UsuarioId";

    /// <summary>Chave da formatura da sessão no escopo de log.</summary>
    public const string ChaveDeFormatura = "FormaturaId";

    /// <summary>Chave do identificador de correlação da requisição no escopo de log.</summary>
    public const string ChaveDeTrace = "TraceId";

    /// <summary>Empilha o escopo e segue.</summary>
    /// <param name="contexto">Contexto HTTP, com o usuário já autenticado.</param>
    /// <param name="logger">Logger usado para abrir o escopo.</param>
    public async Task InvokeAsync(HttpContext contexto, ILogger<EscopoDeLog> logger)
    {
        var escopo = new Dictionary<string, object?> { [ChaveDeTrace] = contexto.TraceIdentifier };

        if (contexto.User.FindFirstValue(JwtRegisteredClaimNames.Sub) is { Length: > 0 } usuarioId)
            escopo[ChaveDeUsuario] = usuarioId;

        if (contexto.User.FindFirstValue(TokenService.ClaimDeFormatura) is { Length: > 0 } formaturaId)
            escopo[ChaveDeFormatura] = formaturaId;

        using (logger.BeginScope(escopo))
            await proximo(contexto);
    }
}
