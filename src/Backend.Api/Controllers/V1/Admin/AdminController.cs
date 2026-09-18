using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Admin;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Usuarios.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Admin;

/// <summary>
/// Painel administrativo.
/// </summary>
/// <remarks>
/// Tudo aqui exige <see cref="Politicas.SomenteAdministrador"/>, declarado na classe: um
/// endpoint novo neste controller já nasce restrito, sem depender de alguém lembrar do atributo.
/// <para>
/// A gestão de usuários — listar, editar, ativar, trocar perfil — fica em
/// <c>/api/v1/usuarios</c> e não é duplicada aqui; o front do painel consome os dois.
/// </para>
/// </remarks>
/// <param name="adminService">Operações do painel.</param>
/// <param name="usuarioAtual">Quem do suporte está executando — é o autor de todo evento daqui.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
[Authorize(Policy = Politicas.SomenteAdministrador)]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class AdminController(IAdminService adminService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Números que alimentam a tela inicial do painel.</summary>
    [HttpGet("resumo")]
    [ProducesResponseType(typeof(ResumoAdminDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterResumo(CancellationToken ct)
    {
        var resultado = await adminService.ObterResumo(ct);

        return Responder(resultado.Map(resumo => resumo.Adapt<ResumoAdminDTO>()));
    }

    /// <summary>
    /// Lista os perfis de acesso que o sistema reconhece.
    /// </summary>
    /// <remarks>
    /// O front usa isto para montar o seletor de perfis. Sem o endpoint, a lista acaba
    /// duplicada em código no front e sai de sincronia na primeira vez que um perfil é criado.
    /// </remarks>
    [HttpGet("perfis")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public IActionResult ListarPerfis() => Responder(Result.Ok(PerfisPadrao.Todos));

    /// <summary>Procura turma ou conta pelo mesmo termo: nome da turma, instituição, curso, nome ou e-mail.</summary>
    /// <remarks>
    /// Uma caixa só. Quem atende recebe "paguei e a turma não ativou" com o nome da turma <b>ou</b>
    /// o e-mail da pessoa na mão — duas caixas separadas obrigariam a adivinhar antes de procurar.
    /// Menos de três letras devolve as duas listas vazias.
    /// </remarks>
    /// <param name="termo">Trecho digitado.</param>
    [HttpGet("suporte/busca")]
    [ProducesResponseType(typeof(ResultadoDaBuscaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Buscar([FromQuery] string? termo, CancellationToken ct) =>
        Responder((await adminService.Buscar(termo, ct)).Map(resultado => resultado.Adapt<ResultadoDaBuscaDTO>()));

    /// <summary>A turma: situação, licença, membros e as contagens de parcela e adesão. O CPF sai mascarado.</summary>
    /// <param name="id">Formatura.</param>
    [HttpGet("suporte/formaturas/{id:guid}")]
    [ProducesResponseType(typeof(TurmaNoSuporteDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterTurma(Guid id, CancellationToken ct) =>
        Responder((await adminService.ObterTurma(id, ct)).Map(turma => turma.Adapt<TurmaNoSuporteDTO>()));

    /// <summary>
    /// Ativa a licença da turma à mão — o caso em que o pagamento entrou e o webhook se perdeu.
    /// </summary>
    /// <remarks>
    /// Grava evento de auditoria com o autor e com a formatura, e é por isso que a comissão vê na
    /// trilha dela que quem ativou foi o suporte. Turma já ativa responde 200 sem gravar nada.
    /// </remarks>
    /// <param name="id">Formatura.</param>
    [HttpPost("suporte/formaturas/{id:guid}/ativar-assinatura")]
    [ProducesResponseType(typeof(TurmaNoSuporteDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AtivarAssinatura(Guid id, CancellationToken ct) =>
        Responder((await adminService.AtivarAssinatura(id, usuarioAtual.Id, ct)).Map(turma => turma.Adapt<TurmaNoSuporteDTO>()));

    /// <summary>A conta: acesso, bloqueio por tentativas e em que turmas a pessoa está.</summary>
    /// <param name="id">Conta.</param>
    [HttpGet("suporte/usuarios/{id:guid}")]
    [ProducesResponseType(typeof(UsuarioNoSuporteDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterUsuario(Guid id, CancellationToken ct) =>
        Responder((await adminService.ObterUsuario(id, ct)).Map(conta => conta.Adapt<UsuarioNoSuporteDTO>()));

    /// <summary>Reenvia o e-mail de confirmação da conta.</summary>
    /// <param name="id">Conta.</param>
    [HttpPost("suporte/usuarios/{id:guid}/reenviar-confirmacao")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReenviarConfirmacao(Guid id, CancellationToken ct) =>
        Responder(await adminService.ReenviarConfirmacao(id, usuarioAtual.Id, ct));

    /// <summary>
    /// Envia o e-mail de redefinição de senha ao dono da conta.
    /// </summary>
    /// <remarks>
    /// O suporte <b>não</b> define senha para ninguém: senha escolhida pelo atendente é senha que
    /// duas pessoas conhecem, e o log de acesso deixa de dizer quem entrou.
    /// </remarks>
    /// <param name="id">Conta.</param>
    [HttpPost("suporte/usuarios/{id:guid}/redefinir-senha")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RedefinirSenha(Guid id, CancellationToken ct) =>
        Responder(await adminService.DispararRedefinicaoDeSenha(id, usuarioAtual.Id, ct));

    /// <summary>Levanta o bloqueio por tentativas de senha. Não reativa conta desativada.</summary>
    /// <param name="id">Conta.</param>
    [HttpPost("suporte/usuarios/{id:guid}/desbloquear")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Desbloquear(Guid id, CancellationToken ct) => Responder(await adminService.Desbloquear(id, usuarioAtual.Id, ct));
}
