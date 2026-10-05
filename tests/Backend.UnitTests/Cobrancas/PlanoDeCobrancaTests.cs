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

    /// <summary>"Festa 10" e "Festa 15" juntos é erro de clique: uma faixa por grupo (Sprint 47, D32).</summary>
    [Fact]
    public void Cesta_aceita_uma_faixa_por_grupo_e_qualquer_pacote_avulso()
    {
        // Arrange
        var plano = new PlanoDeCobranca { Nome = "Plano 2027" };
        var festa10 = Pacote(plano, "Festa", 10);
        var festa15 = Pacote(plano, "Festa", 15);
        var foto = Pacote(plano, null, 0);
        plano.Itens.AddRange([festa10, festa15, foto]);

        // Act
        var valida = plano.MontarCesta([foto.Id, festa15.Id]);
        var duasFaixas = plano.MontarCesta([festa10.Id, festa15.Id]);
        var repetida = plano.MontarCesta([foto.Id, foto.Id]);
        var inexistente = plano.MontarCesta([Guid.CreateVersion7()]);

        // Assert
        valida.Valor.ShouldBe([festa15, foto]);
        duasFaixas.PrimeiroErro.Codigo.ShouldBe("cobranca.faixa_invalida");
        repetida.PrimeiroErro.Codigo.ShouldBe("cobranca.cesta_duplicada");
        inexistente.PrimeiroErro.Codigo.ShouldBe("cobranca.pacote_invalido");
    }

    /// <summary>
    /// O rateio da assembleia não é pacote, e é pontual (Sprint 48, D39): quem adere depois dele não o deve — nem o
    /// lançamento avulso de outro formando entra no catálogo.
    /// </summary>
    [Fact]
    public void Rateio_e_lancamento_ficam_fora_do_catalogo_e_da_adesao()
    {
        // Arrange
        var plano = new PlanoDeCobranca { Nome = "Plano 2027" };
        var foto = Pacote(plano, null, 0);
        var rateio = ItemDeCobranca.Novo(
            plano.Id,
            new DadosDoItem(TipoDeCobranca.Avulsa, "Formatura extra", 10_000, 1, 10, Hoje),
            "assembleia de 12/10"
        );
        var multa = ItemDeCobranca.NovoLancamento(
            plano.Id,
            Guid.CreateVersion7(),
            new DadosDoItem(TipoDeCobranca.Avulsa, "Multa da mesa", 5_000, 1, 10, Hoje)
        );
        plano.Itens.AddRange([foto, rateio, multa]);

        // Assert
        plano.Pacotes().ShouldBe([foto]);
        plano.ItensDoFormando([]).ShouldBeEmpty();
        plano.ItensDoFormando([foto]).ShouldBe([foto]);
    }

    private static ItemDeCobranca Pacote(PlanoDeCobranca plano, string? grupo, int convites) =>
        ItemDeCobranca.NovoPacote(
            plano.Id,
            new DadosDoPacote(new DadosDoItem(TipoDeCobranca.Festa, $"{convites} pessoas", 100_000, 2, 10, Hoje.AddMonths(1)), grupo, convites)
        );

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
