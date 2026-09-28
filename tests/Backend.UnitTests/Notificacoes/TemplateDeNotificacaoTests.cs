using Backend.Business.Notificacoes.Models;
using Backend.Business.Notificacoes.Services;
using Shouldly;

namespace Backend.UnitTests.Notificacoes;

/// <summary>
/// Os textos da régua do Kapa e a troca das variáveis.
/// </summary>
/// <remarks>
/// O texto é do Kapa desde 24/09/2026: variável digitada errada não tem mais editor que a recuse, e
/// é este teste que a pega antes de ela sair para a turma inteira.
/// </remarks>
public sealed class TemplateDeNotificacaoTests
{
    private static readonly Dictionary<string, string> Valores = new(StringComparer.Ordinal)
    {
        ["nome"] = "Júlia",
        ["valor"] = "R$ 350,00",
        ["vencimento"] = "10/09/2026",
        ["formatura"] = "Medicina 2027",
        ["quantidade"] = "3",
    };

    private static IEnumerable<TextoDaMensagem> Textos() =>
        ReguaDoKapa.Degraus.SelectMany(d => d.ResumoDaTesouraria is { } resumo ? [d.Texto, resumo] : new[] { d.Texto });

    [Fact]
    public void Troca_as_variaveis_conhecidas() =>
        TemplateDeNotificacao
            .Renderizar("Oi, {nome}! São {valor} até {vencimento}.", Valores)
            .ShouldBe("Oi, J&#250;lia! S&#227;o R$ 350,00 at&#233; 10/09/2026.");

    /// <summary>Nome de pessoa é texto de terceiro, e o corpo do e-mail é HTML.</summary>
    [Fact]
    public void O_valor_entra_escapado_no_corpo() =>
        TemplateDeNotificacao
            .Renderizar("Oi, {nome}", new Dictionary<string, string>(StringComparer.Ordinal) { ["nome"] = "<script>x</script>" })
            .ShouldBe("Oi, &lt;script&gt;x&lt;/script&gt;");

    [Fact]
    public void Variavel_sem_valor_vira_texto_vazio() =>
        TemplateDeNotificacao.Renderizar("[{quantidade}]", new Dictionary<string, string>(StringComparer.Ordinal)).ShouldBe("[]");

    [Fact]
    public void Aponta_a_variavel_que_nao_existe() => TemplateDeNotificacao.Desconhecidas("Vence em {vencimeto}").ShouldBe(["vencimeto"]);

    [Fact]
    public void O_corpo_de_todo_degrau_so_usa_variavel_que_existe() =>
        Textos().SelectMany(t => TemplateDeNotificacao.Desconhecidas(t.Corpo)).ShouldBeEmpty();

    /// <summary>O assunto não passa pela troca: variável nele sairia com as chaves.</summary>
    [Fact]
    public void O_assunto_de_todo_degrau_nao_tem_variavel() => Textos().ShouldAllBe(t => !t.Assunto.Contains('{'));

    [Fact]
    public void Cada_degrau_aparece_uma_vez() =>
        ReguaDoKapa.Degraus.Select(d => (d.Gatilho, d.DiasDeDeslocamento)).Distinct().Count().ShouldBe(ReguaDoKapa.Degraus.Count);
}
