using Backend.Business.Auth.Services;
using Backend.Business.Formaturas.Models;
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
    /// Exige que a sessão tenha uma formatura selecionada.
    /// </summary>
    /// <remarks>
    /// É a política de todo endpoint de domínio da formatura. Sem ela, um token válido porém
    /// sem <c>formatura_id</c> chegaria ao repositório, o filtro global não casaria com linha
    /// nenhuma e o usuário veria uma tela vazia em vez de ser mandado para a seleção.
    /// </remarks>
    public const string FormaturaSelecionada = nameof(FormaturaSelecionada);

    /// <summary>
    /// Só o Presidente da formatura selecionada: dados da turma, assinatura, membros e papéis.
    /// </summary>
    public const string SomentePresidente = nameof(SomentePresidente);

    /// <summary>Presidente e Tesoureiro: cobranças, despesas, baixa manual e renegociação.</summary>
    public const string Tesouraria = nameof(Tesouraria);

    /// <summary>Tesouraria e Comissão: extrato de qualquer formando, avisos e documentos.</summary>
    public const string Gestao = nameof(Gestao);

    /// <summary>
    /// Qualquer vínculo ativo na formatura selecionada: dashboard público e o próprio extrato.
    /// </summary>
    /// <remarks>
    /// Mais estrita que <see cref="FormaturaSelecionada"/>: além da claim, confere no banco que o
    /// vínculo continua ativo — membro removido perde o acesso na requisição seguinte.
    /// </remarks>
    public const string MembroDaFormatura = nameof(MembroDaFormatura);

    /// <summary>
    /// A formatura selecionada precisa estar <c>Ativa</c>. Vai em <b>toda escrita de domínio</b>,
    /// somada à política de papel.
    /// </summary>
    /// <remarks>
    /// Separa leitura de escrita: leitura pede só o papel, então uma turma suspensa continua
    /// enxergando tudo. Recusa com <c>formatura.inativa</c>.
    /// </remarks>
    public const string ExigeFormaturaAtiva = nameof(ExigeFormaturaAtiva);

    /// <summary>
    /// A formatura aceita edição: <c>Rascunho</c>, <c>AguardandoPagamento</c> ou <c>Ativa</c>.
    /// Vai na montagem da comissão — convidar e ajustar membros.
    /// </summary>
    /// <remarks>
    /// Contratar é decisão da comissão, não do Presidente sozinho: ele precisa chamar o tesoureiro e
    /// os colegas antes de pôr o cartão. Formando só entra depois do pagamento — essa regra é do
    /// <c>ConviteService</c>, porque depende do papel do convite.
    /// </remarks>
    public const string ExigeFormaturaEditavel = nameof(ExigeFormaturaEditavel);

    /// <summary>
    /// A formatura não foi encerrada nem descartada. Vai no cadastro da própria pessoa.
    /// </summary>
    /// <remarks>
    /// O dado é do titular, não da turma: o Presidente preenche o dele antes de contratar, e o
    /// formando de uma turma suspensa ainda corrige o próprio endereço — é direito de retificação
    /// (LGPD, art. 18, III), e não pode depender de a licença estar em dia.
    /// </remarks>
    public const string ExigeFormaturaAberta = nameof(ExigeFormaturaAberta);

    /// <summary>
    /// Aceita qualquer um dos perfis informados, e sempre o administrador.
    /// </summary>
    /// <param name="builder">Construtor da política.</param>
    /// <param name="perfis">Perfis que também têm acesso.</param>
    public static AuthorizationPolicyBuilder ExigirPerfil(this AuthorizationPolicyBuilder builder, params string[] perfis) =>
        builder.RequireAssertion(contexto => contexto.User.IsInRole(PerfisPadrao.Administrador) || Array.Exists(perfis, contexto.User.IsInRole));

    /// <summary>
    /// Exige um dos papéis informados na formatura selecionada. Presidente sempre passa.
    /// </summary>
    /// <remarks>
    /// Papel é do vínculo, não é role do Identity: a mesma pessoa pode ser Tesoureira numa turma e
    /// Formanda em outra, e role global não expressa isso.
    /// <para>
    /// O papel é conferido <b>no vínculo gravado</b>, a cada requisição, por
    /// <see cref="PapelNaFormaturaHandler"/> — e não na claim <c>papel</c> do token, que é só uma
    /// fotografia da emissão. Rebaixar ou remover alguém vale na requisição seguinte, não daqui a
    /// quinze minutos.
    /// </para>
    /// <para>
    /// A claim <c>formatura_id</c> é exigida junto — papel sem formatura não significa nada — e
    /// sem ela o 403 sai como <c>formatura.nao_selecionada</c> (ver
    /// <see cref="RespostaDeAutorizacao"/>).
    /// </para>
    /// <para>
    /// <b>O Presidente é coringa dentro da formatura</b>, como o administrador é na plataforma:
    /// toda política nova nasce acessível a ele sem ninguém lembrar de incluí-lo.
    /// </para>
    /// </remarks>
    /// <param name="builder">Construtor da política.</param>
    /// <param name="papeis">Papéis que também têm acesso.</param>
    public static AuthorizationPolicyBuilder ExigirPapel(this AuthorizationPolicyBuilder builder, params string[] papeis) =>
        builder.RequireClaim(TokenService.ClaimDeFormatura).AddRequirements(new PapelNaFormaturaRequirement(papeis));

    /// <summary>Registra as políticas da aplicação.</summary>
    /// <remarks>
    /// As quatro políticas de papel são a matriz de permissões da Sprint 1 inteira: cada célula
    /// dela vira uma destas ou some. <c>MatrizDePermissoesTests</c> confere as quatro contra a
    /// tabela, papel por papel.
    /// </remarks>
    /// <param name="services">Coleção de serviços.</param>
    public static IServiceCollection AddPoliticas(this IServiceCollection services)
    {
        services
            .AddAuthorizationBuilder()
            .AddPolicy(Autenticado, politica => politica.RequireAuthenticatedUser())
            .AddPolicy(SomenteAdministrador, politica => politica.RequireAuthenticatedUser().ExigirPerfil())
            .AddPolicy(FormaturaSelecionada, politica => politica.RequireAuthenticatedUser().RequireClaim(TokenService.ClaimDeFormatura))
            .AddPolicy(SomentePresidente, politica => politica.RequireAuthenticatedUser().ExigirPapel())
            .AddPolicy(Tesouraria, politica => politica.RequireAuthenticatedUser().ExigirPapel(PapelNaFormatura.Tesoureiro))
            .AddPolicy(Gestao, politica => politica.RequireAuthenticatedUser().ExigirPapel(PapelNaFormatura.Tesoureiro, PapelNaFormatura.Comissao))
            .AddPolicy(MembroDaFormatura, politica => politica.RequireAuthenticatedUser().ExigirPapel([.. PapelNaFormatura.Todos]))
            .AddPolicy(ExigeFormaturaAtiva, politica => politica.ExigirStatus(StatusDaFormatura.Ativa))
            .AddPolicy(
                ExigeFormaturaEditavel,
                politica => politica.ExigirStatus(StatusDaFormatura.Rascunho, StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Ativa)
            )
            .AddPolicy(
                ExigeFormaturaAberta,
                politica =>
                    politica.ExigirStatus(
                        StatusDaFormatura.Rascunho,
                        StatusDaFormatura.AguardandoPagamento,
                        StatusDaFormatura.Ativa,
                        StatusDaFormatura.Suspensa
                    )
            );

        services.AddScoped<IAuthorizationHandler, PapelNaFormaturaHandler>();
        services.AddScoped<IAuthorizationHandler, FormaturaEmStatusHandler>();

        return services;
    }

    private static AuthorizationPolicyBuilder ExigirStatus(this AuthorizationPolicyBuilder builder, params StatusDaFormatura[] aceitos) =>
        builder.RequireAuthenticatedUser().RequireClaim(TokenService.ClaimDeFormatura).AddRequirements(new FormaturaEmStatusRequirement(aceitos));
}
