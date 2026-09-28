using Backend.Business.Festa.Models;
using Backend.Business.Financeiro.Models;
using Shouldly;

namespace Backend.UnitTests.Festa;

/// <summary>
/// O degrau do meio da decisão 11 da Sprint 20: com opcionais aberto, o item <c>PorFormando</c> custa
/// o que a turma de fato vendeu — não a estimativa de quem chutou quantos comprariam.
/// </summary>
/// <remarks>
/// É a decisão 3 da Sprint 17 aplicada mais uma vez: o previsto cede lugar ao real assim que existe
/// real. E é o que fecha o circuito da meta — os R$ 14.000 da foto deixam de subir o custo sem nunca
/// subir o arrecadado.
/// </remarks>
public sealed class CustoComOpcionalTests
{
    /// <summary>"Fotografia — R$ 350,00 × 40 estimados": o estado de toda turma antes dos opcionais.</summary>
    private static ItemDaFestaResumo Foto(
        long? precoDeVenda = null,
        int pedidos = 0,
        long contratado = 0,
        int despesas = 0,
        bool cancelado = false
    ) =>
        new(
            Guid.CreateVersion7(),
            "Fotografia",
            CategoriaDeDespesa.Fotografia,
            null,
            null,
            null,
            TipoDeRateio.PorFormando,
            35_000,
            40,
            contratado,
            0,
            despesas,
            0,
            cancelado,
            1,
            precoDeVenda,
            pedidos
        );

    [Fact]
    public void Sem_opcional_o_custo_e_preco_estimado_vezes_quantidade_estimada()
    {
        var item = Foto();

        item.TemOpcional.ShouldBeFalse();
        item.CustoEmCentavos.ShouldBe(14_000_00);
    }

    /// <summary>Opcionais ligado, nada contratado: 37 pediram, e é por 37 que a festa paga.</summary>
    [Fact]
    public void Com_opcional_e_sem_despesa_o_custo_e_preco_de_venda_vezes_pedidos()
    {
        var item = Foto(precoDeVenda: 35_000, pedidos: 37);

        item.TemOpcional.ShouldBeTrue();
        item.CustoEmCentavos.ShouldBe(12_950_00);
    }

    /// <summary>Ninguém pediu ainda: o custo é zero, e não a estimativa — a turma abriu a venda e a resposta foi nenhuma.</summary>
    [Fact]
    public void Com_opcional_e_nenhum_pedido_o_custo_e_zero()
    {
        Foto(precoDeVenda: 35_000).CustoEmCentavos.ShouldBe(0);
    }

    /// <summary>Contratado manda em tudo: a despesa é o número real, e ela vem depois dos opcionais.</summary>
    [Fact]
    public void Item_contratado_ignora_o_opcional()
    {
        var item = Foto(precoDeVenda: 35_000, pedidos: 37, contratado: 13_500_00, despesas: 1);

        item.CustoEmCentavos.ShouldBe(13_500_00);
        item.Estado.ShouldBe(EstadoDoItem.Contratado);
    }

    [Fact]
    public void Item_cancelado_pesa_zero_mesmo_com_opcional()
    {
        Foto(precoDeVenda: 35_000, pedidos: 37, cancelado: true).CustoEmCentavos.ShouldBe(0);
    }
}
