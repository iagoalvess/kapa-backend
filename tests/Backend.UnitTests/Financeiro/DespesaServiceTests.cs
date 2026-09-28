using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Financeiro.Services;
using Backend.Business.Financeiro.Validators;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Financeiro;

/// <summary>
/// As regras da despesa que não dependem do banco: a parcelada vira N linhas com o centavo fechando, o
/// lançamento repetido é recusado, pagar sem comprovante é 400 e a paga se corrige mas não se cancela.
/// </summary>
public sealed class DespesaServiceTests
{
    private static readonly Guid UsuarioId = Guid.CreateVersion7();
    private static readonly Guid FornecedorId = Guid.CreateVersion7();
    private static readonly Guid LancamentoId = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IDespesaRepository _despesas = Substitute.For<IDespesaRepository>();
    private readonly IFornecedorRepository _fornecedores = Substitute.For<IFornecedorRepository>();
    private readonly IItemDaFestaRepository _itensDaFesta = Substitute.For<IItemDaFestaRepository>();
    private readonly IArquivoService _arquivos = Substitute.For<IArquivoService>();
    private readonly IEventoRepository _eventos = Substitute.For<IEventoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly DateOnly _hoje = DataUtils.Hoje();

    public DespesaServiceTests()
    {
        _fornecedores.ObterCategoria(FornecedorId, Arg.Any<CancellationToken>()).Returns(CategoriaDeDespesa.Buffet);
        _arquivos
            .Enviar(Arg.Any<NovoArquivo>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Ok(
                    new ArquivoResumo(
                        Guid.CreateVersion7(),
                        "comprovante.pdf",
                        "application/pdf",
                        10,
                        "comprovantes-despesa",
                        UsuarioId,
                        DateTime.UtcNow
                    )
                )
            );
        _despesas
            .Obter(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(chamada => Resumo(chamada.Arg<Guid>(), StatusDaDespesa.Prevista));
    }

    private DespesaService Servico =>
        new(
            _despesas,
            _fornecedores,
            _itensDaFesta,
            _arquivos,
            new NovaDespesaValidator(),
            new DadosDaDespesaValidator(),
            new PagarDespesaValidator(),
            _eventos,
            _unitOfWork,
            NullLogger<DespesaService>.Instance
        );

    [Fact]
    public void Parcelada_em_3x_gera_3_linhas_com_o_centavo_fechando()
    {
        // Arrange
        var dados = Nova(valorEmCentavos: 100_000, parcelas: 3, vencimento: new DateOnly(2027, 1, 31));

        // Act
        var linhas = DespesaService.Parcelar(dados);

        // Assert
        linhas.Count.ShouldBe(3);
        linhas.Sum(linha => linha.ValorEmCentavos).ShouldBe(100_000);
        linhas[0].ValorEmCentavos.ShouldBe(33_334);
        linhas[1].ValorEmCentavos.ShouldBe(33_333);
        linhas.Select(linha => linha.Vencimento).ShouldBe([new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31)]);
    }

    [Fact]
    public async Task Lancamento_repetido_devolve_conflito_e_nao_grava()
    {
        // Arrange
        _despesas.ExisteIgual(FornecedorId, "Buffet", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var resultado = await Servico.Lancar(Nova(), UsuarioId, null, Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("financeiro.despesa_duplicada");
        await _despesas.DidNotReceive().Adicionar(Arg.Any<IReadOnlyList<Despesa>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Despesa_que_nasce_paga_sem_comprovante_devolve_validacao()
    {
        // Act
        var resultado = await Servico.Lancar(Nova() with { PagaEm = _hoje }, UsuarioId, null, Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("financeiro.comprovante_obrigatorio");
        await _despesas.DidNotReceive().Adicionar(Arg.Any<IReadOnlyList<Despesa>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Despesa_que_nasce_paga_paga_so_a_primeira_parcela()
    {
        // Arrange
        var gravadas = new List<Despesa>();
        await _despesas.Adicionar(Arg.Do<IReadOnlyList<Despesa>>(linhas => gravadas.AddRange(linhas)), Arg.Any<CancellationToken>());

        // Act
        var resultado = await Servico.Lancar(Nova(parcelas: 3) with { PagaEm = _hoje }, UsuarioId, Comprovante("nota.pdf"), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        gravadas.Count.ShouldBe(3);
        gravadas[0].Status.ShouldBe(StatusDaDespesa.Paga);
        gravadas[0].ComprovanteArquivoId.ShouldNotBeNull();
        gravadas.Skip(1).ShouldAllBe(despesa => despesa.Status == StatusDaDespesa.Prevista);
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Parcelada_grava_o_mesmo_lancamento_em_todas_as_linhas()
    {
        // Arrange
        var gravadas = new List<Despesa>();
        await _despesas.Adicionar(Arg.Do<IReadOnlyList<Despesa>>(linhas => gravadas.AddRange(linhas)), Arg.Any<CancellationToken>());

        // Act
        await Servico.Lancar(Nova(parcelas: 3), UsuarioId, null, Ct);

        // Assert
        gravadas.Select(despesa => despesa.LancamentoId).Distinct().Count().ShouldBe(1);
        gravadas[0].LancamentoId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Lancamentos_diferentes_nao_compartilham_o_lancamento()
    {
        // Arrange
        var gravadas = new List<Despesa>();
        await _despesas.Adicionar(Arg.Do<IReadOnlyList<Despesa>>(linhas => gravadas.AddRange(linhas)), Arg.Any<CancellationToken>());

        // Act
        await Servico.Lancar(Nova(), UsuarioId, null, Ct);
        await Servico.Lancar(Nova() with { Descricao = "Espaço" }, UsuarioId, null, Ct);

        // Assert
        gravadas.Count.ShouldBe(2);
        gravadas[0].LancamentoId.ShouldNotBe(gravadas[1].LancamentoId);
    }

    [Fact]
    public async Task Comprovante_que_nao_e_pdf_nem_imagem_devolve_validacao()
    {
        // Act
        var resultado = await Servico.Lancar(Nova() with { PagaEm = _hoje }, UsuarioId, Comprovante("planilha.xlsx"), Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("financeiro.comprovante_invalido");
    }

    [Fact]
    public async Task Pagar_sem_comprovante_devolve_validacao_e_nao_toca_na_despesa()
    {
        // Arrange
        var despesa = Despesa.Nova(Nova(), Guid.CreateVersion7(), 1, _hoje, 100_000);
        _despesas.ObterParaEdicao(despesa.Id, Arg.Any<CancellationToken>()).Returns(despesa);

        // Act
        var resultado = await Servico.Pagar(despesa.Id, new PagarDespesa(_hoje), UsuarioId, null, Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("financeiro.comprovante_obrigatorio");
        despesa.Status.ShouldBe(StatusDaDespesa.Prevista);
        await _unitOfWork.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pagar_grava_dia_e_comprovante()
    {
        // Arrange
        var despesa = Despesa.Nova(Nova(), Guid.CreateVersion7(), 1, _hoje, 100_000);
        _despesas.ObterParaEdicao(despesa.Id, Arg.Any<CancellationToken>()).Returns(despesa);

        // Act
        var resultado = await Servico.Pagar(despesa.Id, new PagarDespesa(_hoje), UsuarioId, Comprovante("recibo.jpg"), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        despesa.Status.ShouldBe(StatusDaDespesa.Paga);
        despesa.PagoEm.ShouldBe(_hoje);
        despesa.ComprovanteArquivoId.ShouldNotBeNull();
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Despesa_paga_aceita_correcao_da_tesouraria()
    {
        // Arrange
        var despesa = Despesa.Nova(Nova(), Guid.CreateVersion7(), 1, _hoje, 100_000);
        despesa.Pagar(_hoje, Guid.CreateVersion7());
        _despesas.ObterParaEdicao(despesa.Id, Arg.Any<CancellationToken>()).Returns(despesa);

        // Act
        var resultado = await Servico.Atualizar(
            despesa.Id,
            new DadosDaDespesa(FornecedorId, null, "Buffet — entrada", CategoriaDeDespesa.Buffet, 95_000, _hoje, _hoje),
            Ct
        );

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        despesa.ValorEmCentavos.ShouldBe(95_000);
        despesa.Status.ShouldBe(StatusDaDespesa.Paga);
    }

    [Fact]
    public async Task Despesa_cancelada_nao_aceita_correcao()
    {
        // Arrange
        var despesa = Despesa.Nova(Nova(), Guid.CreateVersion7(), 1, _hoje, 100_000);
        despesa.Cancelar();
        _despesas.ObterParaEdicao(despesa.Id, Arg.Any<CancellationToken>()).Returns(despesa);

        // Act
        var resultado = await Servico.Atualizar(
            despesa.Id,
            new DadosDaDespesa(FornecedorId, null, "Buffet — entrada", CategoriaDeDespesa.Buffet, 95_000, _hoje, _hoje),
            Ct
        );

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("financeiro.despesa_cancelada");
        despesa.ValorEmCentavos.ShouldBe(100_000);
    }

    [Fact]
    public async Task Cancelar_despesa_paga_devolve_conflito()
    {
        // Arrange
        var despesa = Despesa.Nova(Nova(), Guid.CreateVersion7(), 1, _hoje, 100_000);
        despesa.Pagar(_hoje, Guid.CreateVersion7());
        _despesas.ObterParaEdicao(despesa.Id, Arg.Any<CancellationToken>()).Returns(despesa);

        // Act
        var resultado = await Servico.Cancelar(despesa.Id, Guid.CreateVersion7(), Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("financeiro.despesa_nao_prevista");
        despesa.Status.ShouldBe(StatusDaDespesa.Paga);
    }

    [Fact]
    public async Task Fornecedor_de_outra_turma_nao_entra_no_lancamento()
    {
        // Act
        var resultado = await Servico.Lancar(Nova() with { FornecedorId = Guid.CreateVersion7() }, UsuarioId, null, Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("financeiro.fornecedor_nao_encontrado");
    }

    [Fact]
    public async Task Competencia_e_gravada_no_primeiro_dia_do_mes()
    {
        // Arrange
        var gravadas = new List<Despesa>();
        await _despesas.Adicionar(Arg.Do<IReadOnlyList<Despesa>>(linhas => gravadas.AddRange(linhas)), Arg.Any<CancellationToken>());

        // Act
        await Servico.Lancar(Nova() with { Competencia = new DateOnly(2027, 3, 18) }, UsuarioId, null, Ct);

        // Assert
        gravadas[0].Competencia.ShouldBe(new DateOnly(2027, 3, 1));
    }

    [Fact]
    public async Task Resumo_separa_atrasada_de_prevista()
    {
        // Arrange
        _despesas
            .Contar(Arg.Any<FiltroDeDespesas>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(
                new List<ContagemDeDespesas>
                {
                    new(StatusDaDespesa.Prevista, true, 2, 30_000),
                    new(StatusDaDespesa.Prevista, false, 1, 10_000),
                    new(StatusDaDespesa.Paga, false, 3, 60_000),
                    new(StatusDaDespesa.Cancelada, false, 1, 5_000),
                }
            );

        // Act
        var resumo = (await Servico.Resumir(new FiltroDeDespesas(), Ct)).Valor;

        // Assert
        resumo.Prevista.ShouldBe(new SomaDeLancamentos(3, 40_000));
        resumo.Atrasada.ShouldBe(new SomaDeLancamentos(2, 30_000));
        resumo.Paga.ShouldBe(new SomaDeLancamentos(3, 60_000));
        resumo.Todas.ShouldBe(new SomaDeLancamentos(7, 105_000));
    }

    private NovaDespesa Nova(long valorEmCentavos = 100_000, int parcelas = 1, DateOnly? vencimento = null) =>
        new(FornecedorId, null, "Buffet", CategoriaDeDespesa.Buffet, valorEmCentavos, parcelas, _hoje, vencimento ?? _hoje);

    private DespesaResumo Resumo(Guid id, StatusDaDespesa status) =>
        new(
            id,
            LancamentoId,
            FornecedorId,
            null,
            "Buffet Sabor",
            "Buffet",
            CategoriaDeDespesa.Buffet,
            100_000,
            _hoje,
            _hoje,
            1,
            1,
            status,
            null,
            false
        );

    private static NovoArquivo Comprovante(string nome) => new(nome, 10, Stream.Null, "comprovantes-despesa");
}
