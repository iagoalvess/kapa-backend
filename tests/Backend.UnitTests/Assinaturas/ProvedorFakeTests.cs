using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Settings;
using Backend.Data.Provedores;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Backend.UnitTests.Assinaturas;

/// <summary>
/// A verificação HMAC do provedor fake — o mesmo papel que o PSP real vai cumprir.
/// </summary>
public sealed class ProvedorFakeTests
{
    private const string Segredo = "segredo-de-teste";

    private const string Corpo = """{"id":"evt_1","tipo":"pagamento.confirmado","assinaturaId":"0199407e-0000-7000-8000-00000000abcd"}""";

    private static ProvedorFake Provedor(string segredo = Segredo) => new(Options.Create(new AssinaturaSettings { SegredoDoWebhook = segredo }));

    [Fact]
    public void Corpo_assinado_com_o_segredo_e_lido()
    {
        // Act
        var resultado = Provedor().LerWebhook(Corpo, ProvedorFake.Assinar(Corpo, Segredo));

        // Assert
        resultado.Valor.ShouldBe(
            new EventoDoProvedor("evt_1", TiposDeEvento.PagamentoConfirmado, Guid.Parse("0199407e-0000-7000-8000-00000000abcd"), null)
        );
    }

    [Fact]
    public void Corpo_alterado_depois_de_assinado_e_recusado()
    {
        var assinatura = ProvedorFake.Assinar(Corpo, Segredo);

        var resultado = Provedor().LerWebhook(Corpo.Replace("evt_1", "evt_2", StringComparison.Ordinal), assinatura);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("webhook.assinatura_invalida");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void Assinatura_ausente_ou_errada_e_recusada(string? assinatura)
    {
        Provedor().LerWebhook(Corpo, assinatura).Erros.ShouldHaveSingleItem().Codigo.ShouldBe("webhook.assinatura_invalida");
    }

    /// <summary>Sem segredo configurado, nem a assinatura "certa" passa: é recusar tudo, não aceitar tudo.</summary>
    [Fact]
    public void Sem_segredo_configurado_recusa_tudo()
    {
        Provedor(segredo: "").LerWebhook(Corpo, ProvedorFake.Assinar(Corpo, "")).Falhou.ShouldBeTrue();
    }

    [Fact]
    public async Task Pagamento_aprovado_fica_visivel_para_a_conciliacao()
    {
        var ct = TestContext.Current.CancellationToken;
        var provedor = Provedor();
        var assinaturaId = Guid.CreateVersion7();
        var pedido = new PedidoDeCheckout(assinaturaId, "completo", "Completo", 34990, CicloDeCobranca.Mensal, "https://app/retorno");
        var sessao = (await provedor.CriarCheckout(pedido, ct)).Valor;

        (await provedor.ConsultarPagamento(assinaturaId, sessao.IdExterno, ct)).Valor.ShouldBeNull();

        var webhook = provedor.Pagar(sessao.IdExterno, aprovado: true)!;

        var consultado = (await provedor.ConsultarPagamento(assinaturaId, sessao.IdExterno, ct)).Valor;
        consultado.ShouldNotBeNull();
        provedor.LerWebhook(webhook.Corpo, webhook.Assinatura).Valor.ShouldBe(consultado);
    }
}
