using Backend.Business.Abstractions;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Comunicacao.Services;
using Backend.Business.Comunicacao.Validators;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Comunicacao;

/// <summary>
/// As regras do mural que não dependem do banco: o quarto fixado é 409, visibilidade não tem padrão,
/// o papel de quem lê vai do vínculo ao repositório, e excluir deixa rastro na mesma transação.
/// </summary>
public sealed class AvisoServiceTests
{
    private static readonly Guid FormaturaId = Guid.CreateVersion7();
    private static readonly Guid UsuarioId = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IAvisoRepository _avisos = Substitute.For<IAvisoRepository>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly IEventoRepository _eventos = Substitute.For<IEventoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public AvisoServiceTests()
    {
        _vinculos.ObterPapelAtivo(UsuarioId, FormaturaId, Arg.Any<CancellationToken>()).Returns(PapelNaFormatura.Comissao);
        _avisos.Obter(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(chamada => Resumo(chamada.Arg<Guid>()));
    }

    private AvisoService Servico => new(_avisos, _vinculos, _eventos, new DadosDoAvisoValidator(), _unitOfWork, NullLogger<AvisoService>.Instance);

    private static DadosDoAviso Dados(bool fixado = false, Visibilidade? visibilidade = Visibilidade.Turma) =>
        new("Reunião na quinta", "A pauta é o **buffet**.", visibilidade, fixado, Destaque: false);

    private static AvisoResumo Resumo(Guid id) =>
        new(id, "Reunião na quinta", "A pauta é o **buffet**.", Visibilidade.Turma, false, false, DateTime.UtcNow, DateTime.UtcNow, UsuarioId, "Ana");

    [Fact]
    public async Task O_quarto_fixado_devolve_409_e_nao_grava()
    {
        // Arrange
        _avisos.ContarFixados(Arg.Any<CancellationToken>()).Returns(Aviso.LimiteDeFixados);

        // Act
        var resultado = await Servico.Publicar(FormaturaId, UsuarioId, Dados(fixado: true), Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("comunicacao.limite_de_fixados");
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Conflito);
        await _avisos.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    [Fact]
    public async Task Com_o_limite_cheio_ainda_publica_aviso_nao_fixado()
    {
        _avisos.ContarFixados(Arg.Any<CancellationToken>()).Returns(Aviso.LimiteDeFixados);

        var resultado = await Servico.Publicar(FormaturaId, UsuarioId, Dados(fixado: false), Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _avisos.Received(1).Adicionar(Arg.Is<Aviso>(a => a.PublicadoPorUsuarioId == UsuarioId && !a.Fixado), Ct);
    }

    /// <summary>Corrigir o texto de um dos três fixados não é fixar um quarto.</summary>
    [Fact]
    public async Task Corrigir_um_aviso_ja_fixado_nao_esbarra_no_limite()
    {
        var aviso = Aviso.Novo(Dados(fixado: true), UsuarioId);
        _avisos.ObterParaEdicao(aviso.Id, Arg.Any<CancellationToken>()).Returns(aviso);
        _avisos.ContarFixados(Arg.Any<CancellationToken>()).Returns(Aviso.LimiteDeFixados);

        var resultado = await Servico.Atualizar(FormaturaId, UsuarioId, aviso.Id, Dados(fixado: true) with { Titulo = "Reunião na sexta" }, Ct);

        resultado.Sucesso.ShouldBeTrue();
        aviso.Titulo.ShouldBe("Reunião na sexta");
    }

    [Fact]
    public async Task Fixar_na_correcao_tambem_respeita_o_limite()
    {
        var aviso = Aviso.Novo(Dados(fixado: false), UsuarioId);
        _avisos.ObterParaEdicao(aviso.Id, Arg.Any<CancellationToken>()).Returns(aviso);
        _avisos.ContarFixados(Arg.Any<CancellationToken>()).Returns(Aviso.LimiteDeFixados);

        var resultado = await Servico.Atualizar(FormaturaId, UsuarioId, aviso.Id, Dados(fixado: true), Ct);

        resultado.PrimeiroErro.Codigo.ShouldBe("comunicacao.limite_de_fixados");
        aviso.Fixado.ShouldBeFalse();
    }

    /// <summary>Sem escolha não vira "turma": o aviso interno esquecido não pode sair para todo mundo.</summary>
    [Fact]
    public async Task Sem_visibilidade_o_aviso_e_recusado()
    {
        var resultado = await Servico.Publicar(FormaturaId, UsuarioId, Dados(visibilidade: null), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.Erros.ShouldContain(erro => erro.Campo == "visibilidade");
    }

    [Fact]
    public async Task A_leitura_leva_ao_repositorio_o_papel_gravado_no_vinculo()
    {
        _vinculos.ObterPapelAtivo(UsuarioId, FormaturaId, Arg.Any<CancellationToken>()).Returns(PapelNaFormatura.Formando);
        _avisos
            .Listar(Arg.Any<PaginacaoRequest>(), Arg.Any<FiltroDeAvisos>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(PaginaDe<AvisoResumo>.Vazia(new PaginacaoRequest()));

        await Servico.Listar(FormaturaId, UsuarioId, new PaginacaoRequest(), new FiltroDeAvisos(), Ct);

        await _avisos.Received(1).Listar(Arg.Any<PaginacaoRequest>(), Arg.Any<FiltroDeAvisos>(), PapelNaFormatura.Formando, Ct);
    }

    [Fact]
    public async Task Excluir_registra_quem_excluiu_na_mesma_transacao()
    {
        var aviso = Aviso.Novo(Dados(), Guid.CreateVersion7());
        _avisos.ObterParaEdicao(aviso.Id, Arg.Any<CancellationToken>()).Returns(aviso);

        var resultado = await Servico.Excluir(FormaturaId, UsuarioId, aviso.Id, Ct);

        resultado.Sucesso.ShouldBeTrue();
        Received.InOrder(() =>
        {
            _avisos.Remover(aviso);
            _eventos.Adicionar(
                Arg.Is<Evento>(e => e.Nome == AvisoService.EventoDeExclusao && e.UsuarioId == UsuarioId && e.Dados!.Contains(aviso.Id.ToString())),
                Ct
            );
            _unitOfWork.SalvarAsync(Ct);
        });
    }

    [Fact]
    public void A_correcao_nao_muda_autor_nem_data_de_publicacao()
    {
        var aviso = Aviso.Novo(Dados(), UsuarioId);
        var publicadoEm = aviso.PublicadoEm;

        aviso.Aplicar(Dados() with { Titulo = "Outro título", Visibilidade = Visibilidade.SomenteComissao });

        aviso.PublicadoPorUsuarioId.ShouldBe(UsuarioId);
        aviso.PublicadoEm.ShouldBe(publicadoEm);
        aviso.Visibilidade.ShouldBe(Visibilidade.SomenteComissao);
    }
}
