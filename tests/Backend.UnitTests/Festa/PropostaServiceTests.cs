using Backend.Business.Abstractions;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Festa.Services;
using Backend.Business.Festa.Validators;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Festa;

/// <summary>
/// As regras das propostas: elas só mudam enquanto o item está "a contratar".
/// </summary>
public sealed class PropostaServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IPropostaRepository _propostas = Substitute.For<IPropostaRepository>();
    private readonly IItemDaFestaRepository _itens = Substitute.For<IItemDaFestaRepository>();
    private readonly IDespesaRepository _despesas = Substitute.For<IDespesaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly ItemDaFesta _item = ItemDaFesta.Sugerido("Banda", CategoriaDeDespesa.Banda, 1);

    public PropostaServiceTests()
    {
        _itens.ObterParaEdicao(_item.Id, Arg.Any<CancellationToken>()).Returns(_item);
        _despesas.ExisteDoItemDaFesta(_item.Id, Arg.Any<CancellationToken>()).Returns(false);
    }

    private PropostaService Servico =>
        new(_propostas, _itens, _despesas, new DadosDaPropostaValidator(), _unitOfWork, NullLogger<PropostaService>.Instance);

    [Fact]
    public async Task Proposta_entra_no_item_a_contratar()
    {
        // Act
        var resultado = await Servico.Criar(_item.Id, new DadosDaProposta("Banda X", 800_000, null), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _propostas.Received(1).Adicionar(Arg.Is<PropostaDoItem>(p => p.Titulo == "Banda X"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Item_ja_contratado_nao_recebe_proposta()
    {
        // Arrange
        _despesas.ExisteDoItemDaFesta(_item.Id, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var resultado = await Servico.Criar(_item.Id, new DadosDaProposta("Banda X", 800_000, null), Ct);

        // Assert
        resultado.Erros[0].Codigo.ShouldBe("festa.disputa_encerrada");
        await _propostas.DidNotReceive().Adicionar(Arg.Any<PropostaDoItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Proposta_de_item_cancelado_nao_e_excluida()
    {
        // Arrange
        var proposta = PropostaDoItem.Nova(_item.Id, new DadosDaProposta("Banda X", 800_000, null));
        _propostas.ObterParaEdicao(proposta.Id, Arg.Any<CancellationToken>()).Returns(proposta);
        _item.Cancelar();

        // Act
        var resultado = await Servico.Excluir(proposta.Id, Ct);

        // Assert
        resultado.Erros[0].Codigo.ShouldBe("festa.disputa_encerrada");
        _propostas.DidNotReceive().Remover(Arg.Any<PropostaDoItem>());
    }
}
