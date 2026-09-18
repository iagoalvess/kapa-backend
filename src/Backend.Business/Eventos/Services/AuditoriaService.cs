using Backend.Business.Abstractions;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;

namespace Backend.Business.Eventos.Services;

/// <summary>
/// A trilha de auditoria da turma.
/// </summary>
/// <remarks>
/// Fino de propósito: a consulta inteira é do repositório, porque o recorte por turma é uma
/// expressão sobre a coluna <c>jsonb</c> e não tem regra de negócio nenhuma em volta. O que este
/// service acrescenta é a normalização da página — sem ela, <c>tamanho=100000</c> vindo da query
/// string vira uma consulta que varre a tabela que mais cresce no banco.
/// </remarks>
/// <param name="eventoRepository">Tabela de eventos.</param>
public sealed class AuditoriaService(IEventoRepository eventoRepository) : IAuditoriaService
{
    /// <inheritdoc />
    public async Task<Result<PaginaDe<LinhaDeAuditoria>>> Listar(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeAuditoria filtro,
        CancellationToken ct = default
    ) => Result.Ok(await eventoRepository.ListarAuditoria(formaturaId, paginacao.Normalizar(), filtro, ct));

    /// <inheritdoc />
    public async Task<Result<OpcoesDeAuditoria>> Opcoes(Guid formaturaId, CancellationToken ct = default) =>
        Result.Ok(await eventoRepository.OpcoesDeAuditoria(formaturaId, ct));

    /// <inheritdoc />
    public async Task<Result<ResumoDaAuditoria>> Resumir(Guid formaturaId, CancellationToken ct = default) =>
        Result.Ok(await eventoRepository.ResumirAuditoria(formaturaId, ct));
}
