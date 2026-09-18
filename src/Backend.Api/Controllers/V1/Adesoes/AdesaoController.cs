using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Adesoes;
using Backend.Api.DTOs.Comum;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Legal.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Adesoes;

/// <summary>
/// O termo de adesão da turma e o aceite de cada formando — o gatilho que transforma o plano em dívida.
/// </summary>
/// <remarks>
/// O Presidente publica o termo; todo membro lê, pede o código por e-mail e adere com ele (a comissão
/// também se forma); a gestão acompanha quem aderiu. <c>/eu</c> em vez de id: quem é "eu" vem do
/// token. Adesão de outro formando não existe para quem não é da gestão — o PDF responde 404, como
/// arquivo de terceiro.
/// </remarks>
/// <param name="termoService">Versões do termo.</param>
/// <param name="adesaoService">Aceite e acompanhamento.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/adesoes")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class AdesaoController(ITermoService termoService, IAdesaoService adesaoService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Nome da rota do conteúdo vigente, para o <c>Location</c> da publicação.</summary>
    public const string RotaDoTermoVigente = "TermoDeAdesaoVigente";

    /// <summary>Nome da rota da própria adesão, para o <c>Location</c> do aceite.</summary>
    public const string RotaDaMinhaAdesao = "MinhaAdesao";

    /// <summary>As versões publicadas do termo, da mais nova para a mais antiga.</summary>
    [HttpGet("termos")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [ProducesResponseType(typeof(IReadOnlyList<TermoPublicadoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarTermos(CancellationToken ct) =>
        Responder((await termoService.Listar(ct)).Map(termos => termos.Adapt<List<TermoPublicadoDTO>>()));

    /// <summary>Publica a versão seguinte do termo. Quem já aderiu continua na versão que aceitou.</summary>
    /// <param name="requisicao">Texto da versão.</param>
    [HttpPost("termos")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(VersaoDoTermoDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publicar([FromBody] PublicarTermoRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await termoService.Publicar(usuarioAtual.Id, new PublicarTermo(requisicao.Conteudo ?? string.Empty), ct);

        return Criado(resultado.Map(termo => termo.Adapt<VersaoDoTermoDTO>()), RotaDoTermoVigente, new { });
    }

    /// <summary>O termo vigente, o plano vigente e o hash dos dois — o que a tela exibe antes do aceite.</summary>
    /// <remarks>Sem termo publicado ou sem plano vigente, a parte que falta vem ausente, e o hash também.</remarks>
    [HttpGet("termos/vigente", Name = RotaDoTermoVigente)]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(ConteudoParaAdesaoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterParaAdesao(CancellationToken ct) =>
        Responder((await termoService.ObterParaAdesao(ct)).Map(conteudo => conteudo.Adapt<ConteudoParaAdesaoDTO>()));

    /// <summary>Envia ao e-mail da conta o código de seis dígitos que o aceite pede.</summary>
    /// <remarks>
    /// 409 com <c>adesao.sem_termo_publicado</c>, <c>adesao.sem_plano_vigente</c> ou
    /// <c>adesao.ja_aderiu</c> — não se gasta código onde o aceite já seria recusado. A resposta traz
    /// o e-mail mascarado, para a tela dizer em que caixa de entrada olhar.
    /// </remarks>
    [HttpPost("codigo")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [EnableRateLimiting(RateLimitConfig.Codigo)]
    [RegistrarEvento("adesao.codigo_solicitado")]
    [ProducesResponseType(typeof(CodigoEnviadoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SolicitarCodigo(CancellationToken ct) =>
        Responder((await adesaoService.SolicitarCodigo(FormaturaId, usuarioAtual.Id, ct)).Map(envio => envio.Adapt<CodigoEnviadoDTO>()));

    /// <summary>Aceita o termo vigente e gera as parcelas, na mesma transação.</summary>
    /// <remarks>
    /// 409 com <c>adesao.sem_termo_publicado</c>, <c>adesao.sem_plano_vigente</c>, <c>adesao.ja_aderiu</c>,
    /// <c>adesao.termo_desatualizado</c> (recarregue e leia de novo), <c>adesao.cadastro_incompleto</c>,
    /// <c>adesao.menor_de_idade</c>, <c>adesao.cpf_em_uso</c> ou <c>adesao.codigo_invalido</c> (peça outro
    /// em <c>POST /adesoes/codigo</c>).
    /// <para>
    /// O limite estreito é o mesmo do envio: o código tem seis dígitos e vale poucos minutos, e sem
    /// teto de tentativas o espaço seria varrido.
    /// </para>
    /// </remarks>
    /// <param name="requisicao">Hash do conteúdo exibido e o código recebido por e-mail.</param>
    [HttpPost]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [EnableRateLimiting(RateLimitConfig.Codigo)]
    [RegistrarEvento("adesao.aceite_registrado")]
    [ProducesResponseType(typeof(AdesaoDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Aderir([FromBody] AderirRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await adesaoService.Aderir(
            FormaturaId,
            usuarioAtual.Id,
            new AderirAoTermo(requisicao.HashDoConteudo ?? string.Empty, requisicao.Codigo ?? string.Empty),
            new OrigemDoAceite(usuarioAtual.EnderecoIp, usuarioAtual.UserAgent),
            ct
        );

        return Criado(resultado.Map(adesao => adesao.Adapt<AdesaoDTO>()), RotaDaMinhaAdesao, new { });
    }

    /// <summary>A própria adesão mais recente e o que falta no cadastro para aderir.</summary>
    [HttpGet("eu", Name = RotaDaMinhaAdesao)]
    // Aceita o desligado: o termo aceito vigorou, e foi sob ele que ele pagou o que pagou (P5).
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [ProducesResponseType(typeof(MinhaAdesaoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterMinha(CancellationToken ct) =>
        Responder((await adesaoService.ObterMinha(FormaturaId, usuarioAtual.Id, ct)).Map(minha => minha.Adapt<MinhaAdesaoDTO>()));

    /// <summary>Quem aderiu e quem falta: os membros ativos com a adesão mais recente de cada um.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="aderiu">Só quem aderiu (<c>true</c>), só quem falta (<c>false</c>) ou todos.</param>
    /// <param name="busca">Trecho do nome ou do e-mail.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(PaginaDTO<SituacaoDeAdesaoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] bool? aderiu,
        [FromQuery] string? busca,
        CancellationToken ct
    )
    {
        var resultado = await adesaoService.Listar(FormaturaId, paginacao.ParaModelo(), new FiltroDeAdesoes(aderiu, busca), ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(situacao => situacao.Adapt<SituacaoDeAdesaoDTO>())));
    }

    /// <summary>Quantos aderiram, de quantos, e a versão vigente — o número do topo do painel.</summary>
    [HttpGet("resumo")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(ResumoDeAdesoesDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resumir(CancellationToken ct) =>
        Responder((await adesaoService.Resumir(FormaturaId, ct)).Map(resumo => resumo.Adapt<ResumoDeAdesoesDTO>()));

    /// <summary>O termo assinado em PDF. O próprio formando e a gestão; para os demais, 404.</summary>
    /// <remarks>
    /// Não usa os helpers do <c>MainController</c> porque o sucesso é o arquivo, não JSON. Remontado a
    /// cada pedido, e igual para a mesma adesão hoje e daqui a um ano.
    /// </remarks>
    /// <param name="id">Adesão.</param>
    [HttpGet("{id:guid}/pdf")]
    // Aceita o desligado: o PDF do próprio termo acompanha a adesão. O de terceiro o service recusa.
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BaixarPdf(Guid id, CancellationToken ct)
    {
        var resultado = await adesaoService.ObterPdf(FormaturaId, id, usuarioAtual.Id, ct);

        if (resultado.Falhou)
            return Responder(resultado.Map(_ => 0));

        return File(resultado.Valor.Conteudo, "application/pdf", resultado.Valor.NomeDoArquivo);
    }

    /// <summary>Lembra por e-mail um membro que ainda não aderiu à versão vigente.</summary>
    /// <param name="usuarioId">Membro lembrado.</param>
    [HttpPost("{usuarioId:guid}/lembrete")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("adesao.lembrete_enviado", CamposDaRota = ["usuarioId"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Lembrar(Guid usuarioId, CancellationToken ct) =>
        Responder(await adesaoService.Lembrar(FormaturaId, usuarioId, ct));
}
