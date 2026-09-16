using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;

namespace Backend.Business.Eventos.Services;

/// <summary>
/// O evento de auditoria, gravado no mesmo <c>SalvarAsync</c> da operação que ele descreve.
/// </summary>
/// <remarks>
/// Pela transação, e não pela fila de analytics — que descarta quando enche: troca de chave PIX, baixa
/// e estorno não podem acontecer sem deixar rastro. A formatura vai nos dados: a tabela de eventos não
/// tem coluna de formatura, e a trilha (Sprint 14) é por turma.
/// </remarks>
public static class Auditoria
{
    /// <summary>
    /// Serialização do corpo do evento — em camelCase, e não no snake_case da API.
    /// </summary>
    /// <remarks>
    /// Isto não é contrato de transporte: é uma linha gravada em <c>eventos</c>, lida meses depois
    /// no <c>psql</c> ou num painel, sobre um fato que já aconteceu. Trocar a convenção renomearia
    /// as chaves só dos eventos novos, e quem consultasse o histórico teria de procurar pelos dois
    /// nomes. Registro passado não se reescreve.
    /// </remarks>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Marca o evento para gravar junto da operação.</summary>
    /// <param name="eventos">Repositório de eventos.</param>
    /// <param name="nome">Nome estável, <c>recurso.acao</c>.</param>
    /// <param name="usuarioId">Autor.</param>
    /// <param name="dados">O que aconteceu — com a <c>formaturaId</c>.</param>
    public static Task Auditar(this IEventoRepository eventos, string nome, Guid usuarioId, object dados, CancellationToken ct = default) =>
        eventos.Adicionar(
            new Evento
            {
                Nome = nome,
                UsuarioId = usuarioId,
                Dados = JsonSerializer.Serialize(dados, Json),
            },
            ct
        );
}
