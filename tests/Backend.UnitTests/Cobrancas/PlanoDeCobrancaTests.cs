using Backend.Business.Cobrancas.Models;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>As regras que o plano, o item e a parcela guardam sozinhos, sem banco.</summary>
public sealed class PlanoDeCobrancaTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 14);

    private static DadosDoItem Dados(TipoDeCobranca tipo = TipoDeCobranca.Mensalidade, long valor = 840_000) =>
        new(tipo, null, valor, 24, 10, new DateOnly(2026, 3, 1));

    private static PlanoDeCobranca ComItens(params TipoDeCobranca[] tipos)
    {
        var plano = new PlanoDeCobranca { Nome = "Plano 2027" };

        foreach (var tipo in tipos)
            plano.Itens.Add(ItemDeCobranca.Novo(plano.Id, Dados(tipo)));

        return plano;
    }

    [Fact]
    public void Segunda_adesao_no_plano_e_recusada()
    {
        // Arrange
        var plano = ComItens(TipoDeCobranca.Adesao, TipoDeCobranca.Mensalidade);

        // Act
        var resultado = plano.AceitaItem(TipoDeCobranca.Adesao);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.adesao_duplicada");
    }

    [Fact]
    public void Adesao_alterada_nao_conflita_consigo_mesma_e_encerrada_nao_conta()
    {
        // Arrange
        var plano = ComItens(TipoDeCobranca.Adesao);
        var adesao = plano.Itens[0];

        // Act
        var alterando = plano.AceitaItem(TipoDeCobranca.Adesao, exceto: adesao);
        adesao.Encerrar(Hoje);
        var depoisDeEncerrar = plano.AceitaItem(TipoDeCobranca.Adesao);

        // Assert
        alterando.Sucesso.ShouldBeTrue();
        depoisDeEncerrar.Sucesso.ShouldBeTrue();
    }

    [Fact]
    public void Plano_sem_item_ativo_nao_entra_em_vigor()
    {
        // Arrange
        var plano = ComItens(TipoDeCobranca.Mensalidade);
        plano.Itens[0].Encerrar(Hoje);

        // Act
        var resultado = plano.Vigorar(DateTime.UtcNow);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.plano_sem_itens");
        plano.Status.ShouldBe(StatusDoPlano.Rascunho);
    }

    [Fact]
    public void Vigorar_marca_a_data_e_nao_vigora_duas_vezes()
    {
        // Arrange
        var plano = ComItens(TipoDeCobranca.Mensalidade);
        var agora = DateTime.UtcNow;

        // Act
        var primeira = plano.Vigorar(agora);
        var segunda = plano.Vigorar(agora.AddMinutes(1));

        // Assert
        primeira.Sucesso.ShouldBeTrue();
        plano.VigenteDesde.ShouldBe(agora);
        segunda.PrimeiroErro.Codigo.ShouldBe("cobranca.plano_ja_vigente");
    }

    [Fact]
    public void Item_grava_o_primeiro_mes_no_dia_1_e_so_numero_dia_mes_e_tipo_mudam_a_grade()
    {
        // Arrange
        var item = ItemDeCobranca.Novo(Guid.CreateVersion7(), Dados() with { PrimeiroMes = new DateOnly(2026, 3, 17) });

        // Assert
        item.PrimeiroMes.ShouldBe(new DateOnly(2026, 3, 1));
        item.MudaAGrade(Dados(valor: 960_000) with { Descricao = "Mensalidade nova" }).ShouldBeFalse();
        item.MudaAGrade(Dados() with { NumeroDeParcelas = 12 }).ShouldBeTrue();
        item.MudaAGrade(Dados() with { DiaDeVencimento = 5 }).ShouldBeTrue();
        item.MudaAGrade(Dados() with { PrimeiroMes = new DateOnly(2026, 4, 1) }).ShouldBeTrue();
        item.MudaAGrade(Dados(TipoDeCobranca.Rifa)).ShouldBeTrue();
    }

    [Fact]
    public void Parcela_vencida_nao_repactua_e_a_que_vence_hoje_repactua()
    {
        // Arrange
        var itemId = Guid.CreateVersion7();
        var vencida = Parcela.Nova(Guid.CreateVersion7(), itemId, new ParcelaPrevista(1, Hoje.AddDays(-1), 35_000));
        var venceHoje = Parcela.Nova(Guid.CreateVersion7(), itemId, new ParcelaPrevista(2, Hoje, 35_000));

        // Act
        var mudouVencida = vencida.Repactuar(40_000, Hoje);
        var mudouHoje = venceHoje.Repactuar(40_000, Hoje);

        // Assert
        mudouVencida.ShouldBeFalse();
        vencida.ValorOriginalEmCentavos.ShouldBe(35_000);
        vencida.StatusEm(Hoje).ShouldBe(StatusDaParcela.Vencida);
        mudouHoje.ShouldBeTrue();
        venceHoje.ValorOriginalEmCentavos.ShouldBe(40_000);
    }

    [Fact]
    public void Parcela_cancelada_nao_repactua_nem_cancela_de_novo()
    {
        // Arrange
        var parcela = Parcela.Nova(Guid.CreateVersion7(), Guid.CreateVersion7(), new ParcelaPrevista(3, Hoje.AddMonths(1), 35_000));

        // Act
        var cancelou = parcela.Cancelar(Hoje);

        // Assert
        cancelou.ShouldBeTrue();
        parcela.Status.ShouldBe(StatusDaParcela.Cancelada);
        parcela.Cancelar(Hoje).ShouldBeFalse();
        parcela.Repactuar(40_000, Hoje).ShouldBeFalse();
        parcela.ValorOriginalEmCentavos.ShouldBe(35_000);
    }
}
