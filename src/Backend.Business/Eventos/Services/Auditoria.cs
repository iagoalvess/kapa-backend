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
    /// <remarks>
    /// A <c>formaturaId</c> do corpo é copiada para a <b>coluna</b> <c>Evento.FormaturaId</c>, que é
    /// por onde a trilha da Sprint 14 filtra — ela precisa de índice, e um índice sobre uma chave
    /// dentro do <c>jsonb</c> obrigaria a expressão do filtro e a do índice a serem escritas igual
    /// para sempre, em dois arquivos diferentes.
    /// <para>
    /// A cópia acontece aqui, e não na assinatura, de propósito: nenhum dos chamadores muda, o corpo
    /// continua sendo a fonte, e evento de auditoria novo entra na tela sem ninguém lembrar de um
    /// parâmetro a mais. Corpo sem <c>formaturaId</c> grava a coluna nula — é o caso do bloqueio por
    /// tentativas, que é da conta e não de uma turma.
    /// </para>
    /// </remarks>
    /// <param name="eventos">Repositório de eventos.</param>
    /// <param name="nome">Nome estável, <c>recurso.acao</c>. Use uma constante de <see cref="NomesDeAuditoria"/>.</param>
    /// <param name="usuarioId">Autor.</param>
    /// <param name="dados">O que aconteceu — com a <c>formaturaId</c>, quando houver turma.</param>
    public static Task Auditar(this IEventoRepository eventos, string nome, Guid usuarioId, object dados, CancellationToken ct = default)
    {
        var corpo = JsonSerializer.SerializeToElement(dados, Json);

        return eventos.Adicionar(
            new Evento
            {
                Nome = nome,
                UsuarioId = usuarioId,
                FormaturaId = FormaturaDo(corpo),
                Dados = corpo.GetRawText(),
            },
            ct
        );
    }

    /// <summary>A turma do corpo do evento, se ele tiver uma.</summary>
    /// <remarks>
    /// Aceita a chave em qualquer caixa inicial porque o corpo é um objeto anônimo escrito à mão em
    /// cada service: <c>new { formaturaId, … }</c> e <c>new { contexto.FormaturaId, … }</c> chegam
    /// aqui com a mesma intenção e nomes diferentes, e a diferença não pode custar a linha na tela.
    /// </remarks>
    /// <param name="corpo">Corpo já serializado.</param>
    private static Guid? FormaturaDo(JsonElement corpo)
    {
        if (corpo.ValueKind != JsonValueKind.Object)
            return null;

        // `ValueKind` antes de `TryGetGuid`: o `TryGet` do `JsonElement` **lança** quando o valor é
        // de outro tipo, em vez de devolver falso, e corpo com `formaturaId` nulo — o que acontece
        // quando quem audita passa um `Guid?` sem turma na sessão — derrubava a própria operação
        // que estava sendo auditada.
        foreach (var chave in (string[])["formaturaId", "FormaturaId"])
        {
            if (corpo.TryGetProperty(chave, out var valor) && valor.ValueKind == JsonValueKind.String && valor.TryGetGuid(out var formaturaId))
                return formaturaId;
        }

        return null;
    }
}
