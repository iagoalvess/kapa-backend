using Backend.Business.Abstractions;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Festa.Services;
using Backend.Business.Festa.Validators;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Business.Usuarios.Interfaces;
using Backend.Business.Usuarios.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Festa;

/// <summary>
/// As regras das propostas e dos votos: a disputa só existe enquanto o item está "a contratar", o
/// voto é um por formando por item, e trocar de ideia atualiza a linha em vez de criar outra.
/// </summary>
public sealed class PropostaServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Formatura = Guid.CreateVersion7();
    private static readonly Guid Usuario = Guid.CreateVersion7();
    private static readonly Guid Vinculo = Guid.CreateVersion7();

    private readonly IPropostaRepository _propostas = Substitute.For<IPropostaRepository>();
    private readonly IItemDaFestaRepository _itens = Substitute.For<IItemDaFestaRepository>();
    private readonly IDespesaRepository _despesas = Substitute.For<IDespesaRepository>();
    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly ItemDaFesta _item = ItemDaFesta.Sugerido("Banda", CategoriaDeDespesa.Banda, 1);

    public PropostaServiceTests()
    {
        _perfis
            .ObterTitular(Formatura, Usuario, Arg.Any<CancellationToken>())
            .Returns(new MembroDoPerfil(Vinculo, Usuario, "Ana", "ana@kapa.dev", "Formando"));

        _usuarios.ObterDetalhe(Usuario, Arg.Any<CancellationToken>()).Returns(Conta(emailConfirmado: true));

        _itens.ObterParaEdicao(_item.Id, Arg.Any<CancellationToken>()).Returns(_item);
        _despesas.ExisteDoItemDaFesta(_item.Id, Arg.Any<CancellationToken>()).Returns(false);
    }

    private PropostaService Servico =>
        new(_propostas, _itens, _despesas, _perfis, _usuarios, new DadosDaPropostaValidator(), _unitOfWork, NullLogger<PropostaService>.Instance);

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
    public async Task Item_cancelado_nao_recebe_voto()
    {
        // Arrange
        var proposta = Proposta();
        _item.Cancelar();

        // Act
        var resultado = await Servico.Votar(proposta.Id, Formatura, Usuario, Ct);

        // Assert
        resultado.Erros[0].Codigo.ShouldBe("festa.disputa_encerrada");
        await _propostas.DidNotReceive().AdicionarVoto(Arg.Any<VotoNaProposta>(), Arg.Any<CancellationToken>());
    }

    /// <summary>O link da turma aceita qualquer conta: sem e-mail provado, cada e-mail inventado seria um voto.</summary>
    [Fact]
    public async Task Sem_email_confirmado_nao_vota()
    {
        // Arrange
        var proposta = Proposta();
        _usuarios.ObterDetalhe(Usuario, Arg.Any<CancellationToken>()).Returns(Conta(emailConfirmado: false));

        // Act
        var resultado = await Servico.Votar(proposta.Id, Formatura, Usuario, Ct);

        // Assert
        resultado.Erros[0].Codigo.ShouldBe("festa.email_nao_confirmado");
        await _propostas.DidNotReceive().AdicionarVoto(Arg.Any<VotoNaProposta>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Primeiro_voto_do_formando_cria_a_linha()
    {
        // Arrange
        var proposta = Proposta();

        // Act
        var resultado = await Servico.Votar(proposta.Id, Formatura, Usuario, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _propostas
            .Received(1)
            .AdicionarVoto(Arg.Is<VotoNaProposta>(v => v.PropostaId == proposta.Id && v.VinculoId == Vinculo), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Trocar_de_proposta_atualiza_o_voto_em_vez_de_criar_outro()
    {
        // Arrange
        var primeira = Proposta();
        var segunda = Proposta();
        var voto = VotoNaProposta.Novo(Vinculo, primeira);
        _propostas.ObterVoto(Vinculo, _item.Id, Arg.Any<CancellationToken>()).Returns(voto);

        // Act
        var resultado = await Servico.Votar(segunda.Id, Formatura, Usuario, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        voto.PropostaId.ShouldBe(segunda.Id);
        await _propostas.DidNotReceive().AdicionarVoto(Arg.Any<VotoNaProposta>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Voto_nao_atravessa_item()
    {
        // Arrange
        var voto = VotoNaProposta.Novo(Vinculo, Proposta());
        var deOutroItem = PropostaDoItem.Nova(Guid.CreateVersion7(), new DadosDaProposta("Banda Z", 700_000, null));

        // Act
        var resultado = voto.Trocar(deOutroItem);

        // Assert
        resultado.Erros[0].Codigo.ShouldBe("festa.proposta_de_outro_item");
    }

    [Fact]
    public async Task Desvotar_sem_voto_nao_grava_nada()
    {
        // Act
        var resultado = await Servico.Desvotar(_item.Id, Formatura, Usuario, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _unitOfWork.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Uma proposta do item de teste, já devolvida pelo repositório.</summary>
    private PropostaDoItem Proposta()
    {
        var proposta = PropostaDoItem.Nova(_item.Id, new DadosDaProposta("Banda X", 800_000, null));

        _propostas.ObterParaEdicao(proposta.Id, Arg.Any<CancellationToken>()).Returns(proposta);

        return proposta;
    }

    private static UsuarioDetalhe Conta(bool emailConfirmado) =>
        new(Usuario, "Ana", "ana@kapa.dev", emailConfirmado, true, [], DateTime.UtcNow, DateTime.UtcNow);
}
