using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Services;
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

    private static DadosDoItem Mensalidade => new(TipoDeCobranca.Mensalidade, null, 840_000, 24, 10, new DateOnly(2026, 3, 1));

    [Fact]
    public async Task Primeira_geracao_marca_a_grade_inteira_de_cada_item_ativo()
    {
        // Arrange
        var plano = Plano(true, Mensalidade, new DadosDoItem(TipoDeCobranca.Adesao, null, 50_000, 1, 5, new DateOnly(2026, 3, 1)));
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
    public async Task Plano_em_montagem_nao_gera_parcela()
    {
        // Act
        var resultado = await Servico.Gerar(VinculoId, Plano(false, Mensalidade), Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.sem_plano_vigente");
        await _parcelas.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }
}
