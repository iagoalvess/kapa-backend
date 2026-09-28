using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Festa;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Agenda.Models;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Festa;

/// <summary>
/// O convite da festa: o do formando, a página pública e o que a Gestão emite à mão.
/// </summary>
/// <remarks>
/// A página e o PDF são <b>públicos</b> (decisão 11) e ficam sob o balde de
/// <see cref="RateLimitConfig.Ingresso"/>: código curto sem limite é código enumerável. O resto exige
/// sessão. "Convite da festa" em tela, <c>Festa/ConviteDoEvento</c> no código — o <c>/convites</c> da
/// raiz é o convite de entrada na turma (decisão 2).
/// </remarks>
/// <param name="convites">Regras do convite.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/festa/convites")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ConviteDaFestaController(IConviteDoEventoService convites, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Os convites do próprio formando para a festa ou para a colação.</summary>
    /// <param name="tipo">Festa (o padrão) ou colação.</param>
    [HttpGet("meus")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(MeusConvitesDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarMeus(CancellationToken ct, [FromQuery] TipoDeEvento tipo = TipoDeEvento.Festa) =>
        Responder((await convites.ListarMeus(FormaturaId, usuarioAtual.Id, tipo, ct)).Map(meus => meus.Adapt<MeusConvitesDTO>()));

    /// <summary>A situação dos convites da festa: emitidos, pendentes e pedidos esperando.</summary>
    [HttpGet("resumo")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(ResumoDosConvitesDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resumir(CancellationToken ct) =>
        Responder((await convites.Resumir(ct)).Map(resumo => resumo.Adapt<ResumoDosConvitesDTO>()));

    /// <summary>A página do convite, sem sessão.</summary>
    /// <remarks>
    /// Não devolve nome de formando, valor nem cadastro de ninguém. Assinatura errada, código
    /// inexistente e convite revogado respondem o mesmo 404 <c>festa.convite_nao_encontrado</c>.
    /// </remarks>
    /// <param name="token">Código com a assinatura, como está no QR.</param>
    [HttpGet("{token}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitConfig.Ingresso)]
    [ProducesResponseType(typeof(ConvitePublicoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> AbrirPublico(string token, CancellationToken ct) =>
        Responder((await convites.AbrirPublico(token, ct)).Map(convite => convite.Adapt<ConvitePublicoDTO>()));

    /// <summary>O convite em PDF, gerado na hora.</summary>
    /// <param name="token">Código com a assinatura.</param>
    [HttpGet("{token}/pdf")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitConfig.Ingresso)]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> BaixarPdf(string token, CancellationToken ct) => Arquivo(await convites.GerarPdf(token, ct));

    /// <summary>Nomeia o convidado — ou troca, que é como se transfere um convite.</summary>
    /// <remarks>
    /// O dono até 24 h antes da festa; depois, 409 <c>festa.lista_fechada</c> e só a Gestão. Trocar o
    /// titular de um convite já nomeado devolve o convite com código novo — o anterior fica revogado.
    /// </remarks>
    /// <param name="id">Convite.</param>
    /// <param name="requisicao">Nome, documento e e-mail.</param>
    [HttpPut("{id:guid}/convidado")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(MeuConviteDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> NomearConvidado(Guid id, [FromBody] ConvidadoRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (await convites.NomearConvidado(id, FormaturaId, usuarioAtual.Id, ParaModelo(requisicao), ct)).Map(convite =>
                convite.Adapt<MeuConviteDTO>()
            )
        );

    /// <summary>Revoga o código e emite outro para o mesmo convidado.</summary>
    /// <param name="id">Convite.</param>
    [HttpPost("{id:guid}/reemitir")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(ConviteNaPortariaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reemitir(Guid id, CancellationToken ct) =>
        Responder((await convites.Reemitir(id, usuarioAtual.Id, ct)).Map(convite => convite.Adapt<ConviteNaPortariaDTO>()));

    /// <summary>Emite os convites de um pedido ainda em aberto — "paga o resto na porta" (P2).</summary>
    /// <param name="requisicao">Pedido e motivo.</param>
    [HttpPost("liberar")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(EmissaoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Liberar([FromBody] LiberacaoRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (await convites.Liberar(usuarioAtual.Id, new LiberacaoDeConvites(requisicao.PedidoId, requisicao.Motivo ?? string.Empty), ct)).Map(
                quantidade => new EmissaoDTO(quantidade)
            )
        );

    /// <summary>Emite um convite da turma, sem dono — o paraninfo, o patrocinador (decisão 14).</summary>
    /// <remarks>Consome o estoque do item de convite extra, se a festa vende um: sem vaga, 409 <c>cobranca.estoque_esgotado</c>.</remarks>
    /// <param name="requisicao">Convidado e motivo.</param>
    [HttpPost("cortesias")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(ConviteNaPortariaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EmitirCortesia([FromBody] CortesiaRequestDTO requisicao, CancellationToken ct)
    {
        var dados = new DadosDaCortesia(
            new DadosDoConvidado(requisicao.Nome ?? string.Empty, requisicao.TipoDoDocumento, requisicao.NumeroDoDocumento, requisicao.Email),
            requisicao.Motivo ?? string.Empty,
            requisicao.EventoId
        );

        return Responder((await convites.EmitirCortesia(usuarioAtual.Id, dados, ct)).Map(convite => convite.Adapt<ConviteNaPortariaDTO>()));
    }

    /// <summary>Emite os convites dos pedidos já quitados que ainda não os têm — depois de a festa ficar completa na agenda.</summary>
    [HttpPost("emitir-pendentes")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(EmissaoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EmitirPendentes(CancellationToken ct) =>
        Responder((await convites.EmitirPendentes(ct)).Map(pedidos => new EmissaoDTO(pedidos)));

    private static DadosDoConvidado ParaModelo(ConvidadoRequestDTO requisicao) =>
        new(requisicao.Nome ?? string.Empty, requisicao.TipoDoDocumento, requisicao.NumeroDoDocumento, requisicao.Email);
}
