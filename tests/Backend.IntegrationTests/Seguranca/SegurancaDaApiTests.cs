using System.Net;
using System.Security.Claims;
using System.Text;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace Backend.IntegrationTests.Seguranca;

/// <summary>
/// As defesas de borda que valem para a API inteira: assinatura do JWT, cabeçalhos de segurança e o
/// inventário de rotas — nenhuma rota nasce anônima por esquecimento.
/// </summary>
/// <remarks>
/// O isolamento por formatura e a matriz de papéis já têm arquivo próprio. Aqui é o que protege
/// <b>toda</b> requisição antes de qualquer regra de domínio rodar: um token forjado não entra, uma
/// resposta do navegador vem blindada, e uma rota nova protegida por engano falha no build da suíte
/// em vez de estrear aberta.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class SegurancaDaApiTests(ApiFactory fabrica)
{
    /// <summary>Rota protegida por <c>[Authorize]</c> que exige apenas um token válido.</summary>
    private const string RotaProtegida = "/api/v1/formaturas/minhas";

    /// <summary>Mesma chave que a fábrica injeta em <c>Jwt:ChaveSecreta</c>.</summary>
    private const string Emissor = "backend-testes";

    private const string Audiencia = "clientes-testes";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] ChaveValida = Encoding.UTF8.GetBytes("chave-de-teste-com-mais-de-32-caracteres-ok");

    [Fact]
    public async Task Sem_token_a_rota_protegida_responde_401()
    {
        var resposta = await fabrica.CreateClient().GetAsync(RotaProtegida, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_adulterado_na_assinatura_e_recusado()
    {
        var tokens = await fabrica.CreateClient().RegistrarUsuarioComum(Ct);

        var resposta = await fabrica.CreateClient().ComToken(AdulterarAssinatura(tokens.AccessToken)).GetAsync(RotaProtegida, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Cada variação de token forjado precisaria de uma configuração errada para passar: chave
    /// diferente, emissor ou audiência de outro sistema, token vencido, <c>alg: none</c>.
    /// </summary>
    /// <param name="descricao">O que torna o token inválido, para o relatório da falha.</param>
    /// <param name="token">O token forjado.</param>
    [Theory]
    [MemberData(nameof(TokensForjados))]
    public async Task Token_forjado_e_recusado(string descricao, string token)
    {
        var resposta = await fabrica.CreateClient().ComToken(token).GetAsync(RotaProtegida, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, descricao);
    }

    /// <summary>Os cenários clássicos de forja de JWT, um por caso.</summary>
    public static IEnumerable<object[]> TokensForjados() =>
        [
            ["chave de assinatura diferente", AssinarCom(ChaveAleatoria(), Emissor, Audiencia, TimeSpan.FromMinutes(15))],
            ["emissor desconhecido", AssinarCom(ChaveValida, "outro-emissor", Audiencia, TimeSpan.FromMinutes(15))],
            ["audiência desconhecida", AssinarCom(ChaveValida, Emissor, "outra-audiencia", TimeSpan.FromMinutes(15))],
            ["token vencido", AssinarCom(ChaveValida, Emissor, Audiencia, TimeSpan.FromMinutes(-5))],
            ["algoritmo none (sem assinatura)", SemAssinatura()],
            ["texto que não é um JWT", "nao-e-um-token"],
        ];

    /// <summary>Cabeçalhos de segurança escritos em toda resposta, inclusive nas anônimas.</summary>
    [Fact]
    public async Task Resposta_traz_os_cabecalhos_de_seguranca()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/api/v1/planos", Ct);

        resposta.EnsureSuccessStatusCode();
        resposta.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        resposta.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        resposta.Headers.GetValues("Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"]);
    }

    /// <summary>
    /// Toda rota é anônima <b>de propósito</b> (com <c>[AllowAnonymous]</c>) ou exige autorização.
    /// </summary>
    /// <remarks>
    /// A proteção padrão vem do <c>[Authorize]</c> de classe do <c>MainController</c>. Um controller
    /// novo que herde de <c>ControllerBase</c> e esqueça o atributo estrearia público e sem teste
    /// vermelho nenhum — este é o teste que fica vermelho.
    /// </remarks>
    [Fact]
    public void Todo_endpoint_tem_autorizacao_ou_anonimo_explicito()
    {
        var semProtecao = Endpoints()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(Descrever)
            .ToArray();

        semProtecao.ShouldBeEmpty($"rotas sem autorização e sem [AllowAnonymous] explícito: {string.Join(", ", semProtecao)}");
    }

    /// <summary>
    /// Toda rota anônima está na lista de exceções conhecidas. Um endpoint novo marcado
    /// <c>[AllowAnonymous]</c> por descuido não passa em branco.
    /// </summary>
    [Fact]
    public void Rota_anonima_esta_na_lista_de_excecoes_conhecidas()
    {
        var anonimas = Endpoints().Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null).Select(Descrever).ToArray();

        var inesperadas = anonimas.Where(rota => !AnonimaPermitida(rota)).ToArray();

        inesperadas.ShouldBeEmpty($"rotas anônimas não declaradas: {string.Join(", ", inesperadas)}");
    }

    /// <summary>As áreas mais sensíveis não podem ter rota anônima nenhuma.</summary>
    /// <param name="prefixo">Prefixo da rota.</param>
    [Theory]
    [InlineData("/admin")]
    [InlineData("/usuarios")]
    [InlineData("/financeiro")]
    [InlineData("/parcelas")]
    [InlineData("/recebimentos")]
    [InlineData("/auditoria")]
    [InlineData("/relatorios")]
    public void Area_sensivel_nao_tem_rota_anonima(string prefixo)
    {
        var anonimas = Endpoints()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Descrever)
            .Where(rota => rota.Contains(prefixo, StringComparison.Ordinal))
            .ToArray();

        anonimas.ShouldBeEmpty($"{prefixo} tem rota anônima: {string.Join(", ", anonimas)}");
    }

    private IEnumerable<RouteEndpoint> Endpoints() => fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

    /// <summary>O caminho da rota, para a mensagem de falha e a lista de exceções.</summary>
    private static string Descrever(RouteEndpoint endpoint) => endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "rota sem padrão";

    /// <summary>
    /// As rotas públicas legítimas, por prefixo: sessão, ciclo de conta, vitrine, loja pública,
    /// convite por token, webhooks assinados, ferramentas de desenvolvimento e o health check.
    /// </summary>
    private static bool AnonimaPermitida(string rota)
    {
        var caminho = "/" + rota.TrimStart('/');

        return PrefixosAnonimos.Any(prefixo => caminho.StartsWith(prefixo, StringComparison.Ordinal));
    }

    private static readonly string[] PrefixosAnonimos =
    [
        "/api/v{version:apiVersion}/auth/",
        "/api/v{version:apiVersion}/conta/",
        "/api/v{version:apiVersion}/legal/",
        "/api/v{version:apiVersion}/planos",
        "/api/v{version:apiVersion}/arquivos/temporario",
        "/api/v{version:apiVersion}/privacidade/operadores",
        "/api/v{version:apiVersion}/privacidade/descadastro",
        "/api/v{version:apiVersion}/loja/",
        "/api/v{version:apiVersion}/convites/",
        "/api/v{version:apiVersion}/festa/convites/",
        "/api/v{version:apiVersion}/webhooks/",
        "/api/v{version:apiVersion}/mercado-pago/retorno",
        "/api/v{version:apiVersion}/provedor-fake/",
        "/api/v{version:apiVersion}/amostra-de-emails",
        "/api/v{version:apiVersion}/marca/",
        "/health",
        "/scalar",
        "/openapi",
    ];

    private static string AdulterarAssinatura(string token)
    {
        var partes = token.Split('.');

        partes[2] = partes[2][..^1] + (partes[2][^1] == 'A' ? 'B' : 'A');

        return string.Join('.', partes);
    }

    private static string AssinarCom(byte[] chave, string emissor, string audiencia, TimeSpan validade)
    {
        var agora = DateTime.UtcNow;

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = emissor,
            Audience = audiencia,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString())]),
            IssuedAt = agora,
            NotBefore = agora,
            Expires = agora.Add(validade),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(chave), SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descritor);
    }

    /// <summary>Um JWT com <c>alg: none</c> e sem a terceira parte — o clássico "confie na minha header".</summary>
    private static string SemAssinatura()
    {
        var expira = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();

        var cabecalho = Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var corpo = Base64Url(
            $"{{\"sub\":\"{Guid.CreateVersion7():N}\",\"iss\":\"{Emissor}\",\"aud\":\"{Audiencia}\",\"exp\":{expira.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"
        );

        return $"{cabecalho}.{corpo}.";
    }

    private static string Base64Url(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));

    private static byte[] ChaveAleatoria() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
}
