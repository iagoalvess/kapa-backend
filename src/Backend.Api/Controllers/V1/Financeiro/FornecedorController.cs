using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Financeiro;
using Backend.Business.Abstractions;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Financeiro;

/// <summary>
/// Quem a turma contrata.
/// </summary>
/// <remarks>
/// Tudo aqui é da Tesouraria — é ela que negocia e paga. Escrita exige a turma ativa.
/// <para>
/// Fornecedor com despesa lançada não é excluído: 409 <c>financeiro.fornecedor_em_uso</c>, e o
/// caminho é desativar (<c>ativo = false</c>) pelo próprio <c>PUT</c>.
/// </para>
/// </remarks>
/// <param name="fornecedorService">Regras do cadastro.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/financeiro/fornecedores")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class FornecedorController(IFornecedorService fornecedorService) : MainController
{
    /// <summary>Nome da rota do detalhe, para o <c>Location</c> da criação.</summary>
    public const string RotaDoFornecedor = "FornecedorPorId";

    /// <summary>Os fornecedores da turma, por nome.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="ativo">Só os ativos, só os inativos, ou todos.</param>
    /// <param name="categoria">Só os desta categoria.</param>
    /// <param name="busca">Trecho do nome ou do documento.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(PaginaDTO<FornecedorDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] bool? ativo,
        [FromQuery] CategoriaDeDespesa? categoria,
        [FromQuery] string? busca,
        CancellationToken ct
    )
    {
        var resultado = await fornecedorService.Listar(paginacao.ParaModelo(), new FiltroDeFornecedores(ativo, categoria, busca), ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(fornecedor => fornecedor.Adapt<FornecedorDTO>())));
    }

    /// <summary>Quantos fornecedores a turma tem ativos e inativos — os números das pílulas da tela.</summary>
    /// <remarks>
    /// Existe para a tela não pedir duas listas de um item só (<c>tamanho=1</c>) das quais só se lia o
    /// <c>total</c> — o caminho que a Sprint 10 tomou por não haver resumo aqui.
    /// </remarks>
    [HttpGet("resumo")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(ContagemDeFornecedoresDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resumir(CancellationToken ct) =>
        Responder((await fornecedorService.Contar(ct)).Map(contagem => contagem.Adapt<ContagemDeFornecedoresDTO>()));

    /// <summary>Um fornecedor da turma.</summary>
    /// <param name="id">Fornecedor.</param>
    [HttpGet("{id:guid}", Name = RotaDoFornecedor)]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(FornecedorDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct) =>
        Responder((await fornecedorService.ObterPorId(id, ct)).Map(fornecedor => fornecedor.Adapt<FornecedorDTO>()));

    /// <summary>Cadastra um fornecedor.</summary>
    /// <param name="requisicao">Nome, documento, categoria e contato.</param>
    [HttpPost]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("financeiro.fornecedor_criado")]
    [ProducesResponseType(typeof(FornecedorDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Criar([FromBody] FornecedorRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await fornecedorService.Criar(ParaModelo(requisicao), ct);

        return Criado(
            resultado.Map(fornecedor => fornecedor.Adapt<FornecedorDTO>()),
            RotaDoFornecedor,
            new { id = resultado.Sucesso ? resultado.Valor.Id : Guid.Empty }
        );
    }

    /// <summary>Altera o cadastro — inclusive a ativação.</summary>
    /// <param name="id">Fornecedor.</param>
    /// <param name="requisicao">Dados novos.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("financeiro.fornecedor_alterado", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(FornecedorDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] FornecedorRequestDTO requisicao, CancellationToken ct) =>
        Responder((await fornecedorService.Atualizar(id, ParaModelo(requisicao), ct)).Map(fornecedor => fornecedor.Adapt<FornecedorDTO>()));

    /// <summary>Exclui um fornecedor sem despesa lançada.</summary>
    /// <remarks>Com despesa, 409 <c>financeiro.fornecedor_em_uso</c>: desative em vez de excluir.</remarks>
    /// <param name="id">Fornecedor.</param>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("financeiro.fornecedor_excluido", CamposDaRota = ["id"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct) => Responder(await fornecedorService.Excluir(id, ct));

    /// <summary>O corpo como o service o espera; <c>ativo</c> ausente vale ativo.</summary>
    private static DadosDoFornecedor ParaModelo(FornecedorRequestDTO requisicao) =>
        new(
            requisicao.Nome ?? string.Empty,
            requisicao.Documento,
            requisicao.Categoria,
            requisicao.Telefone,
            requisicao.Email,
            requisicao.Observacoes,
            requisicao.Ativo ?? true
        );
}
