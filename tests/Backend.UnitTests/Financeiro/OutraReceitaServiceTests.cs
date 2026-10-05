using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Financeiro.Services;
using Backend.Business.Financeiro.Validators;
using Backend.Business.Formaturas.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Financeiro;

/// <summary>
/// As regras da receita que não dependem do banco: o lançamento repetido é recusado, recebida não
/// tem data no futuro, o comprovante precisa ser do acervo da turma, e a recebida se corrige mas não
/// se cancela nem se recebe de novo.
/// </summary>
public sealed class OutraReceitaServiceTests
{
    private static readonly Guid AutorId = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IOutraReceitaRepository _outrasReceitas = Substitute.For<IOutraReceitaRepository>();
    private readonly IDocumentoRepository _documentos = Substitute.For<IDocumentoRepository>();
    private readonly IDocumentoService _acervo = Substitute.For<IDocumentoService>();
    private readonly IEventoRepository _eventos = Substitute.For<IEventoRepository>();
    private readonly IFormaturaAtual _formaturaAtual = Substitute.For<IFormaturaAtual>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly DateOnly _hoje = DataUtils.Hoje();

    public OutraReceitaServiceTests()
    {
        _formaturaAtual.Id.Returns(Guid.CreateVersion7());

        _outrasReceitas
            .Obter(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(chamada => new OutraReceitaResumo(
                chamada.Arg<Guid>(),
                "Cota ouro",
                "Clínica Sorriso",
                CategoriaDeOutraReceita.Patrocinio,
                5_000_00L,
                _hoje,
                StatusDaOutraReceita.Prevista,
                null
            ));
    }

    private OutraReceitaService Servico =>
        new(
            _outrasReceitas,
            _documentos,
            _acervo,
            new NovaOutraReceitaValidator(),
            new DadosDaOutraReceitaValidator(),
            new ReceberOutraReceitaValidator(),
            _eventos,
            _formaturaAtual,
            _unitOfWork,
            NullLogger<OutraReceitaService>.Instance
        );

    [Fact]
    public async Task Lancar_ja_recebida_nasce_recebida_e_grava()
    {
        // Act
        var resultado = await Servico.Lancar(Nova(recebida: true), AutorId, null, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _outrasReceitas
            .Received(1)
            .Adicionar(
                Arg.Is<OutraReceita>(r => r.Status == StatusDaOutraReceita.Recebida && r.Origem == "Clínica Sorriso"),
                Arg.Any<CancellationToken>()
            );
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recebida_com_data_no_futuro_e_recusada()
    {
        // Act
        var resultado = await Servico.Lancar(Nova(recebida: true, data: _hoje.AddDays(3)), AutorId, null, Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Campo.ShouldBe("data");
        await _unitOfWork.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prevista_pode_ter_data_no_futuro()
    {
        // Act
        var resultado = await Servico.Lancar(Nova(recebida: false, data: _hoje.AddMonths(2)), AutorId, null, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _outrasReceitas
            .Received(1)
            .Adicionar(Arg.Is<OutraReceita>(r => r.Status == StatusDaOutraReceita.Prevista), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lancamento_repetido_devolve_conflito_e_nao_grava()
    {
        // Arrange
        _outrasReceitas.ExisteIgual("Cota ouro", "Clínica Sorriso", _hoje, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var resultado = await Servico.Lancar(Nova(descricao: "  Cota ouro "), AutorId, null, Ct);

        // Assert
        resultado.Erros[0].Codigo.ShouldBe("financeiro.outra_receita_duplicada");
        await _unitOfWork.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Comprovante_que_a_turma_nao_enxerga_e_recusado()
    {
        // Arrange — o repositório devolve nulo para o papel de formando: documento só da comissão.
        var documentoId = Guid.CreateVersion7();

        // Act
        var resultado = await Servico.Lancar(Nova() with { DocumentoId = documentoId }, AutorId, null, Ct);

        // Assert
        resultado.Erros[0].Codigo.ShouldBe("financeiro.documento_nao_encontrado");
        await _documentos.Received(1).Obter(documentoId, PapelNaFormatura.Formando, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Comprovante_anexado_ao_lancamento_nasce_no_acervo_e_e_ligado_a_receita()
    {
        // Arrange
        var doAcervo = Guid.CreateVersion7();
        _acervo
            .Enviar(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Is<DadosDoDocumento>(dados => dados.Categoria == CategoriaDeDocumento.Comprovante && dados.Visibilidade == Visibilidade.Turma),
                Arg.Any<NovoArquivo?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Ok(
                    new DocumentoResumo(
                        doAcervo,
                        "Cota ouro",
                        CategoriaDeDocumento.Comprovante,
                        Visibilidade.Turma,
                        1,
                        "extrato.pdf",
                        "application/pdf",
                        1024,
                        DateTime.UtcNow,
                        null
                    )
                )
            );

        // Act
        var resultado = await Servico.Lancar(Nova(), AutorId, Comprovante("extrato.pdf"), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _outrasReceitas.Received(1).Adicionar(Arg.Is<OutraReceita>(receita => receita.DocumentoId == doAcervo), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receber_a_prevista_vira_recebida_na_data_informada()
    {
        // Arrange
        var outraReceita = OutraReceita.Nova(Nova(data: _hoje.AddDays(10)));
        _outrasReceitas.ObterParaEdicao(outraReceita.Id, Arg.Any<CancellationToken>()).Returns(outraReceita);

        // Act
        var resultado = await Servico.Receber(outraReceita.Id, new ReceberOutraReceita(_hoje), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        outraReceita.Status.ShouldBe(StatusDaOutraReceita.Recebida);
        outraReceita.Data.ShouldBe(_hoje);
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receber_duas_vezes_devolve_ja_recebida()
    {
        // Arrange
        var outraReceita = OutraReceita.Nova(Nova(recebida: true));
        _outrasReceitas.ObterParaEdicao(outraReceita.Id, Arg.Any<CancellationToken>()).Returns(outraReceita);

        // Act
        var resultado = await Servico.Receber(outraReceita.Id, new ReceberOutraReceita(_hoje), Ct);

        // Assert
        resultado.Erros[0].Codigo.ShouldBe("financeiro.outra_receita_ja_recebida");
        await _unitOfWork.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recebida_nao_se_cancela()
    {
        // Arrange
        var outraReceita = OutraReceita.Nova(Nova(recebida: true));
        _outrasReceitas.ObterParaEdicao(outraReceita.Id, Arg.Any<CancellationToken>()).Returns(outraReceita);

        // Act
        var resultado = await Servico.Cancelar(outraReceita.Id, AutorId, Ct);

        // Assert
        resultado.Erros[0].Codigo.ShouldBe("financeiro.outra_receita_ja_recebida");
        outraReceita.Status.ShouldBe(StatusDaOutraReceita.Recebida);
    }

    [Fact]
    public async Task Cancelar_a_prevista_grava_o_retrato_na_auditoria()
    {
        // Arrange
        var outraReceita = OutraReceita.Nova(Nova());
        _outrasReceitas.ObterParaEdicao(outraReceita.Id, Arg.Any<CancellationToken>()).Returns(outraReceita);

        // Act
        var resultado = await Servico.Cancelar(outraReceita.Id, AutorId, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        outraReceita.Status.ShouldBe(StatusDaOutraReceita.Cancelada);
        await _eventos
            .Received(1)
            .Adicionar(
                Arg.Is<Evento>(e => e.Nome == NomesDeAuditoria.OutraReceitaCancelada && e.UsuarioId == AutorId && e.Dados!.Contains("500000")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Cancelada_nao_se_corrige()
    {
        // Arrange
        var outraReceita = OutraReceita.Nova(Nova());
        outraReceita.Cancelar();
        _outrasReceitas.ObterParaEdicao(outraReceita.Id, Arg.Any<CancellationToken>()).Returns(outraReceita);

        // Act
        var resultado = await Servico.Atualizar(outraReceita.Id, Dados(_hoje), AutorId, null, Ct);

        // Assert
        resultado.Erros[0].Codigo.ShouldBe("financeiro.outra_receita_cancelada");
    }

    [Fact]
    public async Task Recebida_corrigida_para_o_futuro_e_recusada()
    {
        // Arrange
        var outraReceita = OutraReceita.Nova(Nova(recebida: true));
        _outrasReceitas.ObterParaEdicao(outraReceita.Id, Arg.Any<CancellationToken>()).Returns(outraReceita);

        // Act
        var resultado = await Servico.Atualizar(outraReceita.Id, Dados(_hoje.AddDays(5)), AutorId, null, Ct);

        // Assert
        resultado.Erros[0].Codigo.ShouldBe("financeiro.outra_receita_data_futura");
        outraReceita.Data.ShouldBe(_hoje);
    }

    [Fact]
    public async Task Resumo_separa_atrasada_dentro_da_prevista()
    {
        // Arrange
        _outrasReceitas
            .Contar(Arg.Any<FiltroDeOutrasReceitas>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([
                new ContagemDeOutrasReceitas(StatusDaOutraReceita.Prevista, false, 2, 3_000_00L),
                new ContagemDeOutrasReceitas(StatusDaOutraReceita.Prevista, true, 1, 1_000_00L),
                new ContagemDeOutrasReceitas(StatusDaOutraReceita.Recebida, false, 4, 8_000_00L),
            ]);

        // Act
        var resumo = (await Servico.Resumir(new FiltroDeOutrasReceitas(), Ct)).Valor;

        // Assert
        resumo.Todas.ShouldBe(new SomaDeLancamentos(7, 12_000_00L));
        resumo.Prevista.ShouldBe(new SomaDeLancamentos(3, 4_000_00L));
        resumo.Atrasada.ShouldBe(new SomaDeLancamentos(1, 1_000_00L));
        resumo.Recebida.ShouldBe(new SomaDeLancamentos(4, 8_000_00L));
        resumo.Cancelada.ShouldBe(SomaDeLancamentos.Zero);
    }

    private NovaOutraReceita Nova(string descricao = "Cota ouro", bool recebida = false, DateOnly? data = null) =>
        new(descricao, "Clínica Sorriso", CategoriaDeOutraReceita.Patrocinio, 5_000_00L, data ?? _hoje, recebida);

    private static DadosDaOutraReceita Dados(DateOnly data) =>
        new("Cota ouro", "Clínica Sorriso", CategoriaDeOutraReceita.Patrocinio, 5_000_00L, data);

    private static NovoArquivo Comprovante(string nome) => new(nome, 1024, new MemoryStream([1, 2, 3]), string.Empty);
}
