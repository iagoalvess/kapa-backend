using System.Net.Http.Json;
using Backend.Api.DTOs.Auth;
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
    private static readonly Dictionary<StatusDaFormatura, StatusDaFormatura[]> Caminhos = new()
    {
        [StatusDaFormatura.Rascunho] = [],
        [StatusDaFormatura.AguardandoPagamento] = [StatusDaFormatura.AguardandoPagamento],
        [StatusDaFormatura.Ativa] = [StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Ativa],
        [StatusDaFormatura.Suspensa] = [StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Ativa, StatusDaFormatura.Suspensa],
        [StatusDaFormatura.Encerrada] = [StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Ativa, StatusDaFormatura.Encerrada],
    };

    /// <summary>
    /// Monta uma formatura no status pedido, passando pelas transições de verdade.
    /// </summary>
    /// <remarks>
    /// O criador é sorteado: o índice de "um rascunho por criador" não pode fazer um teste colidir
    /// com outro.
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

        foreach (var passo in Caminhos[status])
            formatura.Transicionar(passo).Sucesso.ShouldBeTrue();

        return formatura;
    }

    /// <summary>Cria uma formatura ativa, sem membros.</summary>
    /// <param name="fabrica">API de teste.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public static Task<Guid> CriarFormatura(this ApiFactory fabrica, CancellationToken ct) => fabrica.CriarFormatura(StatusDaFormatura.Ativa, ct);

    /// <summary>Cria uma formatura no status pedido, sem membros.</summary>
    /// <param name="fabrica">API de teste.</param>
    /// <param name="status">Status da formatura.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public static async Task<Guid> CriarFormatura(this ApiFactory fabrica, StatusDaFormatura status, CancellationToken ct)
    {
        await using var contexto = fabrica.ContextoDe(null);

        var formatura = NovaFormatura(status);
        contexto.Formaturas.Add(formatura);

        await contexto.SaveChangesAsync(ct);

        return formatura.Id;
    }

    /// <summary>Registra uma conta nova, vincula à formatura com o papel e seleciona a formatura.</summary>
    /// <param name="fabrica">API de teste.</param>
    /// <param name="formaturaId">Formatura do vínculo.</param>
    /// <param name="papel">Papel do membro.</param>
    /// <param name="ct">Token de cancelamento.</param>
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

        var selecionados = (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(ct))!;

        return new MembroDeTeste(cliente.ComToken(selecionados.AccessToken), usuarioId);
    }

    /// <summary>Marca o e-mail da conta como confirmado, como se o link do e-mail tivesse sido aberto.</summary>
    /// <param name="fabrica">API de teste.</param>
    /// <param name="email">E-mail da conta.</param>
    /// <param name="ct">Token de cancelamento.</param>
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
    /// <param name="ct">Token de cancelamento.</param>
    public static async Task<string?> Codigo(this HttpResponseMessage resposta, CancellationToken ct)
    {
        var problema = await resposta.Content.ReadFromJsonAsync<Dictionary<string, object>>(ct);

        return problema?.GetValueOrDefault("codigo")?.ToString();
    }
}
