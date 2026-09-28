using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Services;
using Backend.Business.Cobrancas.Validators;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Festa.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>
/// O pedido do formando: a grade que ele passa a dever, a numeração que continua e as guardas da
/// segunda porta que cria parcela.
/// </summary>
/// <remarks>
/// A concorrência não se prova aqui — ela é uma trava de linha e um <c>CHECK</c>, e está em
/// <c>PedidoEndpointsTests</c>, contra o Postgres. O que se prova aqui é a conta.
/// </remarks>
public sealed class PedidoServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid FormaturaId = Guid.CreateVersion7();

    private static readonly Guid UsuarioId = Guid.CreateVersion7();

    private static readonly Guid VinculoId = Guid.CreateVersion7();

    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();

    private readonly IParcelaRepository _parcelas = Substitute.For<IParcelaRepository>();

    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();

    private readonly IAdesaoRepository _adesoes = Substitute.For<IAdesaoRepository>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly IMesaRepository _mesas = Substitute.For<IMesaRepository>();

    private readonly List<Parcela> _gravadas = [];

    public PedidoServiceTests()
    {
        _perfis
            .ObterMembro(FormaturaId, UsuarioId, Arg.Any<CancellationToken>())
            .Returns(new MembroDoPerfil(VinculoId, UsuarioId, "Ana Souza", "ana@turma.dev", PapelNaFormatura.Formando));
        _perfis
            .ObterTitular(FormaturaId, UsuarioId, Arg.Any<CancellationToken>())
            .Returns(new MembroDoPerfil(VinculoId, UsuarioId, "Ana Souza", "ana@turma.dev", PapelNaFormatura.Formando));
        _adesoes.JaAderiuAlgumaVez(VinculoId, Arg.Any<CancellationToken>()).Returns(true);
        _parcelas.ListarNumerosGerados(VinculoId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => [.. _gravadas.Select(p => p.Numero)]);
        _parcelas
            .Adicionar(Arg.Any<IReadOnlyList<Parcela>>(), Arg.Any<CancellationToken>())
            .Returns(chamada =>
            {
                _gravadas.AddRange(chamada.Arg<IReadOnlyList<Parcela>>());

                return Task.CompletedTask;
            });

        // A transação de verdade é do Postgres; aqui ela é o passa-adiante que deixa o service rodar.
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result<PedidoResumo>>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result<PedidoResumo>>>>()(Ct));
    }

    private PedidoService Servico =>
        new(
            _pedidos,
            _parcelas,
            _perfis,
            _adesoes,
            new DadosDoPedidoValidator(),
            Substitute.For<IEventoRepository>(),
            Emissao(),
            new DonosDeMesa(_mesas, NullLogger<DonosDeMesa>.Instance),
            _unitOfWork,
            NullLogger<PedidoService>.Instance
        );

    /// <summary>A emissão de convites sem convite nenhum: o pedido é de outro item, ou ainda não foi quitado.</summary>
    private static EmissaoDeConvites Emissao()
    {
        var convites = Substitute.For<IConviteDoEventoRepository>();
        convites.TravarDoPedido(default, default).ReturnsForAnyArgs([]);

        return new EmissaoDeConvites(
            convites,
            Substitute.For<IEventoDaTurmaRepository>(),
            Substitute.For<IFormaturaRepository>(),
            Substitute.For<IFormaturaAtual>(),
            NullLogger<EmissaoDeConvites>.Instance
        );
    }

    private static DateOnly Hoje => DataUtils.Hoje();

    /// <summary>"Convite extra, R$ 180, em 2×, todo dia 10" — o exemplo da decisão 2.</summary>
    private static ItemDeCobranca Convite(int? estoque = null, int? limite = null) =>
        ItemDeCobranca.NovoOpcional(
            Guid.CreateVersion7(),
            new DadosDoOpcional(
                new DadosDoItem(TipoDeCobranca.ConviteExtra, "Convite extra", 18_000, 2, 10, Hoje.AddMonths(1)),
                limite,
                Estoque: estoque
            )
        );

    private void Preparar(ItemDeCobranca item, Pedido? pedido = null)
    {
        _pedidos.TravarItem(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        _pedidos.ObterParaEdicao(VinculoId, item.Id, Arg.Any<CancellationToken>()).Returns(pedido);
        _pedidos
            .ListarParcelasParaEdicao(Arg.Any<Pedido>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<Parcela>)[.. _gravadas.OrderBy(p => p.Numero)]);
        _pedidos
            .Obter(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(chamada => new PedidoResumo(
                chamada.Arg<Guid>(),
                item.Id,
                item.Tipo,
                item.Descricao,
                UsuarioId,
                "Ana Souza",
                pedido?.Quantidade ?? 1,
                pedido?.Parcelas ?? 1,
                item.ValorEmCentavos,
                0,
                0,
                StatusDoPedido.Confirmado,
                DateTime.UtcNow,
                null
            ));
    }

    /// <summary>O critério da sprint: quantidade 3 num item de R$ 180 em 2× são duas parcelas de R$ 270.</summary>
    [Fact]
    public async Task Pedido_multiplica_o_preco_unitario_pela_quantidade_e_fecha_no_centavo()
    {
        // Arrange
        var item = Convite();
        Preparar(item);

        // Act
        var resultado = await Servico.Pedir(FormaturaId, UsuarioId, new DadosDoPedido(item.Id, 3, Parcelas: 2), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        _gravadas.Count.ShouldBe(2);
        _gravadas.ShouldAllBe(parcela => parcela.ValorOriginalEmCentavos == 27_000);
        _gravadas.Sum(parcela => parcela.ValorOriginalEmCentavos).ShouldBe(54_000);
        _gravadas.Select(parcela => parcela.Numero).ShouldBe([1, 2]);
        item.Reservados.ShouldBe(3);
    }

    /// <summary>O número do item é teto: sem escolha, o pedido é à vista — uma parcela com o total.</summary>
    [Fact]
    public async Task Pedido_sem_escolha_de_parcelas_e_a_vista()
    {
        // Arrange
        var item = Convite();
        Preparar(item);

        // Act
        var resultado = await Servico.Pedir(FormaturaId, UsuarioId, new DadosDoPedido(item.Id, 3), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        _gravadas.Count.ShouldBe(1);
        _gravadas[0].ValorOriginalEmCentavos.ShouldBe(54_000);
    }

    [Fact]
    public async Task Parcelas_acima_do_teto_do_item_sao_recusadas_sem_reservar()
    {
        // Arrange
        var item = Convite();
        Preparar(item);

        // Act
        var resultado = await Servico.Pedir(FormaturaId, UsuarioId, new DadosDoPedido(item.Id, 1, Parcelas: 3), Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.parcelas_acima_do_teto");
        _gravadas.ShouldBeEmpty();
        item.Reservados.ShouldBe(0);
    }

    /// <summary>
    /// Aumentar gera só o que falta, com a divisão que o pedido já tem — e a numeração continua de
    /// onde a anterior parou.
    /// </summary>
    [Fact]
    public async Task Aumentar_a_quantidade_gera_so_as_parcelas_que_faltam()
    {
        // Arrange
        var item = Convite();
        var pedido = Pedido.Novo(VinculoId, item.Id, 1, parcelas: 2);
        Preparar(item, pedido);
        item.Reservar(1, 1, DateTime.UtcNow);
        await Servico.Pedir(FormaturaId, UsuarioId, new DadosDoPedido(item.Id, 1), Ct);
        _gravadas.Clear();
        _gravadas.AddRange([
            Parcela.Nova(VinculoId, item.Id, new ParcelaPrevista(1, Hoje.AddMonths(1), 9_000)),
            Parcela.Nova(VinculoId, item.Id, new ParcelaPrevista(2, Hoje.AddMonths(2), 9_000)),
        ]);

        // Act
        await Servico.Pedir(FormaturaId, UsuarioId, new DadosDoPedido(item.Id, 3), Ct);

        // Assert — duas parcelas novas, cobrindo as duas unidades a mais, nos números 3 e 4.
        _gravadas.Count.ShouldBe(4);
        _gravadas.Select(parcela => parcela.Numero).Order().ShouldBe([1, 2, 3, 4]);
        _gravadas.Where(parcela => parcela.Numero > 2).Sum(parcela => parcela.ValorOriginalEmCentavos).ShouldBe(36_000);
        pedido.Quantidade.ShouldBe(3);
    }

    /// <summary>A quantidade é absoluta: repetir o mesmo pedido reserva delta zero.</summary>
    [Fact]
    public async Task Repetir_a_mesma_quantidade_nao_reserva_nem_gera_parcela()
    {
        // Arrange
        var item = Convite();
        var pedido = Pedido.Novo(VinculoId, item.Id, 2);
        Preparar(item, pedido);
        item.Reservar(2, 2, DateTime.UtcNow);

        // Act
        await Servico.Pedir(FormaturaId, UsuarioId, new DadosDoPedido(item.Id, 2), Ct);

        // Assert
        _gravadas.ShouldBeEmpty();
        item.Reservados.ShouldBe(2);
    }

    /// <summary>Diminuir o pedido de mesa solta as mesas que passaram do direito, e só elas (Sprint 27, decisão 6).</summary>
    [Fact]
    public async Task Diminuir_o_pedido_de_mesa_solta_a_mesa_excedente()
    {
        // Arrange
        var item = ItemDeCobranca.NovoOpcional(
            Guid.CreateVersion7(),
            new DadosDoOpcional(new DadosDoItem(TipoDeCobranca.Mesa, "Mesa de 10", 200_000, 1, 10, Hoje.AddMonths(1)), null)
        );
        var pedido = Pedido.Novo(VinculoId, item.Id, 2);
        Preparar(item, pedido);
        item.Reservar(2, 2, DateTime.UtcNow);
        var mesas = new[] { Mesa.Nova(new DadosDaMesa("Mesa 1", 10, null, false)), Mesa.Nova(new DadosDaMesa("Mesa 2", 10, null, false)) };
        foreach (var mesa in mesas)
            mesa.DefinirDono(VinculoId);
        _mesas.Compradas(VinculoId, Arg.Any<CancellationToken>()).Returns(1);
        _mesas.ListarDoDonoParaEdicao(VinculoId, Arg.Any<CancellationToken>()).Returns(mesas);

        // Act
        var resultado = await Servico.Pedir(FormaturaId, UsuarioId, new DadosDoPedido(item.Id, 1), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        mesas.Count(mesa => mesa.VinculoId is null).ShouldBe(1);
    }

    [Fact]
    public async Task Diminuir_com_parcela_paga_devolve_pedido_com_parcela_paga()
    {
        // Arrange
        var item = Convite();
        var pedido = Pedido.Novo(VinculoId, item.Id, 2);
        Preparar(item, pedido);
        item.Reservar(2, 2, DateTime.UtcNow);
        var paga = Parcela.Nova(VinculoId, item.Id, new ParcelaPrevista(1, Hoje.AddMonths(1), 18_000));
        paga.Pagar(18_000, Hoje, 18_000);
        _gravadas.Add(paga);

        // Act
        var resultado = await Servico.Pedir(FormaturaId, UsuarioId, new DadosDoPedido(item.Id, 1), Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.pedido_com_parcela_paga");
        item.Reservados.ShouldBe(2);
    }

    [Fact]
    public async Task Quem_nao_aderiu_nao_pede()
    {
        // Arrange
        var item = Convite();
        Preparar(item);
        _adesoes.JaAderiuAlgumaVez(VinculoId, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var resultado = await Servico.Pedir(FormaturaId, UsuarioId, new DadosDoPedido(item.Id, 1), Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.pedido_sem_adesao");
        _gravadas.ShouldBeEmpty();
    }

    /// <summary>Item do plano da turma não se pede: ele já está no extrato de todo mundo.</summary>
    [Fact]
    public async Task Item_que_nao_e_opcional_e_recusado()
    {
        // Arrange
        var item = ItemDeCobranca.Novo(Guid.CreateVersion7(), new DadosDoItem(TipoDeCobranca.Mensalidade, null, 840_000, 24, 10, Hoje.AddMonths(1)));
        Preparar(item);

        // Act
        var resultado = await Servico.Pedir(FormaturaId, UsuarioId, new DadosDoPedido(item.Id, 1), Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.item_nao_e_opcional");
    }

    [Fact]
    public async Task Pedir_alem_do_estoque_devolve_estoque_esgotado_e_nao_grava_parcela()
    {
        // Arrange
        var item = Convite(estoque: 2);
        Preparar(item);

        // Act
        var resultado = await Servico.Pedir(FormaturaId, UsuarioId, new DadosDoPedido(item.Id, 3), Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.estoque_esgotado");
        _gravadas.ShouldBeEmpty();
        item.Reservados.ShouldBe(0);
    }
}
