using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Busca.Interfaces;
using Backend.Business.Busca.Models;
using Backend.Business.Busca.Services;
using Backend.Business.Formaturas.Interfaces;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Busca;

/// <summary>
/// O piso da busca: a partir de quando ela vale uma ida ao banco.
/// </summary>
/// <remarks>
/// A tela consulta a cada tecla, com um atraso curto. Sem o piso, "a" varreria a turma inteira para
/// devolver uma lista que não diz nada — e faria isso uma vez por letra digitada.
/// </remarks>
public sealed class BuscaServiceTests
{
    private static readonly Guid Turma = Guid.CreateVersion7();

    private static readonly Guid Usuario = Guid.CreateVersion7();

    private static readonly Plano Premium = new() { Modulos = [Modulo.Membros, Modulo.Mural] };

    private readonly IBuscaRepository _repositorio = Substitute.For<IBuscaRepository>();

    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();

    private readonly IAssinaturaRepository _assinaturas = Substitute.For<IAssinaturaRepository>();

    public BuscaServiceTests() => _assinaturas.ObterPlanoDosModulosDeTodasAsFormaturas(Turma, Arg.Any<CancellationToken>()).Returns(Premium);

    /// <summary>Quem busca, com o papel e os módulos do plano.</summary>
    private static QuemBusca Quem(string papel) =>
        Arg.Is<QuemBusca>(quem => quem.FormaturaId == Turma && quem.Papel == papel && quem.Modulos.SequenceEqual(Premium.Modulos));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("an")]
    [InlineData(" an ")]
    public async Task Termo_curto_nao_chega_ao_banco(string? termo)
    {
        var service = new BuscaService(_repositorio, _vinculos, _assinaturas);

        var resultado = await service.Buscar(Turma, Usuario, termo, TestContext.Current.CancellationToken);

        resultado.Sucesso.ShouldBeTrue();
        resultado.Valor.ShouldSatisfyAllConditions(
            busca => busca.Membros.ShouldBeEmpty(),
            busca => busca.Despesas.ShouldBeEmpty(),
            busca => busca.Fornecedores.ShouldBeEmpty(),
            busca => busca.Avisos.ShouldBeEmpty(),
            busca => busca.Documentos.ShouldBeEmpty()
        );
        await _repositorio.DidNotReceiveWithAnyArgs().Buscar(default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Termo_vai_ao_banco_sem_os_espacos_de_quem_digitou()
    {
        _vinculos.ObterPapelAtivo(Usuario, Turma, Arg.Any<CancellationToken>()).Returns("Presidente");
        _repositorio.Buscar(Arg.Any<QuemBusca>(), "ana", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(BuscaNaTurma.Nada);

        var service = new BuscaService(_repositorio, _vinculos, _assinaturas);

        var resultado = await service.Buscar(Turma, Usuario, "  ana  ", TestContext.Current.CancellationToken);

        resultado.Sucesso.ShouldBeTrue();
        await _repositorio.Received(1).Buscar(Quem("Presidente"), "ana", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>O papel vem do vínculo: o rebaixado deixa de ver membros na hora, não quando o token vence.</summary>
    [Fact]
    public async Task Busca_com_o_papel_gravado_e_nao_com_o_da_claim()
    {
        _vinculos.ObterPapelAtivo(Usuario, Turma, Arg.Any<CancellationToken>()).Returns("Formando");
        _repositorio.Buscar(Arg.Any<QuemBusca>(), "ana", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(BuscaNaTurma.Nada);

        var service = new BuscaService(_repositorio, _vinculos, _assinaturas);

        await service.Buscar(Turma, Usuario, "ana", TestContext.Current.CancellationToken);

        await _repositorio.Received(1).Buscar(Quem("Formando"), "ana", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
