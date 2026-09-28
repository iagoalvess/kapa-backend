using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>
/// O item opcional: a marca que o tira da adesão, e a reserva que decide quem leva a última unidade.
/// </summary>
/// <remarks>
/// A reserva aqui é a conferência que dá a mensagem certa. A garantia de não vender a mais é o
/// <c>CHECK</c> do banco, e ela é conferida em <c>PedidoEndpointsTests</c>, contra o Postgres.
/// </remarks>
public sealed class OpcionalTests
{
    private static DateOnly Hoje => DataUtils.Hoje();

    private static DateTime Agora => DateTime.UtcNow;

    private static DadosDoItem Convite => new(TipoDeCobranca.ConviteExtra, "Convite extra", 18_000, 2, 10, Hoje.AddMonths(1));

    private static ItemDeCobranca Item(
        int? estoque = null,
        int? limite = null,
        DateOnly? abertura = null,
        DateOnly? prazo = null,
        Guid? itemDaFestaId = null
    ) =>
        ItemDeCobranca.NovoOpcional(
            Guid.CreateVersion7(),
            new DadosDoOpcional(Convite, limite, prazo, estoque, abertura is { } dia ? DataUtils.InicioDoDiaEmUtc(dia) : null, itemDaFestaId)
        );

    /// <summary>O ponto crítico da sprint: o item opcional não pode entrar na adesão de todo mundo.</summary>
    [Fact]
    public void Item_opcional_fica_fora_dos_itens_ativos_do_plano()
    {
        // Arrange
        var plano = new PlanoDeCobranca { Nome = "Plano 2027" };
        var mensalidade = ItemDeCobranca.Novo(plano.Id, new DadosDoItem(TipoDeCobranca.Mensalidade, null, 840_000, 24, 10, Hoje.AddMonths(1)));

        // Act
        plano.Itens.Add(mensalidade);
        plano.Itens.Add(Item());

        // Assert
        plano.ItensAtivos.ShouldHaveSingleItem().ShouldBe(mensalidade);
        plano.ItensOpcionais.ShouldHaveSingleItem().Opcional.ShouldBeTrue();
        plano.DadosDosItensAtivos().ShouldHaveSingleItem().Tipo.ShouldBe(TipoDeCobranca.Mensalidade);
    }

    [Fact]
    public void Item_sem_estoque_nunca_reserva_nada()
    {
        var item = Item();

        item.Reservar(5, 5, Agora).Sucesso.ShouldBeTrue();

        item.Estoque.ShouldBeNull();
        item.Disponivel.ShouldBeNull();
        item.Reservados.ShouldBe(5);
    }

    [Fact]
    public void Reservar_alem_do_estoque_devolve_estoque_esgotado()
    {
        var item = Item(estoque: 10);
        item.Reservar(8, 8, Agora);

        var resultado = item.Reservar(3, 11, Agora);

        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.estoque_esgotado");
        item.Reservados.ShouldBe(8);
        item.Disponivel.ShouldBe(2);
    }

    [Fact]
    public void Pedir_antes_da_abertura_devolve_venda_nao_aberta()
    {
        var item = Item(abertura: Hoje.AddDays(3));

        item.Reservar(1, 1, Agora).PrimeiroErro.Codigo.ShouldBe("cobranca.venda_nao_aberta");
        item.AbertoAPedido(Agora).ShouldBeFalse();
        item.AbertoAPedido(Agora.AddDays(3)).ShouldBeTrue();
    }

    [Fact]
    public void Pedir_depois_do_prazo_devolve_fora_do_prazo()
    {
        var item = Item(prazo: Hoje.AddDays(-1));

        item.Reservar(1, 1, Agora).PrimeiroErro.Codigo.ShouldBe("cobranca.pedido_fora_do_prazo");
        item.AbertoAPedido(Agora).ShouldBeFalse();
    }

    /// <summary>A cota olha a quantidade final do pedido, não o delta: dois mais dois já são quatro.</summary>
    [Fact]
    public void Passar_da_cota_por_formando_devolve_limite_excedido()
    {
        var item = Item(limite: 4);

        item.Reservar(2, 2, Agora).Sucesso.ShouldBeTrue();
        item.Reservar(3, 5, Agora).PrimeiroErro.Codigo.ShouldBe("cobranca.limite_do_item_excedido");
        item.Reservar(2, 4, Agora).Sucesso.ShouldBeTrue();
    }

    /// <summary>Devolver nunca passa por conferência: cancelar não pode falhar porque a venda fechou.</summary>
    [Fact]
    public void Devolver_ao_estoque_ignora_prazo_abertura_e_cota()
    {
        var item = Item(estoque: 10, limite: 1, prazo: Hoje.AddDays(-1));
        item.Reservar(-0, 0, Agora);

        var devolucao = item.Reservar(-3, 0, Agora);

        devolucao.Sucesso.ShouldBeTrue();
        item.Reservados.ShouldBe(0);
    }

    /// <summary>P8: a comissão cancela pedido a pedido até caber, e o sistema não afrouxa o teto.</summary>
    [Fact]
    public void Reduzir_o_estoque_abaixo_do_reservado_e_recusado()
    {
        var item = Item(estoque: 100);
        item.Reservar(80, 80, Agora);

        var resultado = item.AplicarDadosDoOpcional(new DadosDoOpcional(Convite, Estoque: 60));

        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.estoque_menor_que_reservado");
        item.Estoque.ShouldBe(100);
    }

    [Fact]
    public void Reduzir_o_estoque_ate_o_reservado_e_aceito()
    {
        var item = Item(estoque: 100);
        item.Reservar(80, 80, Agora);

        item.AplicarDadosDoOpcional(new DadosDoOpcional(Convite, Estoque: 80)).Sucesso.ShouldBeTrue();

        item.Estoque.ShouldBe(80);
        item.Disponivel.ShouldBe(0);
    }

    [Fact]
    public void Item_encerrado_nao_aceita_pedido_novo()
    {
        var item = Item();
        item.Encerrar(Hoje);

        item.Reservar(1, 1, Agora).PrimeiroErro.Codigo.ShouldBe("cobranca.item_encerrado");
        item.AbertoAPedido(Agora).ShouldBeFalse();
    }
}
