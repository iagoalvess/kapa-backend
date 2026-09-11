using Backend.Business.Usuarios.Models;
using Microsoft.AspNetCore.Authorization;

namespace Backend.Api.Configuration;

/// <summary>
/// Políticas de autorização da API.
/// </summary>
/// <remarks>
/// Use <c>[Authorize(Policy = Politicas.X)]</c> e nunca <c>[Authorize(Roles = "texto")]</c>
/// espalhado pelos controllers: erro de digitação em string de papel não é erro de compilação,
/// vira 403 em produção — ou, pior, um endpoint que deveria ser restrito e não é.
/// <para>
/// <b>O administrador é coringa.</b> <see cref="ExigirPerfil"/> deixa passar quem tem o perfil
/// pedido <i>ou</i> o de administrador, então toda política nova já nasce acessível ao admin
/// sem ninguém precisar lembrar de incluí-lo na lista.
/// </para>
/// <para>
/// Quando um projeto precisar de permissão granular (módulo × ação), o ponto de extensão é
/// aqui: troque a asserção por um <c>IAuthorizationHandler</c> que consulte as permissões,
/// mantendo o atalho do administrador. Nada nos controllers muda.
/// </para>
/// </remarks>
public static class Politicas
{
    /// <summary>Exige o perfil de administrador. Usada no painel e na gestão de usuários.</summary>
    public const string SomenteAdministrador = nameof(SomenteAdministrador);

    /// <summary>Exige qualquer usuário autenticado e ativo.</summary>
    public const string Autenticado = nameof(Autenticado);

    /// <summary>
    /// Aceita qualquer um dos perfis informados, e sempre o administrador.
    /// </summary>
    /// <param name="builder">Construtor da política.</param>
    /// <param name="perfis">Perfis que também têm acesso.</param>
    public static AuthorizationPolicyBuilder ExigirPerfil(this AuthorizationPolicyBuilder builder, params string[] perfis) =>
        builder.RequireAssertion(contexto => contexto.User.IsInRole(PerfisPadrao.Administrador) || Array.Exists(perfis, contexto.User.IsInRole));

    /// <summary>Registra as políticas da aplicação.</summary>
    /// <param name="services">Coleção de serviços.</param>
    public static IServiceCollection AddPoliticas(this IServiceCollection services)
    {
        services
            .AddAuthorizationBuilder()
            .AddPolicy(Autenticado, politica => politica.RequireAuthenticatedUser())
            .AddPolicy(SomenteAdministrador, politica => politica.RequireAuthenticatedUser().ExigirPerfil());

        return services;
    }
}
