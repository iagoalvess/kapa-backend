using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Financeiro;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Financeiro;

/// <summary>
/// O dinheiro que entra na conta da turma sem ser parcela de formando (Sprint 28).
/// </summary>
/// <remarks>
/// O espelho de <see cref="DespesaController"/>, no mesmo módulo: quem tem despesas tem receitas.
/// Escrever é da Tesouraria (P1), com a turma ativa; ler é de todo membro, pela mesma razão da
/// despesa — prestação de contas que só a comissão enxerga não presta contas a ninguém.
/// <para>
/// Sem multipart: o comprovante é um documento que já está no acervo, informado por
/// <c>documento_id</c>, e abre pelo download de lá.
/// </para>
/// </remarks>
/// <param name="outraReceitaService">Regras da receita.</param>
/// <param name="usuarioAtual">Quem chama — autor na trilha do cancelamento.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Despesas)]
[Route("api/v{version:apiVersion}/financeiro/outras-receitas")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class OutraReceitaController(IOutraReceitaService outraReceitaService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Nome da rota de uma receita — o <c>Location</c> do lançamento aponta para ela.</summary>
    public const string RotaDaOutraReceita = "OutraReceitaPorId";

    /// <summary>As receitas da turma, da mais recente.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="filtro">Categoria, situação, período e busca.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(PaginaDTO<OutraReceitaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] FiltroDeOutrasReceitas filtro,
        CancellationToken ct
    )
    {
        var resultado = await outraReceitaService.Listar(paginacao.ParaModelo(), filtro, ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(outraReceita => outraReceita.Adapt<OutraReceitaDTO>())));
    }

    /// <summary>Quantas e quanto, por situação, dentro do mesmo filtro — a faixa da tela, numa consulta.</summary>
    /// <param name="filtro">Mesmos filtros da lista; a situação é ignorada.</param>
    [HttpGet("resumo")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(ResumoDeOutrasReceitasDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resumir([FromQuery] FiltroDeOutrasReceitas filtro, CancellationToken ct) =>
        Responder((await outraReceitaService.Resumir(filtro, ct)).Map(resumo => resumo.Adapt<ResumoDeOutrasReceitasDTO>()));

    /// <summary>Uma receita da turma.</summary>
    /// <param name="id">Receita.</param>
    [HttpGet("{id:guid}", Name = RotaDaOutraReceita)]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(OutraReceitaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct) =>
        Responder((await outraReceitaService.ObterPorId(id, ct)).Map(outraReceita => outraReceita.Adapt<OutraReceitaDTO>()));

    /// <summary>Lança a receita, prevista ou já recebida.</summary>
    /// <remarks>409 <c>financeiro.outra_receita_duplicada</c> no clique repetido.</remarks>
    /// <param name="requisicao">Descrição, origem, categoria, valor, data e comprovante.</param>
    [HttpPost]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("financeiro.outra_receita_lancada")]
    [ProducesResponseType(typeof(OutraReceitaDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Lancar([FromBody] NovaOutraReceitaRequestDTO requisicao, CancellationToken ct)
    {
        var dados = new NovaOutraReceita(
            requisicao.Descricao ?? string.Empty,
            requisicao.Origem,
            requisicao.Categoria,
            requisicao.ValorEmCentavos,
            requisicao.Data,
            requisicao.Recebida,
            requisicao.DocumentoId
        );

        return Criado(
            (await outraReceitaService.Lancar(dados, ct)).Map(outraReceita => outraReceita.Adapt<OutraReceitaDTO>()),
            RotaDaOutraReceita,
            dto => dto.Id
        );
    }

    /// <summary>Corrige uma receita lançada — prevista ou recebida.</summary>
    /// <param name="id">Receita.</param>
    /// <param name="requisicao">Dados novos.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("financeiro.outra_receita_alterada", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(OutraReceitaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarOutraReceitaRequestDTO requisicao, CancellationToken ct)
    {
        var dados = new DadosDaOutraReceita(
            requisicao.Descricao ?? string.Empty,
            requisicao.Origem,
            requisicao.Categoria,
            requisicao.ValorEmCentavos,
            requisicao.Data,
            requisicao.DocumentoId
        );

        return Responder((await outraReceitaService.Atualizar(id, dados, ct)).Map(outraReceita => outraReceita.Adapt<OutraReceitaDTO>()));
    }

    /// <summary>Registra a entrada do dinheiro de uma receita prevista.</summary>
    /// <remarks>409 <c>financeiro.outra_receita_ja_recebida</c> na segunda vez.</remarks>
    /// <param name="id">Receita.</param>
    /// <param name="requisicao">Dia em que o dinheiro entrou.</param>
    [HttpPost("{id:guid}/receber")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("financeiro.outra_receita_recebida", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(OutraReceitaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Receber(Guid id, [FromBody] ReceberOutraReceitaRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (await outraReceitaService.Receber(id, new ReceberOutraReceita(requisicao.RecebidaEm), ct)).Map(outraReceita =>
                outraReceita.Adapt<OutraReceitaDTO>()
            )
        );

    /// <summary>Cancela uma receita prevista. A recebida se corrige, não se cancela.</summary>
    /// <param name="id">Receita.</param>
    [HttpPost("{id:guid}/cancelar")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(OutraReceitaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct) =>
        Responder((await outraReceitaService.Cancelar(id, usuarioAtual.Id, ct)).Map(outraReceita => outraReceita.Adapt<OutraReceitaDTO>()));
}
