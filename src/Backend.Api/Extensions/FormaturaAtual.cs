using System.Security.Claims;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Services;

namespace Backend.Api.Extensions;

/// <summary>
/// Lê a formatura da sessão a partir da claim <c>formatura_id</c> do access token.
/// </summary>
/// <remarks>
/// A formatura vem do token, e nunca de cabeçalho ou de rota: cabeçalho é escolhido pelo
/// cliente, e "o cliente diz de qual turma são os dados" é exatamente o buraco que o isolamento
/// existe para fechar. O token é assinado pela API, que só põe ali uma formatura cujo vínculo
/// acabou de ser conferido.
/// <para>
/// Requisição sem a claim — login, listagem de formaturas, cadastro — resolve para
/// <c>Id = null</c>, e nesse estado o filtro global não casa com linha nenhuma.
/// </para>
/// </remarks>
/// <param name="accessor">Acesso ao contexto da requisição.</param>
public sealed class FormaturaAtual(IHttpContextAccessor accessor) : IFormaturaAtual
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    /// <inheritdoc />
    public Guid? Id => Guid.TryParse(Principal?.FindFirstValue(TokenService.ClaimDeFormatura), out var id) ? id : null;
}
