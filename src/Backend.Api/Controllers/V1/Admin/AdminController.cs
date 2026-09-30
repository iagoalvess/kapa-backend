using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Admin;
using Backend.Api.DTOs.Comum;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Admin.Models;
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
    /// <summary>Como a plataforma está no período: contas, turmas, dinheiro do Kapa e das turmas, e uso (Sprint 44).</summary>
    /// <remarks>Sem período, os últimos 30 dias até hoje. Até um ano.</remarks>
    /// <param name="de">Primeiro dia (<c>aaaa-mm-dd</c>), no fuso de exibição.</param>
    /// <param name="ate">Último dia, inclusive.</param>
    [HttpGet("analytics")]
    [ProducesResponseType(typeof(AnalyticsDaPlataformaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterAnalytics([FromQuery] DateOnly? de, [FromQuery] DateOnly? ate, CancellationToken ct) =>
        Responder((await adminService.ObterAnalytics(de, ate, ct)).Map(analytics => analytics.Adapt<AnalyticsDaPlataformaDTO>()));

    /// <summary>Cadastros, turmas novas e recebido do Kapa, mês a mês, terminando no mês corrente.</summary>
    /// <param name="meses">Quantos meses, de 1 a 24.</param>
    [HttpGet("analytics/serie")]
    [ProducesResponseType(typeof(IReadOnlyList<MesDaPlataformaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterSerieMensal([FromQuery] int meses = 12, CancellationToken ct = default) =>
        Responder((await adminService.ObterSerieMensal(meses, ct)).Map(serie => serie.Adapt<IReadOnlyList<MesDaPlataformaDTO>>()));

    /// <summary>As turmas, paginadas: nome, instituição ou curso pelo termo, e a licença de cada uma.</summary>
    /// <param name="paginacao">Página, tamanho e ordenação — <c>nome</c> ou <c>criada_em</c>; o padrão é a mais nova.</param>
    /// <param name="termo">Trecho do nome, da instituição ou do curso.</param>
    /// <param name="licenca"><c>Gratuito</c>, o nome de um plano pago, ou <c>Suspensa</c>/<c>Encerrada</c>/<c>Descartada</c>.</param>
    [HttpGet("suporte/turmas")]
    [ProducesResponseType(typeof(PaginaDTO<TurmaNoPainelDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarTurmas(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] string? termo,
        [FromQuery] string? licenca,
        CancellationToken ct
    ) =>
        Responder(
            (await adminService.ListarTurmas(paginacao.ParaModelo(), new FiltroDeTurmasNoPainel(termo, licenca), ct)).Map(pagina =>
                pagina.ParaDTO(turma => turma.Adapt<TurmaNoPainelDTO>())
            )
        );

    /// <summary>As contas, paginadas: nome ou e-mail pelo termo, e em quantas turmas cada uma está.</summary>
    /// <param name="paginacao">Página, tamanho e ordenação — <c>nome</c>, <c>email</c> ou <c>criado_em</c>; o padrão é o nome.</param>
    /// <param name="termo">Trecho do nome ou do e-mail.</param>
    /// <param name="situacao"><c>Confirmada</c>, <c>Bloqueada</c> ou <c>Desativada</c>.</param>
    [HttpGet("suporte/contas")]
    [ProducesResponseType(typeof(PaginaDTO<ContaNoPainelDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarContas(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] string? termo,
        [FromQuery] SituacaoDaConta? situacao,
        CancellationToken ct
    ) =>
        Responder(
            (await adminService.ListarContas(paginacao.ParaModelo(), new FiltroDeContasNoPainel(termo, situacao), ct)).Map(pagina =>
                pagina.ParaDTO(conta => conta.Adapt<ContaNoPainelDTO>())
            )
        );

    /// <summary>A turma: situação, licença, pagamentos do plano e as contagens de membro, parcela e adesão.</summary>
    /// <param name="id">Formatura.</param>
    [HttpGet("suporte/formaturas/{id:guid}")]
    [ProducesResponseType(typeof(TurmaNoSuporteDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterTurma(Guid id, CancellationToken ct) =>
        Responder((await adminService.ObterTurma(id, ct)).Map(turma => turma.Adapt<TurmaNoSuporteDTO>()));

    /// <summary>Os membros da turma, paginados, ativos primeiro. O CPF sai mascarado.</summary>
    /// <param name="id">Formatura.</param>
    /// <param name="paginacao">Página e tamanho.</param>
    [HttpGet("suporte/formaturas/{id:guid}/membros")]
    [ProducesResponseType(typeof(PaginaDTO<MembroNoSuporteDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarMembros(Guid id, [FromQuery] PaginacaoRequestDTO paginacao, CancellationToken ct) =>
        Responder(
            (await adminService.ListarMembros(id, paginacao.ParaModelo(), ct)).Map(pagina =>
                pagina.ParaDTO(membro => membro.Adapt<MembroNoSuporteDTO>())
            )
        );

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

    /// <summary>
    /// Estorna um pagamento do plano e encerra a assinatura na hora: a renovação é cancelada no provedor e a turma
    /// fica só para consulta (Sprint 37, P7).
    /// </summary>
    /// <remarks>
    /// <c>Integral</c> só até 7 dias do pagamento (desistência); <c>Proporcional</c> devolve o que falta do ciclo — os
    /// casos das seções 13 e 14 dos Termos. Grava evento de auditoria com a formatura.
    /// </remarks>
    /// <param name="id">Formatura.</param>
    /// <param name="cobrancaId">Pagamento.</param>
    /// <param name="requisicao">Modo do estorno.</param>
    [HttpPost("suporte/formaturas/{id:guid}/pagamentos/{cobrancaId:guid}/estornar")]
    [ProducesResponseType(typeof(TurmaNoSuporteDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Estornar(Guid id, Guid cobrancaId, [FromBody] EstornarPagamentoRequestDTO requisicao, CancellationToken ct) =>
        Responder((await adminService.Estornar(id, cobrancaId, requisicao.Modo, usuarioAtual.Id, ct)).Map(turma => turma.Adapt<TurmaNoSuporteDTO>()));

    /// <summary>A planilha (.xlsx) dos pagamentos do plano confirmados no mês — a base da nota fiscal manual (P6).</summary>
    /// <remarks>Traz o CPF inteiro do Presidente, o tomador: é o que o portal da prefeitura pede.</remarks>
    /// <param name="ano">Ano.</param>
    /// <param name="mes">Mês, de 1 a 12.</param>
    [HttpGet("suporte/pagamentos")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportarPagamentos([FromQuery] int ano, [FromQuery] int mes, CancellationToken ct) =>
        Arquivo(await adminService.ExportarPagamentos(ano, mes, ct));

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
