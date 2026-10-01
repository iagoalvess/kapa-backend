using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Arquivos;

/// <summary>
/// Download de arquivo: o conteúdo e o objeto de uma URL temporária.
/// </summary>
/// <remarks>
/// Regra de acesso: cada usuário enxerga os próprios arquivos, e <b>ninguém</b> enxerga os dos
/// outros — o administrador inclusive (Sprint 44, D4): foto e comprovante são dado pessoal que o
/// painel não usa. Arquivo de terceiro responde 404, e não 403 — devolver 403 confirmaria que o
/// identificador existe.
/// <para>
/// <b>Não existe envio avulso.</b> O arquivo sobe pelo endpoint da entidade que o justifica — o
/// comprovante junto da despesa, o documento junto do acervo, a foto junto do perfil —, numa
/// transação só e com um dono de domínio. Um <c>POST /arquivos</c> genérico devolveria um id solto
/// para o cliente amarrar depois, e a janela entre as duas requisições é onde nasce arquivo órfão.
/// Quem centraliza o envio é o <c>IArquivoService</c>, uma camada abaixo.
/// </para>
/// </remarks>
/// <param name="arquivoService">Regras de arquivo.</param>
/// <param name="usuarioAtual">Identidade da requisição.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/arquivos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ArquivoController(IArquivoService arquivoService, IUsuarioAtual usuarioAtual) : MainController
{
    private SolicitanteDeArquivo Solicitante => new(usuarioAtual.Id, PeloSistema: false);

    /// <summary>Baixa o conteúdo de um arquivo.</summary>
    /// <param name="id">Identificador do arquivo.</param>
    [HttpGet("{id:guid}/conteudo")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Baixar(Guid id, CancellationToken ct)
    {
        var resultado = await arquivoService.Baixar(id, Solicitante, ct);

        return Arquivo(resultado);
    }

    /// <summary>
    /// Serve o objeto de uma URL temporária do provedor local.
    /// </summary>
    /// <remarks>
    /// Anônimo de propósito: é o destino do redirecionamento de um download já autorizado, e quem
    /// segue um 302 não leva o bearer. Vale a assinatura e o prazo — adulterada ou vencida responde
    /// 404, igual a objeto inexistente. Com o S3 ativo, nenhuma URL aponta para cá.
    /// </remarks>
    /// <param name="objeto">Chave, nome, tipo, prazo e assinatura, da query string.</param>
    [AllowAnonymous]
    [HttpGet("temporario")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AbrirTemporario([FromQuery] ObjetoTemporario objeto, CancellationToken ct)
    {
        var resultado = await arquivoService.AbrirPorUrlTemporaria(objeto, ct);

        return Arquivo(resultado);
    }
}
