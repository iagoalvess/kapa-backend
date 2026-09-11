using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Arquivos;
using Backend.Api.DTOs.Comum;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Mapster;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Arquivos;

/// <summary>
/// Envio, download e remoção de arquivos.
/// </summary>
/// <remarks>
/// Regra de acesso padrão: cada usuário enxerga os próprios arquivos, e o administrador enxerga
/// todos. Arquivo de terceiro responde 404, e não 403 — devolver 403 confirmaria que o
/// identificador existe.
/// </remarks>
/// <param name="arquivoService">Regras de arquivo.</param>
/// <param name="usuarioAtual">Identidade da requisição.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/arquivos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ArquivoController(IArquivoService arquivoService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Nome da rota de metadados, usado para montar o cabeçalho <c>Location</c>.</summary>
    public const string RotaDeDetalhe = "ArquivoPorId";

    private SolicitanteDeArquivo Solicitante => new(usuarioAtual.Id, usuarioAtual.EhAdministrador);

    /// <summary>Envia um arquivo.</summary>
    /// <param name="arquivo">Conteúdo enviado como <c>multipart/form-data</c>.</param>
    /// <param name="categoria">Agrupamento lógico — por exemplo <c>avatares</c> ou <c>anexos-pedido</c>.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [RegistrarEvento("arquivo.enviado")]
    [HttpPost]
    [ProducesResponseType(typeof(ArquivoResumoDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Enviar(IFormFile arquivo, [FromForm] string categoria, CancellationToken ct)
    {
        if (arquivo is null || arquivo.Length == 0)
            return Responder(Result.Falha<ArquivoResumoDTO>(Erro.Validacao("arquivo.vazio", "Nenhum arquivo foi enviado.", "arquivo")));

        await using var conteudo = arquivo.OpenReadStream();

        var resultado = await arquivoService.Enviar(
            new NovoArquivo(arquivo.FileName, arquivo.Length, conteudo, categoria ?? string.Empty),
            usuarioAtual.Id,
            ct
        );

        var resposta = resultado.Map(a => a.Adapt<ArquivoResumoDTO>());

        return Criado(resposta, RotaDeDetalhe, new { id = resultado.Sucesso ? resultado.Valor.Id : Guid.Empty });
    }

    /// <summary>Obtém os metadados de um arquivo.</summary>
    /// <param name="id">Identificador do arquivo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("{id:guid}", Name = RotaDeDetalhe)]
    [ProducesResponseType(typeof(ArquivoResumoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken ct)
    {
        var resultado = await arquivoService.ObterPorId(id, Solicitante, ct);

        return Responder(resultado.Map(a => a.Adapt<ArquivoResumoDTO>()));
    }

    /// <summary>Lista arquivos paginados.</summary>
    /// <param name="paginacao">Página e tamanho.</param>
    /// <param name="categoria">Filtro por categoria.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PaginaDTO<ArquivoResumoDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] PaginacaoRequestDTO paginacao, [FromQuery] string? categoria, CancellationToken ct)
    {
        var resultado = await arquivoService.Listar(paginacao.ParaModelo(), categoria, Solicitante, ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(a => a.Adapt<ArquivoResumoDTO>())));
    }

    /// <summary>Baixa o conteúdo de um arquivo.</summary>
    /// <remarks>
    /// Não usa os helpers do <c>MainController</c> porque a resposta de sucesso não é JSON. O
    /// fluxo é entregue ao ASP.NET, que o transmite e o descarta ao final da resposta.
    /// </remarks>
    /// <param name="id">Identificador do arquivo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("{id:guid}/conteudo")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Baixar(Guid id, CancellationToken ct)
    {
        var resultado = await arquivoService.Baixar(id, Solicitante, ct);

        if (resultado.Falhou)
            return Responder(resultado.Map(_ => 0));

        var arquivo = resultado.Valor;

        return File(arquivo.Conteudo, arquivo.ContentType, arquivo.Nome);
    }

    /// <summary>Remove um arquivo.</summary>
    /// <param name="id">Identificador do arquivo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [RegistrarEvento("arquivo.removido", CamposDaRota = ["id"])]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remover(Guid id, CancellationToken ct) => Responder(await arquivoService.Remover(id, Solicitante, ct));
}
