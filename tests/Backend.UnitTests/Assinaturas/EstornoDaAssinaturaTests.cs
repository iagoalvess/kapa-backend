using Backend.Business.Abstractions;
using Backend.Business.Admin.Models;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Pagamentos.Models;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Assinaturas;

/// <summary>O reembolso integral devolve o que foi pago, inclusive quando houve desconto de cupom.</summary>
public sealed class EstornoDaAssinaturaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(MeioDePagamento.Pix)]
    [InlineData(MeioDePagamento.Cartao)]
    public async Task Cupom_nao_faz_o_reembolso_voltar_ao_preco_cheio(MeioDePagamento meio)
    {
        var agora = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var assinatura = new Assinatura
        {
            PlanoId = Guid.NewGuid(),
            Meio = meio,
            CupomId = Guid.NewGuid(),
            IdExterno = meio == MeioDePagamento.Cartao ? "recorrencia" : null,
        };
        assinatura.ConfirmarPagamento(agora.AddDays(-3), CicloDeCobranca.Mensal);
        var cobranca = CobrancaDaAssinatura.Abrir(assinatura.Id, assinatura.PlanoId, MotivoDaCobranca.Ciclo, meio, 17900);
        cobranca.Pagar("pagamento", 8950, agora.AddDays(-3));
        var provedor = Substitute.For<IProvedorDeAssinatura>();
        provedor.Cancelar(Arg.Any<string>(), Ct).Returns(Result.Ok());
        provedor.Estornar(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<Guid>(), Ct).Returns(Result.Ok());
        var servico = new EstornoDaAssinatura(Substitute.For<IAssinaturaRepository>(), Substitute.For<IFormaturaRepository>(), provedor);

        var resultado = await servico.Devolver(assinatura, cobranca, ModoDeEstorno.Integral, agora, Ct);

        resultado.Valor.ShouldBe(8950);
        await provedor.Received(1).Estornar("pagamento", 8950, cobranca.Id, Ct);
        await provedor.DidNotReceive().Estornar("pagamento", 17900, cobranca.Id, Ct);
        cobranca.Situacao.ShouldBe(SituacaoDaCobrancaDoPlano.Estornada);
    }
}
