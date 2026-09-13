using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Formandos;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Formandos;

/// <summary>
/// Cadastro do formando: o próprio preenche em <c>/eu</c>, a comissão consulta e corrige.
/// </summary>
/// <remarks>
/// <c>/eu</c> em vez de <c>/{meuId}</c>: o id vem do token. Endpoint que aceita o próprio id no
/// caminho é endpoint onde alguém troca o id e testa a sorte.
/// <para>
/// A formatura vem da claim, e o formando de outra turma não existe aqui — responde 404, como
/// em membros.
/// </para>
/// </remarks>
/// <param name="perfilService">Regras do cadastro.</param>
/// <param name="formaturaAtual">Formatura da sessão.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/formandos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class FormandoController(IPerfilService perfilService, IFormaturaAtual formaturaAtual, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>A política garante a claim; o <c>Guid.Empty</c> nunca chega a ser consultado.</summary>
    private Guid FormaturaId => formaturaAtual.Id ?? Guid.Empty;

    /// <summary>O próprio cadastro, vazio se ainda não foi preenchido.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("eu")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(PerfilDoFormandoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterMeu(CancellationToken ct) =>
        Responder((await perfilService.Obter(FormaturaId, usuarioAtual.Id, ct)).Map(perfil => perfil.Adapt<PerfilDoFormandoDTO>()));

    /// <summary>Altera o próprio cadastro, seção por seção.</summary>
    /// <param name="requisicao">Seções a gravar; a ausente fica como está.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPut("eu")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAberta)]
    [RegistrarEvento("perfil.atualizado")]
    [ProducesResponseType(typeof(PerfilDoFormandoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AtualizarMeu([FromBody] AtualizarPerfilRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await perfilService.Atualizar(FormaturaId, usuarioAtual.Id, requisicao.Adapt<AtualizarPerfil>(), ct);

        return Responder(resultado.Map(perfil => perfil.Adapt<PerfilDoFormandoDTO>()));
    }

    /// <summary>Troca a própria foto. JPEG, PNG ou WebP de até 5 MB; sai em até 512×512.</summary>
    /// <param name="foto">Imagem enviada como <c>multipart/form-data</c>.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("eu/foto")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAberta)]
    [RegistrarEvento("perfil.foto_enviada")]
    [ProducesResponseType(typeof(PerfilDoFormandoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> EnviarFoto(IFormFile? foto, CancellationToken ct)
    {
        await using var conteudo = foto?.OpenReadStream() ?? Stream.Null;

        var resultado = await perfilService.EnviarFoto(FormaturaId, usuarioAtual.Id, conteudo, foto?.Length ?? 0, ct);

        return Responder(resultado.Map(perfil => perfil.Adapt<PerfilDoFormandoDTO>()));
    }

    /// <summary>Os membros ativos, paginados, com a completude do cadastro de cada um.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="busca">Trecho do nome de exibição, do nome civil ou do e-mail.</param>
    /// <param name="situacao"><c>Pendente</c> (falta o essencial), <c>Incompleto</c> ou <c>Completo</c>; ausente traz todos.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(PaginaDTO<FormandoResumoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] string? busca,
        [FromQuery] SituacaoDoCadastro? situacao,
        CancellationToken ct
    )
    {
        var resultado = await perfilService.Listar(FormaturaId, paginacao.ParaModelo(), new FiltroDeFormandos(busca, situacao), ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(formando => formando.Adapt<FormandoResumoDTO>())));
    }

    /// <summary>O cadastro de um formando da turma.</summary>
    /// <param name="usuarioId">Formando.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("{usuarioId:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(PerfilDoFormandoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(Guid usuarioId, CancellationToken ct) =>
        Responder((await perfilService.Obter(FormaturaId, usuarioId, ct)).Map(perfil => perfil.Adapt<PerfilDoFormandoDTO>()));

    /// <summary>A foto de um formando da turma.</summary>
    /// <remarks>
    /// Não usa os helpers do <c>MainController</c> porque a resposta de sucesso é a imagem, não
    /// JSON. Sem nome de arquivo: vai <c>inline</c>, para exibir, e não como download.
    /// </remarks>
    /// <param name="usuarioId">Formando.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("{usuarioId:guid}/foto")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BaixarFoto(Guid usuarioId, CancellationToken ct)
    {
        var resultado = await perfilService.BaixarFoto(FormaturaId, usuarioId, ct);

        if (resultado.Falhou)
            return Responder(resultado.Map(_ => 0));

        return File(resultado.Valor.Conteudo, resultado.Valor.ContentType);
    }

    /// <summary>Correção do cadastro pela comissão, registrada com o autor.</summary>
    /// <param name="usuarioId">Formando corrigido.</param>
    /// <param name="requisicao">Seções a gravar; a ausente fica como está.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPut("{usuarioId:guid}")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("perfil.corrigido", CamposDaRota = ["usuarioId"])]
    [ProducesResponseType(typeof(PerfilDoFormandoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Corrigir(Guid usuarioId, [FromBody] AtualizarPerfilRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await perfilService.Corrigir(FormaturaId, usuarioId, usuarioAtual.Id, requisicao.Adapt<AtualizarPerfil>(), ct);

        return Responder(resultado.Map(perfil => perfil.Adapt<PerfilDoFormandoDTO>()));
    }
}
