using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Cobrancas;

/// <summary>
/// O desconto ou a isenção individual (Sprint 54): de quem já está na turma, pelo usuário, ou de quem ainda vai entrar
/// pelo convite pessoal, pelo e-mail.
/// </summary>
/// <remarks>
/// Só autoriza: o desconto acontece quando a pessoa escolhe o pacote e aceita. Ler é da Gestão; conceder, alterar e
/// revogar, da Tesouraria — inclusive a própria condição, com auditoria (P9).
/// </remarks>
/// <param name="condicoes">As condições da turma.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Cobrancas)]
[Route("api/v{version:apiVersion}/cobrancas/condicoes")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class CondicaoFinanceiraController(ICondicaoFinanceiraService condicoes, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>A condição de um membro da turma.</summary>
    /// <param name="usuarioId">Membro.</param>
    [HttpGet("membros/{usuarioId:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(CondicaoDaPessoaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterDoMembro(Guid usuarioId, CancellationToken ct) =>
        Responder((await condicoes.Obter(FormaturaId, new PessoaDaCondicao(usuarioId, null), ct)).Map(c => c.Adapt<CondicaoDaPessoaDTO>()));

    /// <summary>Concede ou altera a condição de um membro. O que ele já aceitou não muda.</summary>
    /// <remarks>
    /// 400 com <c>cobranca.condicao_fora_do_catalogo</c>, <c>cobranca.condicao_grupo_repetido</c> ou erro de forma; sem
    /// plano vigente, 409 <c>cobranca.sem_plano_vigente</c>.
    /// </remarks>
    /// <param name="usuarioId">Membro.</param>
    /// <param name="requisicao">Os benefícios.</param>
    [HttpPut("membros/{usuarioId:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(CondicaoDaPessoaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SalvarDoMembro(Guid usuarioId, [FromBody] CondicaoFinanceiraRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (await condicoes.Salvar(FormaturaId, new PessoaDaCondicao(usuarioId, null), Dados(requisicao), usuarioAtual.Id, ct)).Map(c =>
                c.Adapt<CondicaoDaPessoaDTO>()
            )
        );

    /// <summary>Revoga a condição de um membro. O que ele já aceitou continua valendo.</summary>
    /// <param name="usuarioId">Membro.</param>
    [HttpDelete("membros/{usuarioId:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoverDoMembro(Guid usuarioId, CancellationToken ct) =>
        Responder(await condicoes.Remover(FormaturaId, new PessoaDaCondicao(usuarioId, null), usuarioAtual.Id, ct));

    /// <summary>A condição preparada para o e-mail de um convite pessoal.</summary>
    /// <param name="email">E-mail do convite.</param>
    [HttpGet("convidados")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(CondicaoDaPessoaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ObterDoConvidado([FromQuery] string? email, CancellationToken ct) =>
        Responder((await condicoes.Obter(FormaturaId, new PessoaDaCondicao(null, email), ct)).Map(c => c.Adapt<CondicaoDaPessoaDTO>()));

    /// <summary>Prepara ou altera a condição de quem ainda vai entrar pelo convite pessoal.</summary>
    /// <remarks>Ao aceitar o convite com a conta desse e-mail, a condição passa a ser do vínculo da pessoa.</remarks>
    /// <param name="email">E-mail do convite.</param>
    /// <param name="requisicao">Os benefícios.</param>
    [HttpPut("convidados")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(CondicaoDaPessoaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SalvarDoConvidado(
        [FromQuery] string? email,
        [FromBody] CondicaoFinanceiraRequestDTO requisicao,
        CancellationToken ct
    ) =>
        Responder(
            (await condicoes.Salvar(FormaturaId, new PessoaDaCondicao(null, email), Dados(requisicao), usuarioAtual.Id, ct)).Map(c =>
                c.Adapt<CondicaoDaPessoaDTO>()
            )
        );

    /// <summary>Revoga a condição preparada no convite.</summary>
    /// <param name="email">E-mail do convite.</param>
    [HttpDelete("convidados")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RemoverDoConvidado([FromQuery] string? email, CancellationToken ct) =>
        Responder(await condicoes.Remover(FormaturaId, new PessoaDaCondicao(null, email), usuarioAtual.Id, ct));

    /// <summary>O corpo da tela, como o service o recebe.</summary>
    /// <param name="requisicao">Corpo.</param>
    private static DadosDaCondicao Dados(CondicaoFinanceiraRequestDTO requisicao) =>
        new([.. (requisicao.Beneficios ?? []).Select(b => new BeneficioDaCondicao(b.ItemDeCobrancaId, b.Percentual, b.ValorEmCentavos))]);
}
