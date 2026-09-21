using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Auth;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Formaturas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace Backend.IntegrationTests.Infra;

/// <summary>Um membro de turma pronto para chamar a API.</summary>
/// <param name="Cliente">Cliente já autenticado, com a formatura selecionada no token.</param>
/// <param name="UsuarioId">Usuário do membro.</param>
public sealed record MembroDeTeste(HttpClient Cliente, Guid UsuarioId);

/// <summary>
/// Monta turmas com membros em papéis definidos.
/// </summary>
/// <remarks>
/// A formatura e o vínculo são gravados direto no banco — mais rápido que o fluxo de criação, e
/// permite montar qualquer status —, mas o token vem da API de verdade, pela seleção de formatura:
/// é ele que as políticas leem.
/// </remarks>
public static class FormaturaDeTeste
{
    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    /// <summary>
    /// Monta uma formatura no status pedido, passando pelas transições de verdade.
    /// </summary>
    /// <remarks>
    /// O criador é sorteado, para um teste não colidir com outro.
    /// <para>
    /// A turma nasce <c>Ativa</c>, então o caminho até qualquer outro status é um passo só.
    /// </para>
    /// </remarks>
    /// <param name="status">Status final.</param>
    public static Formatura NovaFormatura(StatusDaFormatura status = StatusDaFormatura.Ativa)
    {
        var formatura = new Formatura
        {
            Nome = $"Turma {Guid.CreateVersion7():N}",
            Instituicao = "Universidade de Teste",
            Curso = "Curso de Teste",
            Ano = DateTime.UtcNow.Year + 1,
            Semestre = 1,
            QuantidadeEstimadaDeFormandos = 50,
            CriadoPorUsuarioId = Guid.CreateVersion7(),
        };

        foreach (var passo in status == StatusDaFormatura.Ativa ? Array.Empty<StatusDaFormatura>() : [status])
            formatura.Transicionar(passo).Sucesso.ShouldBeTrue();

        return formatura;
    }

    /// <summary>Cria uma formatura ativa e contratada, sem membros.</summary>
    /// <param name="fabrica">API de teste.</param>
    public static Task<Guid> CriarFormatura(this ApiFactory fabrica, CancellationToken ct) => fabrica.CriarFormatura(StatusDaFormatura.Ativa, ct);

    /// <summary>Cria uma formatura no status pedido, sem membros.</summary>
    /// <remarks>
    /// <b>Contratada por padrão</b>, no maior plano. Sem assinatura a turma cai no gratuito, e o
    /// gratuito não inclui mural, festa, despesas nem caixa — a política de módulo recusaria com
    /// <c>plano.modulo_nao_incluido</c> e a suíte inteira desses módulos falharia por um motivo que
    /// não é o que ela testa. Quem quer testar o gratuito passa <c>contratada: false</c>.
    /// </remarks>
    /// <param name="fabrica">API de teste.</param>
    /// <param name="status">Status da formatura.</param>
    /// <param name="contratada">Se a turma nasce com assinatura ativa no maior plano.</param>
    public static async Task<Guid> CriarFormatura(this ApiFactory fabrica, StatusDaFormatura status, CancellationToken ct, bool contratada = true)
    {
        Guid formaturaId;

        await using (var contexto = fabrica.ContextoDe(null))
        {
            var formatura = NovaFormatura(status);
            contexto.Formaturas.Add(formatura);

            await contexto.SaveChangesAsync(ct);
            formaturaId = formatura.Id;
        }

        if (contratada)
            await fabrica.Contratar(formaturaId, ct);

        return formaturaId;
    }

    /// <summary>Dá à turma uma assinatura ativa no plano mais completo do catálogo.</summary>
    /// <remarks>
    /// O escopo do contexto é apontado para a turma: <c>Assinatura</c> é <c>EntidadeDaFormatura</c>,
    /// e o carimbo do <c>AppDbContext</c> recusa gravar sem formatura na sessão.
    /// <para>
    /// <b>Ordena por quantidade de módulos, não por limite de formandos.</b> Quem contrata aqui é
    /// todo teste que exercita área com <c>[ExigeModulo]</c>, e o que ele quer é o plano que libera
    /// tudo. Por limite havia empate em 400 entre o plano de topo e o <c>ampliado</c> que a
    /// <c>Inicial</c> gravou sem módulo nenhum — e no dia em que o desempate mudou de lado, metade
    /// da suíte passou a responder 403 sem que nada de autorização tivesse sido tocado.
    /// </para>
    /// </remarks>
    /// <param name="fabrica">API de teste.</param>
    /// <param name="formaturaId">Turma que contrata.</param>
    public static async Task Contratar(this ApiFactory fabrica, Guid formaturaId, CancellationToken ct)
    {
        await using var contexto = fabrica.ContextoDe(formaturaId);

        var plano = await contexto
            .Planos.Where(p => p.Ativo)
            .OrderByDescending(p => p.Modulos.Count)
            .ThenByDescending(p => p.LimiteDeFormandos)
            .FirstAsync(ct);
        var assinatura = new Assinatura { PlanoId = plano.Id };
        assinatura.ConfirmarPagamento(DateTime.UtcNow, plano.Ciclo);

        contexto.Assinaturas.Add(assinatura);
        await contexto.SaveChangesAsync(ct);
    }

    /// <summary>Registra uma conta nova, vincula à formatura com o papel e seleciona a formatura.</summary>
    /// <param name="fabrica">API de teste.</param>
    /// <param name="formaturaId">Formatura do vínculo.</param>
    /// <param name="papel">Papel do membro.</param>
    public static async Task<MembroDeTeste> NovoMembro(this ApiFactory fabrica, Guid formaturaId, string papel, CancellationToken ct)
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(ct);
        var usuarioId = IdDoUsuario(tokens.AccessToken);

        await using (var contexto = fabrica.ContextoDe(null))
        {
            contexto.Vinculos.Add(
                new VinculoDeFormatura
                {
                    UsuarioId = usuarioId,
                    FormaturaId = formaturaId,
                    Papel = papel,
                }
            );

            await contexto.SaveChangesAsync(ct);
        }

        var resposta = await cliente.ComToken(tokens.AccessToken).PostAsync($"/api/v1/formaturas/{formaturaId}/selecionar", null, ct);
        resposta.EnsureSuccessStatusCode();

        var selecionados = (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(Json, ct))!;

        return new MembroDeTeste(cliente.ComToken(selecionados.AccessToken), usuarioId);
    }

    /// <summary>Marca o e-mail da conta como confirmado, como se o link do e-mail tivesse sido aberto.</summary>
    /// <param name="fabrica">API de teste.</param>
    /// <param name="email">E-mail da conta.</param>
    public static async Task ConfirmarEmail(this ApiFactory fabrica, string email, CancellationToken ct)
    {
        await using var contexto = fabrica.ContextoDe(null);

        await contexto.Users.Where(u => u.Email == email).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailConfirmed, true), ct);
    }

    /// <summary>Lê o id do usuário do access token.</summary>
    /// <param name="accessToken">Token emitido pela API.</param>
    public static Guid IdDoUsuario(string accessToken) =>
        Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(accessToken).Claims.First(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value);

    /// <summary>Código estável do erro devolvido pela API.</summary>
    /// <param name="resposta">Resposta de falha.</param>
    public static async Task<string?> Codigo(this HttpResponseMessage resposta, CancellationToken ct)
    {
        var problema = await resposta.Content.ReadFromJsonAsync<Dictionary<string, object>>(Json, ct);

        return problema?.GetValueOrDefault("codigo")?.ToString();
    }
}
