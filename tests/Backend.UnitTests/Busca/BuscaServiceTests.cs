using Backend.Business.Busca.Interfaces;
using Backend.Business.Busca.Models;
using Backend.Business.Busca.Services;
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
    private static readonly QuemBusca Presidente = new(Guid.CreateVersion7(), "Presidente");

    private readonly IBuscaRepository _repositorio = Substitute.For<IBuscaRepository>();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("an")]
    [InlineData(" an ")]
    public async Task Termo_curto_nao_chega_ao_banco(string? termo)
    {
        var service = new BuscaService(_repositorio);

        var resultado = await service.Buscar(Presidente, termo, TestContext.Current.CancellationToken);

        resultado.Sucesso.ShouldBeTrue();
        resultado.Valor.Vazia.ShouldBeTrue();
        await _repositorio.DidNotReceiveWithAnyArgs().Buscar(default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Termo_vai_ao_banco_sem_os_espacos_de_quem_digitou()
    {
        _repositorio.Buscar(Presidente, "ana", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(BuscaNaTurma.Nada);

        var service = new BuscaService(_repositorio);

        var resultado = await service.Buscar(Presidente, "  ana  ", TestContext.Current.CancellationToken);

        resultado.Sucesso.ShouldBeTrue();
        await _repositorio.Received(1).Buscar(Presidente, "ana", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
