using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Enquetes;
using Backend.Api.Extensions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Enquetes.Interfaces;
using Backend.Business.Enquetes.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Enquetes;

/// <summary>Consultas da turma: leitura de membro, gestão da comissão e resposta do próprio titular.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/enquetes")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
[ExigeModulo(Modulo.Enquetes)]
public sealed class EnquetesController(IEnqueteService enquetes, IUsuarioAtual usuario) : MainController
{
    /// <summary>Rota usada no Location da publicação.</summary>
    public const string RotaDaEnquete = "EnquetePorId";

    /// <summary>Abertas e histórico, com participação e resposta do próprio usuário.</summary>
    [HttpGet]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(IReadOnlyList<EnqueteResumo>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(CancellationToken ct) => Responder(await enquetes.Listar(usuario.Id, ct));

    /// <summary>Detalhe e resultado agregado ao vivo.</summary>
    [HttpGet("{id:guid}", Name = RotaDaEnquete)]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(EnqueteDetalhe), StatusCodes.Status200OK)]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct) => Responder(await enquetes.Obter(id, usuario.Id, ct));

    /// <summary>Publica uma enquete nova.</summary>
    [HttpPost]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("enquete.criada")]
    [ProducesResponseType(typeof(EnqueteDetalhe), StatusCodes.Status201Created)]
    public async Task<IActionResult> Criar(EnqueteRequestDTO requisicao, CancellationToken ct) =>
        Criado(await enquetes.Criar(ParaModelo(requisicao), usuario.Id, ct), RotaDaEnquete, dto => dto.Enquete.Id);

    /// <summary>Corrige o cadastro dentro das regras do primeiro voto.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("enquete.alterada", CamposDaRota = ["id"])]
    public async Task<IActionResult> Atualizar(Guid id, EnqueteRequestDTO requisicao, CancellationToken ct) =>
        Responder(await enquetes.Atualizar(id, ParaModelo(requisicao), usuario.Id, ct));

    /// <summary>Encerra antes do prazo.</summary>
    [HttpPost("{id:guid}/encerrar")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("enquete.encerrada", CamposDaRota = ["id"])]
    public async Task<IActionResult> Encerrar(Guid id, CancellationToken ct) => Responder(await enquetes.Encerrar(id, ct));

    /// <summary>Cancela, mantendo as respostas para a Gestão.</summary>
    [HttpPost("{id:guid}/cancelar")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("enquete.cancelada", CamposDaRota = ["id"])]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct) => Responder(await enquetes.Cancelar(id, ct));

    /// <summary>Envia ou muda a resposta do próprio usuário; a auditoria nunca inclui escolhas.</summary>
    [HttpPut("{id:guid}/resposta")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("enquete.respondida", CamposDaRota = ["id"])]
    public async Task<IActionResult> Responder(Guid id, RespostaDaEnqueteRequestDTO requisicao, CancellationToken ct) =>
        Responder(await enquetes.Responder(id, usuario.Id, requisicao.Opcoes ?? [], ct));

    /// <summary>Retira a própria resposta enquanto aberta.</summary>
    [HttpDelete("{id:guid}/resposta")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("enquete.resposta_retirada", CamposDaRota = ["id"])]
    public async Task<IActionResult> Retirar(Guid id, CancellationToken ct) => Responder(await enquetes.Retirar(id, usuario.Id, ct));

    /// <summary>Normaliza campos ausentes antes da validação de domínio.</summary>
    private static DadosDaEnquete ParaModelo(EnqueteRequestDTO r) =>
        new(r.Pergunta ?? string.Empty, r.Descricao, r.Tipo, r.MaximoDeEscolhas, r.QuorumMinimoPercentual, r.EncerraEm, r.Opcoes ?? []);
}
