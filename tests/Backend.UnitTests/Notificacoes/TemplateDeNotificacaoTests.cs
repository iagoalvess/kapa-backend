using Backend.Business.Abstractions;
using Backend.Business.Notificacoes.Models;
using Backend.Business.Notificacoes.Services;
using Backend.Business.Notificacoes.Validators;
using Shouldly;

namespace Backend.UnitTests.Notificacoes;

/// <summary>
/// As variáveis do template: a troca, a proteção do HTML e a recusa na gravação.
/// </summary>
/// <remarks>
/// Critério de aceite: template com variável desconhecida é rejeitado na gravação, não no envio.
/// </remarks>
public sealed class TemplateDeNotificacaoTests
{
    private static readonly Dictionary<string, string> Valores = new(StringComparer.Ordinal)
    {
        ["nome"] = "Júlia",
        ["valor"] = "R$ 350,00",
        ["vencimento"] = "10/09/2026",
        ["link"] = "https://kapa.dev/extrato",
        ["formatura"] = "Medicina 2027",
        ["quantidade"] = "3",
    };

    private static DadosDaRegra Regra(string assunto = "Oi, {nome}", string template = "Sua parcela de {valor} vence em {vencimento}.") =>
        new(GatilhoDaRegua.Vencimento, 3, assunto, template, true, false);

    [Fact]
    public void Troca_as_variaveis_conhecidas() =>
        TemplateDeNotificacao
            .Renderizar("Oi, {nome}! São {valor} até {vencimento}.", Valores)
            .ShouldBe("Oi, J&#250;lia! São R$ 350,00 até 10/09/2026.");

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
    public void Template_correto_nao_tem_desconhecidas() => TemplateDeNotificacao.Desconhecidas("{nome} {valor} {link}").ShouldBeEmpty();

    [Fact]
    public void A_gravacao_recusa_variavel_desconhecida()
    {
        var resultado = new DadosDaReguaValidator().Validar(new DadosDaRegua([Regra(template: "Vence em {vencimeto}.")]));

        resultado.Falhou.ShouldBeTrue();
        resultado.Erros.ShouldContain(erro => erro.Mensagem.Contains("{vencimeto}", StringComparison.Ordinal));
    }

    [Fact]
    public void A_gravacao_aceita_a_regua_padrao() =>
        new DadosDaReguaValidator()
            .Validar(
                new DadosDaRegua([
                    .. RegraDeNotificacao
                        .Padrao()
                        .Select(r => new DadosDaRegra(r.Gatilho, r.DiasDeDeslocamento, r.Assunto, r.Template, r.Ativa, false)),
                ])
            )
            .Sucesso.ShouldBeTrue();

    [Fact]
    public void A_gravacao_recusa_dois_degraus_com_o_mesmo_gatilho_e_deslocamento()
    {
        var resultado = new DadosDaReguaValidator().Validar(new DadosDaRegua([Regra(), Regra(assunto: "Outro")]));

        resultado.Falhou.ShouldBeTrue();
    }
}
