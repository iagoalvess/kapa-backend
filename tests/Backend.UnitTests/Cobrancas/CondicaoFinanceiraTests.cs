using Backend.Business.Adesoes.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>
/// A conta do desconto individual (Sprint 54): percentual e valor fixo, a faixa-teto do grupo (P3), o alcance e o
/// líquido que o snapshot congela.
/// </summary>
public sealed class CondicaoFinanceiraTests
{
    private static readonly Guid PlanoId = Guid.CreateVersion7();

    private static readonly DateOnly Hoje = DataUtils.Hoje();

    private static ItemDeCobranca Pacote(string nome, long valor, string? grupo = null) =>
        ItemDeCobranca.NovoPacote(PlanoId, new DadosDoPacote(new DadosDoItem(TipoDeCobranca.Festa, nome, valor, 10, 10, Hoje.AddMonths(1)), grupo));

    private static readonly ItemDeCobranca Festa10 = Pacote("Festa 10", 500_000, "Festa");
    private static readonly ItemDeCobranca Festa15 = Pacote("Festa 15", 600_000, "Festa");
    private static readonly ItemDeCobranca Festa5 = Pacote("Festa 5", 300_000, "Festa");
    private static readonly ItemDeCobranca Fotos = Pacote("Fotos", 100_000);
    private static readonly ItemDeCobranca[] Catalogo = [Festa5, Festa10, Festa15, Fotos];

    private static CondicaoFinanceira Condicao(params BeneficioDaCondicao[] beneficios) =>
        CondicaoFinanceira.DoVinculo(Guid.CreateVersion7(), new DadosDaCondicao(beneficios), Guid.CreateVersion7());

    [Fact]
    public void Isencao_ate_a_faixa_cobre_o_preco_dela_e_deixa_a_diferenca_da_faixa_maior()
    {
        var condicao = Condicao(new BeneficioDaCondicao(Festa10.Id, BeneficioDaCondicao.Inteiro, null));

        condicao.DescontoDe(Festa10, Catalogo).ShouldBe(500_000);
        condicao.DescontoDe(Festa15, Catalogo).ShouldBe(500_000);
        condicao.DescontoDe(Festa5, Catalogo).ShouldBe(300_000);
        condicao.DescontoDe(Fotos, Catalogo).ShouldBe(0);
    }

    [Fact]
    public void Percentual_vale_sobre_a_faixa_menor_e_para_no_teto_da_apontada()
    {
        var condicao = Condicao(new BeneficioDaCondicao(Festa10.Id, 2_000, null));

        condicao.DescontoDe(Festa5, Catalogo).ShouldBe(60_000);
        condicao.DescontoDe(Festa10, Catalogo).ShouldBe(100_000);
        condicao.DescontoDe(Festa15, Catalogo).ShouldBe(100_000);
    }

    [Fact]
    public void Valor_fixo_nunca_passa_do_preco_do_pacote()
    {
        var condicao = Condicao(new BeneficioDaCondicao(Fotos.Id, null, 150_000));

        condicao.DescontoDe(Fotos, Catalogo).ShouldBe(100_000);
    }

    [Fact]
    public void Sem_condicao_nao_ha_desconto_e_a_condicao_so_alcanca_os_pacotes_dela()
    {
        var condicao = Condicao(new BeneficioDaCondicao(Fotos.Id, BeneficioDaCondicao.Inteiro, null));

        CondicaoFinanceira.Descontos(null, [Fotos], Catalogo).ShouldBeEmpty();
        CondicaoFinanceira.Descontos(condicao, [Fotos, Festa10], Catalogo).ShouldBe(new Dictionary<Guid, long> { [Fotos.Id] = 100_000 });
    }

    [Fact]
    public void Snapshot_congela_o_liquido_e_o_pacote_isento_nao_vira_parcela()
    {
        var plano = new PlanoDeCobranca { Nome = "Plano" };
        plano.Itens.AddRange([Festa10, Fotos]);
        var descontos = new Dictionary<Guid, long> { [Festa10.Id] = 500_000, [Fotos.Id] = 20_000 };

        var snapshot = SnapshotDoPlano.De(plano, [Festa10, Fotos], Hoje, descontos);

        snapshot.TotalEmCentavos.ShouldBe(80_000);
        snapshot.Itens.ShouldHaveSingleItem().ValorEmCentavos.ShouldBe(80_000);
        snapshot.Cesta!.Select(p => p.ValorEmCentavos).ShouldBe([500_000, 100_000]);
        snapshot.Descontos!.Select(d => d.DescontoEmCentavos).ShouldBe([500_000, 20_000]);
        SnapshotDoPlano.De(plano, [Fotos], Hoje).Descontos.ShouldBeNull();
    }
}
