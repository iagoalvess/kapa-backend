using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Comunicacao;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Comunicacao.Services;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Comunicacao;

/// <summary>
/// O acervo da turma: atas, contratos, orçamentos, regulamentos.
/// </summary>
/// <remarks>
/// Mesma regra do mural: todo membro lista e baixa, dentro do que o papel pode ver; enviar, corrigir,
/// substituir e excluir são da Gestão, com a turma ativa. O download nunca devolve os bytes nem a
/// URL do provedor direto: confere formatura e visibilidade e redireciona (302) para uma URL assinada
/// de minutos.
/// </remarks>
/// <param name="documentoService">Regras do acervo.</param>
/// <param name="usuarioAtual">Quem chama — dono do arquivo enviado.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Mural)]
[Route("api/v{version:apiVersion}/comunicacao/documentos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class DocumentoController(IDocumentoService documentoService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>O acervo, por categoria, visibilidade e título — ou <c>ordenarPor=enviadoEm</c>, do mais recente com <c>descendente=true</c>.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="filtro">Categoria, visibilidade e busca pelo título.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(PaginaDTO<DocumentoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar([FromQuery] PaginacaoRequestDTO paginacao, [FromQuery] FiltroDeDocumentos filtro, CancellationToken ct)
    {
        var resultado = await documentoService.Listar(FormaturaId, usuarioAtual.Id, paginacao.ParaModelo(), filtro, ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(documento => documento.Adapt<DocumentoDTO>())));
    }

    /// <summary>O acervo em números: quantos, quanto ocupa, o último envio e quantos por categoria.</summary>
    [HttpGet("resumo")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(ResumoDoAcervoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resumir(CancellationToken ct) =>
        Responder((await documentoService.Resumir(FormaturaId, usuarioAtual.Id, ct)).Map(resumo => resumo.Adapt<ResumoDoAcervoDTO>()));

    /// <summary>Envia um documento novo.</summary>
    /// <remarks>
    /// Multipart. O tipo é conferido pelos primeiros bytes; o teto é de
    /// <see cref="Documento.TamanhoMaximoEmMB"/> MB.
    /// </remarks>
    /// <param name="requisicao">Título, categoria e visibilidade.</param>
    /// <param name="arquivo">PDF, imagem, Word ou Excel; obrigatório.</param>
    [HttpPost]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [Consumes("multipart/form-data")]
    [RegistrarEvento("comunicacao.documento_enviado")]
    [ProducesResponseType(typeof(DocumentoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Enviar([FromForm] DocumentoRequestDTO requisicao, IFormFile? arquivo, CancellationToken ct)
    {
        await using var conteudo = arquivo?.OpenReadStream() ?? Stream.Null;

        var resultado = await documentoService.Enviar(FormaturaId, usuarioAtual.Id, Dados(requisicao), Arquivo(arquivo, conteudo), ct);

        return Responder(resultado.Map(documento => documento.Adapt<DocumentoDTO>()));
    }

    /// <summary>Corrige título, categoria e visibilidade e, se vier arquivo, substitui o atual pela versão seguinte.</summary>
    /// <remarks>Multipart. A substituição fica na auditoria, com o arquivo que saiu e o que entrou.</remarks>
    /// <param name="id">Documento.</param>
    /// <param name="requisicao">Dados novos.</param>
    /// <param name="arquivo">Arquivo novo; ausente, o atual fica.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [Consumes("multipart/form-data")]
    [RegistrarEvento("comunicacao.documento_alterado", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(DocumentoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Atualizar(Guid id, [FromForm] DocumentoRequestDTO requisicao, IFormFile? arquivo, CancellationToken ct)
    {
        await using var conteudo = arquivo?.OpenReadStream() ?? Stream.Null;

        var resultado = await documentoService.Atualizar(FormaturaId, usuarioAtual.Id, id, Dados(requisicao), Arquivo(arquivo, conteudo), ct);

        return Responder(resultado.Map(documento => documento.Adapt<DocumentoDTO>()));
    }

    /// <summary>Exclui um documento e o arquivo dele. Quem excluiu fica na auditoria.</summary>
    /// <param name="id">Documento.</param>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct) =>
        Responder(await documentoService.Excluir(FormaturaId, usuarioAtual.Id, id, ct));

    /// <summary>
    /// Redireciona para uma URL assinada do arquivo, válida por minutos.
    /// </summary>
    /// <remarks>
    /// Não usa os helpers do <c>MainController</c>: o sucesso é um 302. Antes dele, a formatura (filtro
    /// global) e a visibilidade (repositório) já foram conferidas — documento interno pedido por formando
    /// responde 404, como o que não existe.
    /// </remarks>
    /// <param name="id">Documento.</param>
    [HttpGet("{id:guid}/download")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Baixar(Guid id, CancellationToken ct)
    {
        var resultado = await documentoService.Baixar(FormaturaId, usuarioAtual.Id, id, ct);

        return resultado.Falhou ? Responder(resultado) : Redirect(resultado.Valor);
    }

    private static DadosDoDocumento Dados(DocumentoRequestDTO requisicao) =>
        new(requisicao.Titulo ?? string.Empty, requisicao.Categoria, requisicao.Visibilidade);

    /// <summary>O arquivo do multipart como pedido de envio; ausente ou vazio, nenhum.</summary>
    private static NovoArquivo? Arquivo(IFormFile? arquivo, Stream conteudo) =>
        arquivo is { Length: > 0 } ? new NovoArquivo(arquivo.FileName, arquivo.Length, conteudo, DocumentoService.CategoriaDoArquivo) : null;
}
