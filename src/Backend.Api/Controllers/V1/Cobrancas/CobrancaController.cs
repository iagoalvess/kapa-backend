using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Comum;
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
/// O plano financeiro da turma e as parcelas que ele gera.
/// </summary>
/// <remarks>
/// A tesouraria monta e simula; colocar em vigor é do Presidente — é uma decisão de centenas de
/// milhares de reais. A gestão inteira consulta as parcelas. Escrita exige a turma ativa; leitura
/// e simulação, não.
/// <para>
/// Plano de outra turma não existe aqui: o filtro global o esconde e a resposta é 404.
/// </para>
/// </remarks>
/// <param name="cobrancaService">Regras do plano.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Cobrancas)]
[Route("api/v{version:apiVersion}/cobrancas")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class CobrancaController(ICobrancaService cobrancaService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Nome da rota do detalhe do plano, para o <c>Location</c> da criação.</summary>
    public const string RotaDoPlano = "PlanoDeCobrancaPorId";

    /// <summary>Os planos da turma, o vigente primeiro.</summary>
    [HttpGet("planos")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(IReadOnlyList<PlanoDeCobrancaResumoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Responder((await cobrancaService.Listar(ct)).Map(planos => planos.Adapt<List<PlanoDeCobrancaResumoDTO>>()));

    /// <summary>Um plano, com os itens.</summary>
    /// <param name="id">Plano.</param>
    [HttpGet("planos/{id:guid}", Name = RotaDoPlano)]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(PlanoDeCobrancaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct) => Responder(ParaDTO(await cobrancaService.Obter(id, ct)));

    /// <summary>Cria um plano em montagem, sem itens.</summary>
    /// <param name="requisicao">Nome e regras de atraso.</param>
    [HttpPost("planos")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("cobranca.plano_criado")]
    [ProducesResponseType(typeof(PlanoDeCobrancaDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Criar([FromBody] PlanoDeCobrancaRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await cobrancaService.Criar(requisicao.Adapt<DadosDoPlano>(), ct);

        return Criado(ParaDTO(resultado), RotaDoPlano, new { id = resultado.Sucesso ? resultado.Valor.Id : Guid.Empty });
    }

    /// <summary>Altera nome e regras de atraso. Quem já aderiu fica com as regras que aceitou.</summary>
    /// <param name="id">Plano.</param>
    /// <param name="requisicao">Nome e regras de atraso.</param>
    [HttpPut("planos/{id:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("cobranca.plano_alterado", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(PlanoDeCobrancaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] PlanoDeCobrancaRequestDTO requisicao, CancellationToken ct) =>
        Responder(ParaDTO(await cobrancaService.Atualizar(id, requisicao.Adapt<DadosDoPlano>(), ct)));

    /// <summary>
    /// Inclui um item. Devolve o plano inteiro.
    /// </summary>
    /// <remarks>
    /// Por padrão o item vale só para quem aderir daqui em diante — a parcela nasce na adesão.
    /// Com <c>aplicar_a_quem_ja_aderiu</c> e a <c>origem_da_decisao</c>, ele é um rateio
    /// extraordinário e alcança também quem já aderiu; sem a origem, 400
    /// <c>cobranca.origem_obrigatoria</c>, e com o primeiro mês no passado, 400
    /// <c>cobranca.rateio_retroativo</c>.
    /// </remarks>
    /// <param name="id">Plano.</param>
    /// <param name="requisicao">Item, com o rateio opcional.</param>
    [HttpPost("planos/{id:guid}/itens")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("cobranca.item_adicionado", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(PlanoDeCobrancaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AdicionarItem(Guid id, [FromBody] ItemDeCobrancaRequestDTO requisicao, CancellationToken ct)
    {
        var rateio = requisicao.AplicarAQuemJaAderiu ? new RateioExtraordinario(requisicao.OrigemDaDecisao ?? string.Empty) : null;

        return Responder(ParaDTO(await cobrancaService.AdicionarItem(id, requisicao.Adapt<DadosDoItem>(), rateio, ct)));
    }

    /// <summary>Altera um item. Com parcela gerada, só valor e descrição — e o valor novo vale só para o que não venceu.</summary>
    /// <param name="id">Plano.</param>
    /// <param name="itemId">Item.</param>
    /// <param name="requisicao">Dados novos.</param>
    [HttpPut("planos/{id:guid}/itens/{itemId:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(PlanoDeCobrancaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AlterarItem(Guid id, Guid itemId, [FromBody] ItemDeCobrancaRequestDTO requisicao, CancellationToken ct) =>
        Responder(ParaDTO(await cobrancaService.AlterarItem(id, itemId, requisicao.Adapt<DadosDoItem>(), usuarioAtual.Id, ct)));

    /// <summary>Remove um item que nunca gerou parcela. Em uso, 409 <c>cobranca.item_em_uso</c>: encerre-o.</summary>
    /// <param name="id">Plano.</param>
    /// <param name="itemId">Item.</param>
    [HttpDelete("planos/{id:guid}/itens/{itemId:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(PlanoDeCobrancaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoverItem(Guid id, Guid itemId, CancellationToken ct) =>
        Responder(ParaDTO(await cobrancaService.RemoverItem(id, itemId, usuarioAtual.Id, ct)));

    /// <summary>Encerra um item: para de cobrar e cancela as parcelas que vencem de amanhã em diante.</summary>
    /// <param name="id">Plano.</param>
    /// <param name="itemId">Item.</param>
    [HttpPost("planos/{id:guid}/itens/{itemId:guid}/encerrar")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(PlanoDeCobrancaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EncerrarItem(Guid id, Guid itemId, CancellationToken ct) =>
        Responder(ParaDTO(await cobrancaService.EncerrarItem(id, itemId, usuarioAtual.Id, ct)));

    /// <summary>A grade de um formando e o total da turma, sem gravar nada.</summary>
    /// <remarks>
    /// Com <c>itens</c>, simula o formulário ainda não salvo — é a prévia ao lado dos campos. Sem,
    /// simula o plano gravado — é o número da confirmação de "colocar em vigor".
    /// </remarks>
    /// <param name="id">Plano.</param>
    /// <param name="requisicao">Itens a simular.</param>
    [HttpPost("planos/{id:guid}/simular")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(SimulacaoDoPlanoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Simular(Guid id, [FromBody] SimularPlanoRequestDTO? requisicao, CancellationToken ct)
    {
        var resultado = await cobrancaService.Simular(FormaturaId, id, new SimularPlano(requisicao?.Itens?.Adapt<List<DadosDoItem>>()), ct);

        return Responder(resultado.Map(simulacao => simulacao.Adapt<SimulacaoDoPlanoDTO>()));
    }

    /// <summary>Coloca o plano em vigor. Só o Presidente, e só um plano vigente por turma.</summary>
    /// <param name="id">Plano.</param>
    [HttpPost("planos/{id:guid}/vigorar")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(PlanoDeCobrancaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Vigorar(Guid id, CancellationToken ct) =>
        Responder(ParaDTO(await cobrancaService.Vigorar(id, usuarioAtual.Id, ct)));

    /// <summary>As parcelas da turma, por vencimento.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="usuarioId">Só as deste formando.</param>
    /// <param name="status">Situação no dia de hoje: <c>Aberta</c>, <c>Vencida</c>, <c>Paga</c>, <c>Cancelada</c> ou <c>Renegociada</c>.</param>
    /// <param name="de">Vencimento a partir deste dia, <c>aaaa-mm-dd</c>, inclusive.</param>
    /// <param name="ate">Vencimento até este dia, <c>aaaa-mm-dd</c>, inclusive.</param>
    /// <param name="busca">Trecho do nome da conta ou do nome civil do formando.</param>
    [HttpGet("parcelas")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(PaginaDTO<ParcelaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarParcelas(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] Guid? usuarioId,
        [FromQuery] StatusDaParcela? status,
        [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate,
        [FromQuery] string? busca,
        CancellationToken ct
    )
    {
        var resultado = await cobrancaService.ListarParcelas(paginacao.ParaModelo(), new FiltroDeParcelas(usuarioId, status, de, ate, busca), ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(parcela => parcela.Adapt<ParcelaDTO>())));
    }

    /// <summary>Quantas parcelas e quanto somam por situação — a faixa da tela Parcelas, numa chamada.</summary>
    /// <remarks>Os mesmos filtros da lista, menos a situação: a faixa mostra todas, e as pílulas contam cada uma.</remarks>
    /// <param name="usuarioId">Só as deste formando.</param>
    /// <param name="de">Vencimento a partir deste dia, inclusive.</param>
    /// <param name="ate">Vencimento até este dia, inclusive.</param>
    /// <param name="busca">Trecho do nome do formando.</param>
    [HttpGet("parcelas/resumo")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(ResumoDeParcelasDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResumirParcelas(
        [FromQuery] Guid? usuarioId,
        [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate,
        [FromQuery] string? busca,
        CancellationToken ct
    )
    {
        var resultado = await cobrancaService.ResumirParcelas(new FiltroDeParcelas(usuarioId, null, de, ate, busca), ct);

        return Responder(resultado.Map(resumo => resumo.Adapt<ResumoDeParcelasDTO>()));
    }

    private static Result<PlanoDeCobrancaDTO> ParaDTO(Result<PlanoDeCobrancaDetalhe> resultado) =>
        resultado.Map(plano => plano.Adapt<PlanoDeCobrancaDTO>());
}
