using Backend.Business.Formaturas.Models;
using Backend.Business.Loja.Models;
using Shouldly;

namespace Backend.UnitTests.Loja;

/// <summary>
/// O cancelamento da compra (Sprint 38): a conta do estorno, a lista a devolver e o pedido do comprador.
/// </summary>
public sealed class CancelamentoTests
{
    private static CompraDeConvite Paga(long pago)
    {
        var compra = LojaTests.Compra();
        compra.Pagar(pago, DateTime.UtcNow, null, reservouDeNovo: false).ShouldBeTrue();

        return compra;
    }

    [Fact]
    public void Estorno_e_o_pago_por_convite_e_o_centavo_que_sobra_vai_no_ultimo()
    {
        var compra = Paga(10_001);

        compra.Cancelar(1).ShouldBe(5_000);
        compra.LugaresValendo.ShouldBe(1);
        compra.Status.ShouldBe(StatusDaCompra.ADevolver);
        compra.Cancelar(1).ShouldBe(5_001);

        compra.ValorEstornadoEmCentavos.ShouldBe(10_001);
        compra.ValorADevolverEmCentavos.ShouldBe(10_001);
        compra.LugaresValendo.ShouldBe(0);
    }

    [Fact]
    public void Cancelar_tudo_de_uma_vez_estorna_exatamente_o_que_entrou()
    {
        var compra = Paga(10_001);

        compra.Cancelar(2).ShouldBe(10_001);
    }

    [Fact]
    public void Nao_se_cancela_alem_dos_lugares_nem_compra_pendente()
    {
        var pendente = LojaTests.Compra();
        var paga = Paga(40_000);
        paga.Cancelar(2);

        Should.Throw<InvalidOperationException>(() => pendente.Cancelar(1));
        Should.Throw<InvalidOperationException>(() => paga.Cancelar(1));
    }

    [Fact]
    public void Pagamento_que_chega_depois_do_cancelamento_nao_muda_a_compra()
    {
        var compra = Paga(40_000);
        compra.Cancelar(1);

        compra.Pagar(40_000, DateTime.UtcNow, null, reservouDeNovo: false).ShouldBeFalse();
        compra.Status.ShouldBe(StatusDaCompra.ADevolver);
    }

    [Fact]
    public void Devolver_a_compra_cancelada_zera_o_que_falta_e_nao_estorna_de_novo()
    {
        var compra = Paga(40_000);
        compra.Cancelar(1);
        var comprovante = Guid.CreateVersion7();

        compra.Devolver(DateTime.UtcNow, comprovante).ShouldBe(0);

        compra.Status.ShouldBe(StatusDaCompra.Devolvida);
        compra.ValorADevolverEmCentavos.ShouldBe(0);
        compra.ValorEstornadoEmCentavos.ShouldBe(20_000);
        compra.ComprovanteDaDevolucaoId.ShouldBe(comprovante);
        compra.LugaresValendo.ShouldBe(1);
    }

    [Fact]
    public void Compra_paga_sem_lugar_entra_na_lista_e_estorna_ao_ser_devolvida()
    {
        var tardia = LojaTests.Compra();
        typeof(CompraDeConvite).GetProperty(nameof(CompraDeConvite.Status))!.SetValue(tardia, StatusDaCompra.Expirada);

        tardia.Pagar(40_000, DateTime.UtcNow, null, reservouDeNovo: false).ShouldBeTrue();

        tardia.Status.ShouldBe(StatusDaCompra.ADevolver);
        tardia.LugaresValendo.ShouldBe(0);
        tardia.ValorADevolverEmCentavos.ShouldBe(40_000);
        tardia.Devolver(DateTime.UtcNow, Guid.CreateVersion7()).ShouldBe(40_000);
        tardia.ValorEstornadoEmCentavos.ShouldBe(40_000);
    }

    [Fact]
    public void Devolver_fora_da_lista_e_erro_de_quem_chama()
    {
        Should.Throw<InvalidOperationException>(() => Paga(40_000).Devolver(DateTime.UtcNow, Guid.CreateVersion7()));
    }

    [Fact]
    public void Pedido_responde_uma_vez_so()
    {
        var pedido = new PedidoDeCancelamento(Guid.CreateVersion7(), [Guid.Empty, Guid.Empty], "  ", DateTime.UtcNow);

        pedido.ConviteIds.Length.ShouldBe(1);
        pedido.Motivo.ShouldBeNull();
        pedido.Responder(aprovado: false, Guid.CreateVersion7(), " não dá ", DateTime.UtcNow).ShouldBeTrue();
        pedido.Responder(aprovado: true, Guid.CreateVersion7(), null, DateTime.UtcNow).ShouldBeFalse();
        pedido.Status.ShouldBe(StatusDoPedidoDeCancelamento.Recusado);
        pedido.MotivoDaResposta.ShouldBe("não dá");
    }

    [Fact]
    public void Pendencias_descrevem_so_o_que_falta_no_singular_e_no_plural()
    {
        var pendencias = new PendenciasDaTurma(2, 0, 0, 0, 1, 0, 0, 2);

        pendencias.Alguma.ShouldBeTrue();
        pendencias.Descrever().ShouldBe(["2 parcelas em aberto", "1 compra da loja a devolver", "2 valores a devolver a formandos"]);
        new PendenciasDaTurma(0, 0, 0, 0, 0, 0, 0, 0).Alguma.ShouldBeFalse();
    }
}
