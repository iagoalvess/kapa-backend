using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Auth;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Auth.Settings;
using Backend.Business.Legal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Backend.Api.Controllers.V1.Auth;

/// <summary>Login Google, cadastro curto e comprovação de vínculo; administrador usa apenas senha e código.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/google")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitConfig.Entrada)]
public sealed class GoogleController(
    IGoogleService google,
    GoogleHttp transporte,
    SessaoHttp sessao,
    IUsuarioAtual usuario,
    IOptions<GoogleSettings> options
) : MainController
{
    /// <summary>Configuração pública efetiva; permite ocultar o botão quando a API não atende SSO.</summary>
    [HttpGet("configuracao")]
    public IActionResult Configuracao() => Ok(new { ClientId = options.Value.Habilitado ? options.Value.ClientId : null });

    /// <summary>Inicia tentativa e nonce, com origem conferida e cookie de navegador.</summary>
    [HttpPost("iniciar")]
    public async Task<IActionResult> Iniciar([FromBody] InicioGoogleRequestDTO dados, CancellationToken ct)
    {
        var navegador = transporte.Conferir(iniciar: true);
        return navegador.Falhou ? Responder(Result.Falha(navegador.Erros)) : Responder(await google.Iniciar(navegador.Valor, dados.Convite, ct));
    }

    /// <summary>Valida credencial e retorna sessão ou contexto de cadastro/vínculo.</summary>
    [HttpPost]
    public async Task<IActionResult> Entrar([FromBody] GoogleRequestDTO dados, CancellationToken ct)
    {
        var navegador = transporte.Conferir();
        if (navegador.Falhou)
            return Responder(Result.Falha(navegador.Erros));
        var resultado = await google.Entrar(dados.Tentativa, dados.Credencial, navegador.Valor, usuario.EnderecoIp, ct);
        if (resultado is { Sucesso: true, Valor.ProximoPasso: { } proximo })
        {
            transporte.GuardarContexto(proximo.Contexto);
            return Responder(Result.Falha(resultado.Valor.ErroDoProximoPasso! with { Dados = proximo }));
        }
        return Responder(sessao.Preparar(resultado.Map(valor => valor.Sessao!)));
    }

    /// <summary>Cria sem senha, com consentimentos e vínculo em transação.</summary>
    [HttpPost("cadastro")]
    public async Task<IActionResult> Cadastrar([FromBody] CadastroGoogleRequestDTO dados, CancellationToken ct)
    {
        var navegador = transporte.Conferir();
        if (navegador.Falhou)
            return Responder(Result.Falha(navegador.Erros));
        var cadastro = new CadastroGoogle(
            dados.Contexto,
            dados.Nome,
            [.. (dados.Aceites ?? []).OfType<AceiteDeDocumentoDTO>().Select(a => new AceiteDeDocumento(a.Tipo, a.Versao))],
            dados.ReceberComunicacaoDoKapa ?? false
        );
        var resultado = await google.Cadastrar(cadastro, navegador.Valor, usuario.Origem, ct);
        if (resultado.Sucesso)
        {
            transporte.LimparContexto();
            return Created(string.Empty, sessao.Preparar(resultado).Valor);
        }
        return Responder(sessao.Preparar(resultado));
    }

    /// <summary>Envia link de comprovação; resposta genérica.</summary>
    [HttpPost("vinculo/solicitar")]
    public async Task<IActionResult> Solicitar([FromBody] SolicitarVinculoGoogleRequestDTO dados, CancellationToken ct)
    {
        var navegador = transporte.Conferir();
        return navegador.Falhou
            ? Responder(Result.Falha(navegador.Erros))
            : Responder(await google.SolicitarVinculo(dados.Contexto, navegador.Valor, ct));
    }

    /// <summary>Aplica vínculo comprovado e finaliza entrada pelo mesmo mecanismo da senha.</summary>
    [HttpPost("vinculo")]
    public async Task<IActionResult> Vincular([FromBody] VinculoGoogleRequestDTO dados, CancellationToken ct)
    {
        var navegador = transporte.Conferir();
        if (navegador.Falhou)
            return Responder(Result.Falha(navegador.Erros));
        var resultado = await google.Vincular(transporte.LerContexto(dados.Contexto), dados.Comprovante, navegador.Valor, usuario.EnderecoIp, ct);
        if (resultado.Sucesso)
            transporte.LimparContexto();
        return Responder(sessao.Preparar(resultado));
    }
}
