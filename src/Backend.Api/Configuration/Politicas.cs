using Backend.Business.Assinaturas.Models;
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
    /// É o piso de todo endpoint de domínio: além de exigir a claim <c>formatura_id</c>, confere no
    /// banco que o vínculo continua ativo — membro removido perde o acesso na requisição seguinte.
    /// Não existe política que peça só a claim, porque não existe endpoint a quem ela bastasse.
    /// </remarks>
    public const string MembroDaFormatura = nameof(MembroDaFormatura);

    /// <summary>
    /// <see cref="MembroDaFormatura"/> aceitando também quem foi <b>desligado</b> da turma. Vai só
    /// nas leituras do que é do próprio titular.
    /// </summary>
    /// <remarks>
    /// Quem sai deixa de dever, mas não deixa de ter pago: o extrato é a prova do que ele pagou, e
    /// tirá-la no mesmo instante faz do Kapa o lugar onde o comprovante some quando a pessoa mais
    /// precisa dele (P5 de 17/09/2026).
    /// <para>
    /// A lista é curta de propósito e não cresce sem uma decisão: <c>GET /formaturas/atual</c> (a
    /// moldura de toda tela), <c>GET /extrato/eu</c>, <c>GET /adesoes/eu</c> e o PDF do termo. Mural,
    /// acervo, dashboard da turma e toda outra escrita continuam em <see cref="MembroDaFormatura"/>,
    /// que exige vínculo ativo.
    /// </para>
    /// <para>
    /// Desde 23/09/2026 também a parcela, o PIX e o aviso de pagamento — só das <b>próprias</b>
    /// parcelas (<c>PagamentoService.ParcelaVisivel</c>): quando a comissão desliga mantendo o atraso, a
    /// dívida continua, e sem isso a pessoa não tinha por onde quitá-la.
    /// </para>
    /// </remarks>
    public const string TitularDoProprioHistorico = nameof(TitularDoProprioHistorico);

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
    /// A formatura não foi encerrada nem descartada: <c>Ativa</c> ou <c>Suspensa</c>. Vai no
    /// cadastro da própria pessoa.
    /// </summary>
    /// <remarks>
    /// O dado é do titular, não da turma: o Presidente preenche o dele antes de contratar, e o
    /// formando de uma turma suspensa ainda corrige o próprio endereço — é direito de retificação
    /// (LGPD, art. 18, III), e não pode depender de a licença estar em dia.
    /// </remarks>
    public const string ExigeFormaturaAberta = nameof(ExigeFormaturaAberta);

    /// <summary>
    /// O plano da turma inclui o módulo. Vai na área inteira — leitura e escrita.
    /// </summary>
    /// <remarks>
    /// Uma política por código de <c>Modulo</c>, registradas em laço: área nova ganha o gate só de
    /// existir na lista, e <b>o que cada plano libera continua sendo a linha do catálogo</b>, não
    /// código. Recusa com <c>plano.modulo_nao_incluido</c>.
    /// </remarks>
    /// <param name="modulo">Código do módulo, de <c>Modulo</c>.</param>
    /// <returns>O nome da política.</returns>
    public static string ExigeModulo(string modulo) => $"ExigeModulo:{modulo}";

    /// <summary>
    /// A formatura está <c>Ativa</c> ou <c>Suspensa</c>. Vai só no registro de dinheiro que já entrou:
    /// baixa, conferência de informe e o aviso de pagamento do formando.
    /// </summary>
    /// <remarks>
    /// Revisão de 17/09/2026. Suspensa é a turma que deixou a assinatura da Kapa vencer, e até aqui
    /// isso travava também a baixa — mas o dinheiro dos formandos continua caindo na conta PIX da
    /// comissão, porque a Kapa não é meio de pagamento (decisão de 14/09/2026). O efeito era o
    /// contrário do pretendido: quem pagou em dia seguia "vencido", com multa e juros correndo por
    /// uma dívida que é da comissão com a Kapa, não dele com a turma.
    /// <para>
    /// Registrar dinheiro que já entrou é escrituração do caixa da própria turma. O resto da suspensão
    /// continua valendo — plano, despesas, mural, convites e a régua seguem travados.
    /// </para>
    /// </remarks>
    public const string ExigeFormaturaRecebendo = nameof(ExigeFormaturaRecebendo);

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
            .AddPolicy(SomentePresidente, politica => politica.RequireAuthenticatedUser().ExigirPapel())
            .AddPolicy(Tesouraria, politica => politica.RequireAuthenticatedUser().ExigirPapel(PapelNaFormatura.Tesoureiro))
            .AddPolicy(Gestao, politica => politica.RequireAuthenticatedUser().ExigirPapel(PapelNaFormatura.Tesoureiro, PapelNaFormatura.Comissao))
            .AddPolicy(MembroDaFormatura, politica => politica.RequireAuthenticatedUser().ExigirPapel([.. PapelNaFormatura.Todos]))
            .AddPolicy(
                TitularDoProprioHistorico,
                politica =>
                    politica
                        .RequireAuthenticatedUser()
                        .RequireClaim(TokenService.ClaimDeFormatura)
                        .AddRequirements(new PapelNaFormaturaRequirement([.. PapelNaFormatura.Todos], aceitaDesligado: true))
            )
            .AddPolicy(ExigeFormaturaAtiva, politica => politica.ExigirStatus(StatusDaFormatura.Ativa))
            .AddPolicy(ExigeFormaturaRecebendo, politica => politica.ExigirStatus(StatusDaFormatura.Ativa, StatusDaFormatura.Suspensa))
            .AddPolicy(ExigeFormaturaAberta, politica => politica.ExigirStatus(StatusDaFormatura.Ativa, StatusDaFormatura.Suspensa));

        var porModulo = services.AddAuthorizationBuilder();

        foreach (var modulo in Modulo.Todos)
            porModulo.AddPolicy(ExigeModulo(modulo), politica => politica.AddRequirements(new PlanoComModuloRequirement(modulo)));

        services.AddScoped<IAuthorizationHandler, PapelNaFormaturaHandler>();
        services.AddScoped<IAuthorizationHandler, FormaturaEmStatusHandler>();
        services.AddScoped<IAuthorizationHandler, PlanoComModuloHandler>();

        return services;
    }

    private static AuthorizationPolicyBuilder ExigirStatus(this AuthorizationPolicyBuilder builder, params StatusDaFormatura[] aceitos) =>
        builder.RequireAuthenticatedUser().RequireClaim(TokenService.ClaimDeFormatura).AddRequirements(new FormaturaEmStatusRequirement(aceitos));
}
