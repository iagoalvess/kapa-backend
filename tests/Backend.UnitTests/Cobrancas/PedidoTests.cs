using Backend.Business.Cobrancas.Models;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>
/// O cancelamento do pedido: o que volta ao estoque, e o que o dinheiro já pago segura (P9).
/// </summary>
public sealed class PedidoTests
{
    private static Pedido Novo(int quantidade = 2) => Pedido.Novo(Guid.CreateVersion7(), Guid.CreateVersion7(), quantidade);

    [Fact]
    public void Cancelar_sem_nada_pago_devolve_tudo_e_encerra_o_pedido()
    {
        var pedido = Novo(quantidade: 2);

        var devolvidas = pedido.Cancelar(quantidadeMantida: 0);

        devolvidas.ShouldBe(2);
        pedido.Status.ShouldBe(StatusDoPedido.Cancelado);
        pedido.CanceladoEm.ShouldNotBeNull();
    }

    /// <summary>P9: pagou uma de duas parcelas de dois convites — fica com um, e um volta ao estoque.</summary>
    [Fact]
    public void Cancelar_com_uma_unidade_paga_encolhe_o_pedido_em_vez_de_apaga_lo()
    {
        var pedido = Novo(quantidade: 2);

        var devolvidas = pedido.Cancelar(quantidadeMantida: 1);

        devolvidas.ShouldBe(1);
        pedido.Quantidade.ShouldBe(1);
        pedido.Status.ShouldBe(StatusDoPedido.Confirmado);
        pedido.CanceladoEm.ShouldBeNull();
    }

    /// <summary>Cancelar de novo não devolve outra vez — é o que o clique repetido precisa.</summary>
    [Fact]
    public void Cancelar_de_novo_nao_devolve_estoque_outra_vez()
    {
        var pedido = Novo(quantidade: 3);
        pedido.Cancelar(quantidadeMantida: 0);

        pedido.Cancelar(quantidadeMantida: 0).ShouldBe(0);
        pedido.Status.ShouldBe(StatusDoPedido.Cancelado);
    }

    /// <summary>Pedir de novo o que se cancelou reaproveita a linha: o índice único não distingue os dois.</summary>
    [Fact]
    public void Ajustar_reabre_um_pedido_cancelado()
    {
        var pedido = Novo(quantidade: 1);
        pedido.Cancelar(quantidadeMantida: 0);

        pedido.Ajustar(4);

        pedido.Confirmado.ShouldBeTrue();
        pedido.Quantidade.ShouldBe(4);
        pedido.CanceladoEm.ShouldBeNull();
    }
}
