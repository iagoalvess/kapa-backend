using System.Reflection;
using Backend.Business.Eventos.Models;
using Shouldly;

namespace Backend.UnitTests.Eventos;

/// <summary>
/// A trilha de auditoria é completa: todo nome auditável é escrito pela transação, e nenhum pela
/// fila de analytics.
/// </summary>
/// <remarks>
/// Achado em 17/09/2026: seis nomes de <see cref="NomesDeAuditoria"/> — os quatro de cobrança, o
/// termo publicado e a despesa cancelada — eram gravados por <c>[RegistrarEvento]</c>, a fila que
/// descarta quando enche. O efeito eram três coisas de uma vez: o evento não levava
/// <c>formaturaId</c> e portanto <b>nunca aparecia na trilha da turma</b>; o corpo era só o id da
/// rota, que a tela derruba por não virar nome; e ele podia ser perdido sob carga — o contrário do
/// que o próprio <see cref="NomesDeAuditoria"/> promete.
/// <para>
/// Este teste é a rede contra a reincidência. Ele lê o assembly da API: nome auditável usado num
/// <c>[RegistrarEvento]</c> reprova, e a mensagem diz qual.
/// </para>
/// </remarks>
public sealed class TrilhaCompletaTests
{
    /// <summary>O tipo do atributo, achado pelo nome para não arrastar a referência da API para cá.</summary>
    private static Type AtributoDeEvento =>
        typeof(Backend.Api.Controllers.MainController).Assembly.GetType("Backend.Api.Analytics.RegistrarEventoAttribute")!;

    [Fact]
    public void Nenhum_nome_auditavel_e_gravado_pela_fila_de_analytics()
    {
        // Arrange
        var auditaveis = NomesDeAuditoria.Todos.ToHashSet(StringComparer.Ordinal);

        // Act — todo `[RegistrarEvento("...")]` declarado nos controllers da API.
        var pelaFila = typeof(Backend.Api.Controllers.MainController)
            .Assembly.GetTypes()
            .SelectMany(tipo => tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(metodo => metodo.GetCustomAttributes(AtributoDeEvento, inherit: false))
            .Select(atributo => (string)AtributoDeEvento.GetProperty("Nome")!.GetValue(atributo)!)
            .Where(auditaveis.Contains)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        pelaFila.ShouldBeEmpty(
            $"Estes nomes são auditáveis e estão sendo gravados pela fila, que descarta quando enche e não "
                + $"preenche a formatura — eles somem da trilha da turma: {string.Join(", ", pelaFila)}. "
                + "Grave-os com IEventoRepository.Auditar, dentro da transação da operação."
        );
    }
}
