using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Privacidade;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Legal.Interfaces;
using Backend.Business.Marketing.Interfaces;
using Backend.Business.Marketing.Models;
using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Privacidade.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Privacidade;

/// <summary>
/// O portal do titular: ver o que a Kapa guarda, exportar, revogar consentimento e pedir eliminação.
/// </summary>
/// <remarks>
/// Tudo aqui é <c>Autenticado</c> e nada aqui é da formatura selecionada — nem a política, nem o
/// recorte. Titular é pessoa, e o portal alcança a conta e todos os vínculos dela (decisão 2 da
/// Sprint 14). A exceção é <c>operadores</c>, que é anônimo de propósito: quem ainda está decidindo
/// se cria conta tem direito de saber para onde o dado dele vai.
/// <para>
/// Nenhuma rota daqui aceita o identificador de outro titular. O que separa "meus dados" de "dados
/// de qualquer um" é o <c>usuarioAtual.Id</c>, que vem do token — nunca da rota.
/// </para>
/// </remarks>
/// <param name="privacidadeService">O portal.</param>
/// <param name="legalService">Consentimento, de onde sai a revogação.</param>
/// <param name="comunicacaoDoKapa">A preferência de marketing do Kapa e o descadastro de um clique.</param>
/// <param name="usuarioAtual">Quem está fazendo a requisição — é o titular, sempre.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/privacidade")]
[Authorize(Policy = Politicas.Autenticado)]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class PrivacidadeController(
    IPrivacidadeService privacidadeService,
    ILegalService legalService,
    IComunicacaoDoKapaService comunicacaoDoKapa,
    IUsuarioAtual usuarioAtual
) : MainController
{
    /// <summary>Tudo o que a Kapa guarda sobre você, por seção.</summary>
    /// <remarks>
    /// Direito de confirmação e acesso (LGPD, art. 18, I e II). Traz conta, cadastro de cada turma,
    /// financeiro, consentimentos e preferências de comunicação — o mesmo conteúdo que a exportação
    /// leva, para que a tela não possa mostrar menos do que o pacote.
    /// </remarks>
    [HttpGet("meus-dados")]
    [ProducesResponseType(typeof(MeusDadosDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MeusDados(CancellationToken ct) =>
        Responder((await privacidadeService.MeusDados(usuarioAtual.Id, ct)).Map(dados => dados.Adapt<MeusDadosDTO>()));

    /// <summary>
    /// Pede o pacote com tudo o que a Kapa guarda sobre você, em JSON e CSV.
    /// </summary>
    /// <remarks>
    /// Sempre assíncrono: volta na hora com a solicitação, e o worker gera. Um <c>GET</c> que
    /// juntasse conta, cadastro, parcelas, adesão e consentimentos de todas as turmas numa
    /// requisição seria a consulta mais cara da API e a mais fácil de disparar em série.
    /// <para>Pedido igual já pendente devolve o mesmo, e não um segundo pacote.</para>
    /// </remarks>
    [HttpPost("exportacao")]
    [RegistrarEvento("privacidade.exportacao_solicitada")]
    [ProducesResponseType(typeof(SolicitacaoDePrivacidadeDTO), StatusCodes.Status200OK)]
    public async Task<IActionResult> Exportar(CancellationToken ct) => await Solicitar(TipoDeSolicitacao.Exportacao, senha: null, ct);

    /// <summary>
    /// Abre uma solicitação — exportação ou eliminação.
    /// </summary>
    /// <remarks>
    /// A eliminação <b>anonimiza</b>, e não apaga tudo: o que identifica some, os lançamentos
    /// financeiros ficam. A tela explica isso antes de confirmar, e o e-mail repete. Prometer
    /// apagamento total e não cumprir é pior que explicar o limite.
    /// </remarks>
    /// <param name="requisicao">O que pedir. Ausente, exportação.</param>
    [HttpPost("solicitacoes")]
    [RegistrarEvento("privacidade.solicitacao_aberta")]
    [ProducesResponseType(typeof(SolicitacaoDePrivacidadeDTO), StatusCodes.Status200OK)]
    public async Task<IActionResult> Solicitar([FromBody] SolicitarPrivacidadeDTO? requisicao, CancellationToken ct) =>
        await Solicitar(requisicao?.Tipo ?? TipoDeSolicitacao.Exportacao, requisicao?.Senha, ct);

    /// <summary>As suas solicitações, da mais recente.</summary>
    [HttpGet("solicitacoes")]
    [ProducesResponseType(typeof(IReadOnlyList<SolicitacaoDePrivacidadeDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarSolicitacoes(CancellationToken ct) =>
        Responder(
            (await privacidadeService.ListarSolicitacoes(usuarioAtual.Id, ct)).Map(solicitacoes =>
                solicitacoes.Adapt<IReadOnlyList<SolicitacaoDePrivacidadeDTO>>()
            )
        );

    /// <summary>Confirma a eliminação e dispensa a espera dos quinze dias.</summary>
    /// <param name="id">Solicitação.</param>
    [HttpPost("solicitacoes/{id:guid}/confirmar")]
    [RegistrarEvento("privacidade.exclusao_confirmada", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(SolicitacaoDePrivacidadeDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirmar(Guid id, CancellationToken ct) =>
        Responder((await privacidadeService.Confirmar(id, usuarioAtual.Id, ct)).Map(s => s.Adapt<SolicitacaoDePrivacidadeDTO>()));

    /// <summary>
    /// Desiste de uma solicitação ainda pendente.
    /// </summary>
    /// <remarks>
    /// É o que torna os quinze dias uma janela de arrependimento, e não só um prazo legal: até o fim
    /// dela, um clique errado no botão de eliminação ainda tem conserto.
    /// </remarks>
    /// <param name="id">Solicitação.</param>
    [HttpPost("solicitacoes/{id:guid}/cancelar")]
    [RegistrarEvento("privacidade.solicitacao_cancelada", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(SolicitacaoDePrivacidadeDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct) =>
        Responder((await privacidadeService.Cancelar(id, usuarioAtual.Id, ct)).Map(s => s.Adapt<SolicitacaoDePrivacidadeDTO>()));

    /// <summary>
    /// Baixa o pacote de uma exportação concluída.
    /// </summary>
    /// <remarks>
    /// Solicitação de outro titular, ainda na fila ou já expirada respondem a mesma coisa: 404. O
    /// link do e-mail aponta para a tela, e não para cá — e-mail encaminhado por engano não carrega
    /// download nenhum junto.
    /// </remarks>
    /// <param name="id">Solicitação.</param>
    [HttpGet("solicitacoes/{id:guid}/arquivo")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Baixar(Guid id, CancellationToken ct)
    {
        var resultado = await privacidadeService.Baixar(id, usuarioAtual.Id, ct);

        return Arquivo(resultado);
    }

    /// <summary>
    /// Revoga um consentimento registrado (LGPD, art. 18, IX).
    /// </summary>
    /// <remarks>
    /// Grava uma linha nova; o aceite original fica intacto, porque o banco recusa alterá-lo. Revogar
    /// o que é obrigatório devolve aquela versão para as pendências, e o aplicativo pede o aceite de
    /// novo na entrada seguinte — a tela avisa disso antes.
    /// </remarks>
    /// <param name="id">Registro de aceite a revogar.</param>
    [HttpPost("consentimentos/{id:guid}/revogar")]
    [RegistrarEvento("privacidade.consentimento_revogado", CamposDaRota = ["id"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revogar(Guid id, CancellationToken ct) =>
        Responder(await legalService.Revogar(usuarioAtual.Id, id, usuarioAtual.Origem, ct));

    /// <summary>
    /// Com quem a Kapa compartilha dado pessoal, e para quê.
    /// </summary>
    /// <remarks>
    /// Anônimo. A lista precisa ser legível <b>antes</b> do cadastro: atrás do login ela só alcança
    /// quem já concordou, o que é o contrário do que o art. 18, VII existe para fazer.
    /// </remarks>
    [HttpGet("operadores")]
    [AllowAnonymous]
    [EnableCors(ApiConfig.Vitrine)]
    [ProducesResponseType(typeof(IReadOnlyList<OperadorDTO>), StatusCodes.Status200OK)]
    public IActionResult Operadores() => Ok(OperadoresDaKapa.Todos.Adapt<IReadOnlyList<OperadorDTO>>());

    /// <summary>
    /// Liga ou desliga "Receber novidades do Kapa" (Sprint 40).
    /// </summary>
    /// <remarks>
    /// Por conta, e não por turma. Grava uma linha no histórico só quando o valor muda — pedir o que já vale
    /// responde 204 e não registra nada.
    /// </remarks>
    /// <param name="requisicao">O valor novo.</param>
    [HttpPut("comunicacao-do-kapa")]
    [RegistrarEvento("privacidade.comunicacao_do_kapa_alterada")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DefinirComunicacaoDoKapa([FromBody] ComunicacaoDoKapaRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            await comunicacaoDoKapa.DefinirPreferencia(
                usuarioAtual.Id,
                requisicao.Receber,
                OrigemDoConsentimentoDeMarketing.MinhaPrivacidade,
                usuarioAtual.Origem,
                ct
            )
        );

    /// <summary>
    /// O descadastro de um clique do e-mail de marketing — sem login.
    /// </summary>
    /// <remarks>
    /// Atende os dois caminhos: o <c>POST</c> que o cliente de e-mail faz sozinho pelo <c>List-Unsubscribe-Post</c>
    /// (RFC 8058, corpo <c>List-Unsubscribe=One-Click</c>, ignorado) e o botão da página do app. O token vem na
    /// query nos dois.
    /// <para>
    /// Responde 204 para token válido, vencido ou adulterado: endpoint anônimo não confirma existência de conta.
    /// <c>POST</c> e nunca <c>GET</c> — antivírus de e-mail visitam os links, e sair não pode depender de ninguém
    /// ter aberto a mensagem.
    /// </para>
    /// </remarks>
    /// <param name="token">O token do link.</param>
    [HttpPost("descadastro")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitConfig.Webhook)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Descadastrar([FromQuery] string? token, CancellationToken ct) =>
        Responder(await comunicacaoDoKapa.Descadastrar(token, usuarioAtual.Origem, ct));

    private async Task<IActionResult> Solicitar(TipoDeSolicitacao tipo, string? senha, CancellationToken ct) =>
        Responder((await privacidadeService.Solicitar(tipo, usuarioAtual.Id, senha, ct)).Map(s => s.Adapt<SolicitacaoDePrivacidadeDTO>()));
}
