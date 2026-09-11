using Backend.Api.DTOs.Auth;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Models;
using Backend.Business.Auth.Settings;

namespace Backend.Api.Configuration;

/// <summary>
/// Converte um par de tokens na resposta que o cliente recebe.
/// </summary>
/// <remarks>
/// Compartilhado porque mais de um endpoint emite sessão — login, registro, renovação e a
/// seleção de formatura. Montar o <c>TokenResponseDTO</c> na mão em cada um deles é como o
/// refresh token volta para o corpo da resposta no dia em que alguém copiar o trecho errado.
/// </remarks>
public static class RespostaDeSessao
{
    /// <summary>
    /// Prepara o corpo da resposta, mandando o refresh token pelo cookie quando o modo está ligado.
    /// </summary>
    /// <param name="resultado">Resultado devolvido pelo service.</param>
    /// <param name="resposta">Resposta em construção.</param>
    /// <param name="cookie">Configuração do cookie de sessão.</param>
    /// <param name="jwt">Configuração de JWT, que define a validade do cookie.</param>
    /// <returns>O resultado já no formato do contrato público, ou a falha intacta.</returns>
    public static Result<TokenResponseDTO> Preparar(
        Result<ParDeTokens> resultado,
        HttpResponse resposta,
        CookieDeSessaoSettings cookie,
        JwtSettings jwt
    )
    {
        if (resultado.Falhou)
            return Result.Falha<TokenResponseDTO>(resultado.Erros);

        var par = resultado.Valor;

        if (!cookie.Habilitado)
            return Result.Ok(new TokenResponseDTO(par.AccessToken, par.ExpiraEm, par.RefreshToken));

        resposta.Gravar(par.RefreshToken, cookie, jwt);

        return Result.Ok(new TokenResponseDTO(par.AccessToken, par.ExpiraEm, null));
    }
}
