using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Comunicacao;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Comunicacao.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Comunicacao;

/// <summary>
/// O mural da turma: a comissão publica, todo membro lê.
/// </summary>
/// <remarks>
/// Ler é de todo membro, mas <b>o que</b> cada um lê é decidido no repositório pelo papel gravado: o
/// formando não recebe aviso <c>SomenteComissao</c> nem pela lista, nem pelo id. Publicar, corrigir e
/// excluir são da Gestão, com a turma ativa; o formando recebe 403.
/// </remarks>
/// <param name="avisoService">Regras do mural.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/comunicacao/avisos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class AvisoController(IAvisoService avisoService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Nome da rota de um aviso, para o <c>Location</c> da publicação.</summary>
    public const string RotaDoAviso = "AvisoPorId";

    /// <summary>O mural: fixados primeiro, depois do mais novo para o mais antigo.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="filtro">Fixados, importantes, visibilidade e busca por título e texto.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(PaginaDTO<AvisoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar([FromQuery] PaginacaoRequestDTO paginacao, [FromQuery] FiltroDeAvisos filtro, CancellationToken ct)
    {
        var resultado = await avisoService.Listar(FormaturaId, usuarioAtual.Id, paginacao.ParaModelo(), filtro, ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(aviso => aviso.Adapt<AvisoDTO>())));
    }

    /// <summary>O mural em números: avisos, fixados, importantes, internos e a última publicação.</summary>
    /// <remarks>Cada número é o total do filtro, dentro do que o papel vê — é o que as pílulas da tela mostram.</remarks>
    [HttpGet("resumo")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(ResumoDoMuralDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resumir(CancellationToken ct) =>
        Responder((await avisoService.Resumir(FormaturaId, usuarioAtual.Id, ct)).Map(resumo => resumo.Adapt<ResumoDoMuralDTO>()));

    /// <summary>O que entrou no mural desde a última visita desta pessoa — o sino do cabeçalho.</summary>
    /// <remarks>
    /// Recorte de visibilidade igual ao da lista: o aviso interno não conta no sino de quem não o lê.
    /// </remarks>
    [HttpGet("novidades")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(NovidadesDoMuralDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Novidades(CancellationToken ct) =>
        Responder((await avisoService.Novidades(FormaturaId, usuarioAtual.Id, ct)).Map(novidades => novidades.Adapt<NovidadesDoMuralDTO>()));

    /// <summary>Marca o mural como visto agora: o sino zera para quem chamou.</summary>
    /// <remarks>Quem chama é a tela do mural ao abrir — ver o balão do sino não é ter lido os avisos.</remarks>
    [HttpPost("novidades/visto")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MarcarVisto(CancellationToken ct) => Responder(await avisoService.MarcarVisto(FormaturaId, usuarioAtual.Id, ct));

    /// <summary>Um aviso. O interno pedido por formando responde 404, como o que não existe.</summary>
    /// <param name="id">Aviso.</param>
    [HttpGet("{id:guid}", Name = RotaDoAviso)]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(AvisoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct) =>
        Responder((await avisoService.Obter(FormaturaId, usuarioAtual.Id, id, ct)).Map(aviso => aviso.Adapt<AvisoDTO>()));

    /// <summary>Publica um aviso. O quarto fixado devolve 409 <c>comunicacao.limite_de_fixados</c>.</summary>
    /// <param name="requisicao">Título, texto, visibilidade, fixado e destaque.</param>
    [HttpPost]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("comunicacao.aviso_publicado")]
    [ProducesResponseType(typeof(AvisoDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publicar([FromBody] AvisoRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await avisoService.Publicar(FormaturaId, usuarioAtual.Id, Dados(requisicao), ct);

        return Criado(resultado.Map(aviso => aviso.Adapt<AvisoDTO>()), RotaDoAviso, new { id = resultado.Sucesso ? resultado.Valor.Id : Guid.Empty });
    }

    /// <summary>Corrige um aviso. Autor e data de publicação ficam.</summary>
    /// <param name="id">Aviso.</param>
    /// <param name="requisicao">Dados novos.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("comunicacao.aviso_alterado", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(AvisoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AvisoRequestDTO requisicao, CancellationToken ct) =>
        Responder((await avisoService.Atualizar(FormaturaId, usuarioAtual.Id, id, Dados(requisicao), ct)).Map(aviso => aviso.Adapt<AvisoDTO>()));

    /// <summary>Exclui um aviso. Quem excluiu fica na auditoria, na mesma transação.</summary>
    /// <param name="id">Aviso.</param>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct) =>
        Responder(await avisoService.Excluir(FormaturaId, usuarioAtual.Id, id, ct));

    private static DadosDoAviso Dados(AvisoRequestDTO requisicao) =>
        new(requisicao.Titulo ?? string.Empty, requisicao.Conteudo ?? string.Empty, requisicao.Visibilidade, requisicao.Fixado, requisicao.Destaque);
}
