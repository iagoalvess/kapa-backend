using Backend.Business.Abstractions;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Festa.Services;
using Backend.Business.Festa.Validators;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Festa;

/// <summary>
/// As regras da festa que não dependem do banco: o estado do item sai das despesas, o custo troca o
/// previsto pelo contratado, o item com despesa não se exclui e a turma sem item recebe os sugeridos.
/// </summary>
public sealed class ItemDaFestaServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IItemDaFestaRepository _itens = Substitute.For<IItemDaFestaRepository>();
    private readonly IDespesaRepository _despesas = Substitute.For<IDespesaRepository>();
    private readonly IDocumentoRepository _documentos = Substitute.For<IDocumentoRepository>();
    private readonly ICaixaRepository _caixa = Substitute.For<ICaixaRepository>();
    private readonly IPropostaRepository _propostas = Substitute.For<IPropostaRepository>();
    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public ItemDaFestaServiceTests()
    {
        _itens.ExisteAlgum(Arg.Any<CancellationToken>()).Returns(true);
    }

    private ItemDaFestaService Servico =>
        new(
            _itens,
            _despesas,
            _documentos,
            _caixa,
            _propostas,
            _perfis,
            new DadosDoItemDaFestaValidator(),
            _unitOfWork,
            NullLogger<ItemDaFestaService>.Instance
        );

    [Fact]
    public async Task Turma_sem_nenhum_item_recebe_os_seis_sugeridos()
    {
        // Arrange
        _itens.ExisteAlgum(Arg.Any<CancellationToken>()).Returns(false);
        _itens.Listar(Arg.Any<CancellationToken>()).Returns([]);

        // Act
        await Servico.Listar(Ct);

        // Assert
        await _itens.Received(1).Adicionar(Arg.Is<IReadOnlyList<ItemDaFesta>>(lista => lista.Count == 6), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Turma_que_ja_tem_item_nao_recebe_sugeridos()
    {
        // Arrange
        _itens.Listar(Arg.Any<CancellationToken>()).Returns([Resumo()]);

        // Act
        await Servico.Listar(Ct);

        // Assert
        await _itens.DidNotReceive().Adicionar(Arg.Any<IReadOnlyList<ItemDaFesta>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Item_com_despesa_lancada_nao_e_excluido()
    {
        // Arrange
        var item = ItemDaFesta.Sugerido("Buffet", CategoriaDeDespesa.Buffet, 1);
        _itens.ObterParaEdicao(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        _despesas.ExisteDoItemDaFesta(item.Id, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var resultado = await Servico.Excluir(item.Id, Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("festa.item_em_uso");
        _itens.DidNotReceive().Remover(Arg.Any<ItemDaFesta>());
    }

    [Fact]
    public async Task Item_sem_despesa_e_excluido()
    {
        // Arrange
        var item = ItemDaFesta.Sugerido("Banda", CategoriaDeDespesa.Banda, 4);
        _itens.ObterParaEdicao(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        _despesas.ExisteDoItemDaFesta(item.Id, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var resultado = await Servico.Excluir(item.Id, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        _itens.Received(1).Remover(item);
    }

    [Fact]
    public async Task Item_cancelado_nao_aceita_correcao()
    {
        // Arrange
        var item = ItemDaFesta.Sugerido("Banda", CategoriaDeDespesa.Banda, 4);
        item.Cancelar();
        _itens.ObterParaEdicao(item.Id, Arg.Any<CancellationToken>()).Returns(item);

        // Act
        var resultado = await Servico.Atualizar(item.Id, Dados(), Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("festa.item_cancelado");
    }

    [Fact]
    public async Task Contrato_que_a_turma_nao_pode_abrir_e_recusado()
    {
        // Arrange
        var documentoDaComissao = Guid.CreateVersion7();
        _documentos.Obter(documentoDaComissao, PapelNaFormatura.Formando, Arg.Any<CancellationToken>()).Returns((DocumentoResumo?)null);

        // Act
        var resultado = await Servico.Criar(Dados() with { DocumentoId = documentoDaComissao }, Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("festa.documento_nao_encontrado");
        await _itens.DidNotReceive().Adicionar(Arg.Any<IReadOnlyList<ItemDaFesta>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Contrato_visivel_para_a_turma_e_aceito()
    {
        // Arrange
        var contrato = Guid.CreateVersion7();
        _documentos
            .Obter(contrato, PapelNaFormatura.Formando, Arg.Any<CancellationToken>())
            .Returns(
                new DocumentoResumo(
                    contrato,
                    "Contrato do buffet",
                    CategoriaDeDocumento.Contrato,
                    Visibilidade.Turma,
                    1,
                    "contrato.pdf",
                    "application/pdf",
                    1024,
                    DateTime.UtcNow,
                    null
                )
            );
        _itens.Obter(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Resumo());

        // Act
        var resultado = await Servico.Criar(Dados() with { DocumentoId = contrato }, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _itens.Received(1).Adicionar(Arg.Any<IReadOnlyList<ItemDaFesta>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_meta_soma_o_custo_dos_itens_de_pe_e_le_o_arrecadado_do_caixa()
    {
        // Arrange
        _itens
            .Listar(Arg.Any<CancellationToken>())
            .Returns([
                Resumo(previsto: 60_000_00),
                Resumo(previsto: 40_000_00, contratado: 35_000_00, pago: 10_000_00, despesas: 3),
                Resumo(previsto: 9_000_00, cancelado: true),
            ]);
        _caixa.Arrecadado(Arg.Any<CancellationToken>()).Returns(70_000_00);

        // Act
        var meta = (await Servico.ObterMeta(Ct)).Valor;

        // Assert
        meta.CustoEmCentavos.ShouldBe(95_000_00);
        meta.PagoEmCentavos.ShouldBe(10_000_00);
        meta.ArrecadadoEmCentavos.ShouldBe(70_000_00);
        meta.FaltaArrecadarEmCentavos.ShouldBe(25_000_00);
        meta.Itens.ShouldBe(2);
        meta.AContratar.ShouldBe(1);
    }

    [Fact]
    public async Task Arrecadar_mais_que_o_custo_nao_vira_falta_negativa()
    {
        // Arrange
        _itens.Listar(Arg.Any<CancellationToken>()).Returns([Resumo(previsto: 10_000_00)]);
        _caixa.Arrecadado(Arg.Any<CancellationToken>()).Returns(30_000_00);

        // Act
        var meta = (await Servico.ObterMeta(Ct)).Valor;

        // Assert
        meta.FaltaArrecadarEmCentavos.ShouldBe(0);
    }

    [Theory]
    [InlineData(0, 0, 0, false, EstadoDoItem.AContratar)]
    [InlineData(2, 30_000_00, 10_000_00, false, EstadoDoItem.Contratado)]
    [InlineData(2, 30_000_00, 30_000_00, false, EstadoDoItem.Pago)]
    [InlineData(2, 30_000_00, 30_000_00, true, EstadoDoItem.Cancelado)]
    public void O_estado_do_item_sai_das_despesas_vinculadas(int despesas, long contratado, long pago, bool cancelado, EstadoDoItem esperado)
    {
        // Act
        var item = Resumo(contratado: contratado, pago: pago, despesas: despesas, cancelado: cancelado);

        // Assert
        item.Estado.ShouldBe(esperado);
    }

    [Fact]
    public void O_contratado_toma_o_lugar_do_previsto_assim_que_existe_despesa()
    {
        // Act
        var previsto = Resumo(previsto: 60_000_00);
        var contratado = Resumo(previsto: 60_000_00, contratado: 54_000_00, despesas: 1);

        // Assert
        previsto.CustoEmCentavos.ShouldBe(60_000_00);
        contratado.CustoEmCentavos.ShouldBe(54_000_00);
    }

    [Fact]
    public void Item_por_formando_custa_o_preco_vezes_a_expectativa()
    {
        // Act
        var item = Resumo(previsto: 350_00, quantidade: 40, rateio: TipoDeRateio.PorFormando);

        // Assert
        item.CustoEmCentavos.ShouldBe(14_000_00);
    }

    [Fact]
    public void Item_da_turma_ignora_a_quantidade_informada()
    {
        // Act
        var item = ItemDaFesta.Novo(Dados() with { QuantidadeEstimada = 80 }, 1);

        // Assert
        item.QuantidadeEstimada.ShouldBe(1);
    }

    [Fact]
    public void Cancelar_duas_vezes_e_conflito_e_reativar_desfaz()
    {
        // Arrange
        var item = ItemDaFesta.Sugerido("Banda", CategoriaDeDespesa.Banda, 4);

        // Act
        item.Cancelar().Sucesso.ShouldBeTrue();

        // Assert
        item.Cancelar().Erros[0].Codigo.ShouldBe("festa.item_cancelado");
        item.Reativar().Sucesso.ShouldBeTrue();
        item.Cancelado.ShouldBeFalse();
    }

    private static DadosDoItemDaFesta Dados() => new("Buffet", CategoriaDeDespesa.Buffet, "Open bar de 4 horas", null, TipoDeRateio.Turma, 60_000_00);

    private static ItemDaFestaResumo Resumo(
        long previsto = 0,
        long contratado = 0,
        long pago = 0,
        int despesas = 0,
        bool cancelado = false,
        int quantidade = 1,
        TipoDeRateio rateio = TipoDeRateio.Turma
    ) =>
        new(
            Guid.CreateVersion7(),
            "Buffet",
            CategoriaDeDespesa.Buffet,
            null,
            null,
            null,
            rateio,
            previsto,
            quantidade,
            contratado,
            pago,
            despesas,
            0,
            cancelado,
            1
        );
}
