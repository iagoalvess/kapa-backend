using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Services;
using Backend.Business.Common.Datas;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>A geração marca só o que falta, nunca salva sozinha e só gera pelo plano vigente.</summary>
public sealed class GeracaoDeParcelasServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid VinculoId = Guid.CreateVersion7();

    private readonly IParcelaRepository _parcelas = Substitute.For<IParcelaRepository>();

    private GeracaoDeParcelasService Servico => new(_parcelas);

    private static PlanoDeCobranca Plano(bool vigente, params DadosDoItem[] itens)
    {
        var plano = new PlanoDeCobranca { Nome = "Plano 2027" };

        foreach (var dados in itens)
            plano.Itens.Add(ItemDeCobranca.Novo(plano.Id, dados));

        if (vigente)
            plano.Vigorar(DateTime.UtcNow);

        return plano;
    }

    /// <summary>Mês que vem: a grade cheia, sem nada vencido — o caso de quem adere na publicação.</summary>
    private static DadosDoItem Mensalidade => new(TipoDeCobranca.Mensalidade, null, 840_000, 24, 10, DataUtils.Hoje().AddMonths(1));

    [Fact]
    public async Task Primeira_geracao_marca_a_grade_inteira_de_cada_item_ativo()
    {
        // Arrange
        var plano = Plano(true, Mensalidade, new DadosDoItem(TipoDeCobranca.Adesao, null, 50_000, 1, 5, DataUtils.Hoje().AddMonths(1)));
        _parcelas.ListarNumerosGerados(VinculoId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var resultado = await Servico.Gerar(VinculoId, plano, Ct);

        // Assert
        resultado.Valor.ShouldBe(25);
        await _parcelas
            .Received(1)
            .Adicionar(
                Arg.Is<IReadOnlyList<Parcela>>(parcelas =>
                    parcelas.Count == 25 && parcelas.All(p => p.VinculoId == VinculoId) && parcelas.Sum(p => p.ValorOriginalEmCentavos) == 890_000
                ),
                Ct
            );
        plano.Itens.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Gerar_de_novo_para_o_mesmo_vinculo_nao_marca_nada()
    {
        // Arrange
        var plano = Plano(true, Mensalidade);
        _parcelas.ListarNumerosGerados(VinculoId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([.. Enumerable.Range(1, 24)]);

        // Act
        var resultado = await Servico.Gerar(VinculoId, plano, Ct);

        // Assert
        resultado.Valor.ShouldBe(0);
        await _parcelas.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    [Fact]
    public async Task Geracao_interrompida_completa_so_os_numeros_que_faltam()
    {
        // Arrange
        var plano = Plano(true, Mensalidade);
        _parcelas.ListarNumerosGerados(VinculoId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([.. Enumerable.Range(1, 20)]);

        // Act
        await Servico.Gerar(VinculoId, plano, Ct);

        // Assert
        await _parcelas
            .Received(1)
            .Adicionar(Arg.Is<IReadOnlyList<Parcela>>(parcelas => parcelas.Select(p => p.Numero).SequenceEqual(new[] { 21, 22, 23, 24 })), Ct);
    }

    [Fact]
    public async Task Rateio_grava_a_grade_do_item_para_cada_vinculo()
    {
        // Arrange
        Guid[] vinculos = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
        var item = ItemDeCobranca.Novo(
            Guid.CreateVersion7(),
            new DadosDoItem(TipoDeCobranca.Avulsa, "Rateio do buffet", 10_000, 2, 10, new DateOnly(2027, 2, 1))
        );

        // Act
        var resultado = await Servico.GerarDoItem(vinculos, item, Ct);

        // Assert
        resultado.Valor.ShouldBe(6);
        await _parcelas
            .Received(1)
            .Adicionar(
                Arg.Is<IReadOnlyList<Parcela>>(parcelas =>
                    parcelas.All(p => p.ItemDeCobrancaId == item.Id)
                    && parcelas.Select(p => p.VinculoId).Distinct().Count() == 3
                    && parcelas.Sum(p => p.ValorOriginalEmCentavos) == 30_000
                ),
                Ct
            );
    }

    [Fact]
    public async Task Rateio_sem_ninguem_para_alcancar_nao_marca_nada()
    {
        // Act
        var resultado = await Servico.GerarDoItem([], ItemDeCobranca.Novo(Guid.CreateVersion7(), Mensalidade), Ct);

        // Assert
        resultado.Valor.ShouldBe(0);
        await _parcelas.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    [Fact]
    public async Task Rateio_nao_toca_nos_outros_itens_do_plano()
    {
        // Arrange
        var plano = Plano(true, Mensalidade);
        var rateio = ItemDeCobranca.Novo(
            plano.Id,
            new DadosDoItem(TipoDeCobranca.Avulsa, "Rateio do buffet", 10_000, 1, 10, new DateOnly(2027, 2, 1))
        );
        plano.Itens.Add(rateio);

        // Act
        await Servico.GerarDoItem([VinculoId], rateio, Ct);

        // Assert
        await _parcelas
            .Received(1)
            .Adicionar(Arg.Is<IReadOnlyList<Parcela>>(parcelas => parcelas.Count == 1 && parcelas[0].ItemDeCobrancaId == rateio.Id), Ct);
        await _parcelas.DidNotReceiveWithAnyArgs().ListarNumerosGerados(default, default, Ct);
    }

    /// <summary>
    /// O ponto crítico da Sprint 20: o item opcional não entra na adesão de ninguém.
    /// </summary>
    /// <remarks>
    /// Esquecer o filtro de <c>Opcional</c> em <c>ItensAtivos</c> faria a turma inteira passar a
    /// dever um convite extra que ninguém pediu — e ela só descobriria no extrato.
    /// </remarks>
    [Fact]
    public async Task Item_opcional_nao_entra_na_geracao_da_adesao()
    {
        // Arrange — um plano com os dois tipos de item.
        var plano = Plano(true, Mensalidade);
        plano.Itens.Add(
            ItemDeCobranca.NovoOpcional(
                plano.Id,
                new DadosDoOpcional(new DadosDoItem(TipoDeCobranca.ConviteExtra, "Convite extra", 18_000, 2, 10, DataUtils.Hoje().AddMonths(1)))
            )
        );
        _parcelas.ListarNumerosGerados(VinculoId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var resultado = await Servico.Gerar(VinculoId, plano, Ct);

        // Assert — só as 24 da mensalidade.
        resultado.Valor.ShouldBe(24);
        var opcional = plano.Itens.Single(item => item.Opcional).Id;
        await _parcelas.Received(1).Adicionar(Arg.Is<IReadOnlyList<Parcela>>(parcelas => parcelas.All(p => p.ItemDeCobrancaId != opcional)), Ct);
    }

    [Fact]
    public async Task Plano_em_montagem_nao_gera_parcela()
    {
        // Act
        var resultado = await Servico.Gerar(VinculoId, Plano(false, Mensalidade), Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.sem_plano_vigente");
        await _parcelas.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }
}
