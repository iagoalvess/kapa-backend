using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Agenda.Services;
using Backend.Business.Agenda.Validators;
using Backend.Business.Common.Datas;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Agenda;

/// <summary>
/// As regras da agenda que não dependem do banco: colação e festa são únicas, o resto se repete, a
/// edição não conflita consigo mesma e o evento cancelado continua editável.
/// </summary>
public sealed class AgendaServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Hoje => DataUtils.Hoje();

    private readonly IEventoDaTurmaRepository _eventos = Substitute.For<IEventoDaTurmaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private AgendaService Servico => new(_eventos, new DadosDoEventoValidator(), _unitOfWork, NullLogger<AgendaService>.Instance);

    private static DadosDoEvento Dados(
        TipoDeEvento tipo = TipoDeEvento.Reuniao,
        string titulo = "Reunião da comissão",
        DateOnly? data = null,
        SituacaoDoEvento situacao = SituacaoDoEvento.AConfirmar
    ) => new(titulo, tipo, situacao, data ?? Hoje.AddDays(30), null, null, null);

    [Fact]
    public async Task Segunda_colacao_devolve_conflito_sem_gravar()
    {
        // Arrange
        _eventos.ExisteDoTipo(TipoDeEvento.Colacao, null, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var resultado = await Servico.Criar(Dados(TipoDeEvento.Colacao, "Colação de grau"), Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("agenda.tipo_unico");
        await _eventos.DidNotReceive().Adicionar(Arg.Any<EventoDaTurma>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Reunião, prazo e "outro" se repetem: a turma marca quantas quiser.</summary>
    [Fact]
    public async Task Segunda_reuniao_e_gravada_sem_consultar_unicidade()
    {
        // Arrange
        _eventos.Obter(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Resumo());

        // Act
        var resultado = await Servico.Criar(Dados(), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _eventos.Received(1).Adicionar(Arg.Any<EventoDaTurma>(), Arg.Any<CancellationToken>());
        await _eventos.DidNotReceive().ExisteDoTipo(Arg.Any<TipoDeEvento>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Mover a data da colação é o caminho normal, e ela não pode conflitar consigo mesma.
    /// </summary>
    /// <remarks>
    /// É o defeito que a exclusão do próprio id evita: sem ela, a turma que tem colação marcada
    /// nunca mais conseguiria mudá-la de dia — a checagem de unicidade acharia a própria linha.
    /// </remarks>
    [Fact]
    public async Task Mover_a_colacao_existente_nao_conflita_consigo_mesma()
    {
        // Arrange
        var colacao = EventoDaTurma.Novo(Dados(TipoDeEvento.Colacao, "Colação de grau"));
        _eventos.ObterParaEdicao(colacao.Id, Arg.Any<CancellationToken>()).Returns(colacao);
        _eventos.ExisteDoTipo(TipoDeEvento.Colacao, colacao.Id, Arg.Any<CancellationToken>()).Returns(false);
        _eventos.Obter(colacao.Id, Arg.Any<CancellationToken>()).Returns(Resumo(colacao.Id));

        // Act
        var resultado = await Servico.Atualizar(colacao.Id, Dados(TipoDeEvento.Colacao, "Colação de grau", Hoje.AddDays(400)), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        colacao.Data.ShouldBe(Hoje.AddDays(400));
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Editar um evento para virar a segunda festa da turma também é conflito.</summary>
    [Fact]
    public async Task Transformar_uma_reuniao_na_segunda_festa_devolve_conflito()
    {
        // Arrange
        var reuniao = EventoDaTurma.Novo(Dados());
        _eventos.ObterParaEdicao(reuniao.Id, Arg.Any<CancellationToken>()).Returns(reuniao);
        _eventos.ExisteDoTipo(TipoDeEvento.Festa, reuniao.Id, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var resultado = await Servico.Atualizar(reuniao.Id, Dados(TipoDeEvento.Festa, "Festa"), Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("agenda.tipo_unico");
        reuniao.Tipo.ShouldBe(TipoDeEvento.Reuniao);
        await _unitOfWork.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Evento cancelado aceita correção — é assim que uma data desmarcada volta ao calendário.
    /// </summary>
    /// <remarks>
    /// Contrário de propósito ao item da festa, que recusa edição enquanto cancelado: lá o preço de
    /// algo que não vai acontecer confunde o custo da turma; aqui a única coisa a corrigir é a data,
    /// e exigir "reative antes de mover" seria um passo a mais para dizer a mesma coisa.
    /// </remarks>
    [Fact]
    public async Task Evento_cancelado_aceita_correcao_e_volta_a_confirmado()
    {
        // Arrange
        var evento = EventoDaTurma.Novo(Dados(situacao: SituacaoDoEvento.Cancelado));
        _eventos.ObterParaEdicao(evento.Id, Arg.Any<CancellationToken>()).Returns(evento);
        _eventos.Obter(evento.Id, Arg.Any<CancellationToken>()).Returns(Resumo(evento.Id));

        // Act
        var resultado = await Servico.Atualizar(evento.Id, Dados(situacao: SituacaoDoEvento.Confirmado), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        evento.Cancelado.ShouldBeFalse();
        evento.Situacao.ShouldBe(SituacaoDoEvento.Confirmado);
    }

    [Fact]
    public async Task Evento_de_outra_turma_nao_e_excluido()
    {
        // Arrange
        _eventos.ObterParaEdicao(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((EventoDaTurma?)null);

        // Act
        var resultado = await Servico.Excluir(Guid.CreateVersion7(), Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Codigo.ShouldBe("agenda.evento_nao_encontrado");
        _eventos.DidNotReceive().Remover(Arg.Any<EventoDaTurma>());
    }

    /// <summary>Ano fora da janela da turma é dedo trocado, e enche a agenda de meses vazios.</summary>
    [Fact]
    public async Task Data_implausivel_e_recusada_antes_de_qualquer_consulta()
    {
        // Act
        var resultado = await Servico.Criar(Dados(data: new DateOnly(Hoje.Year + 30, 1, 1)), Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        resultado.Erros[0].Tipo.ShouldBe(ETipoErro.Validacao);
        await _eventos.DidNotReceive().Adicionar(Arg.Any<EventoDaTurma>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Titulo_e_local_sao_aparados_e_o_vazio_vira_nulo()
    {
        // Arrange
        EventoDaTurma? gravado = null;
        await _eventos.Adicionar(Arg.Do<EventoDaTurma>(evento => gravado = evento), Arg.Any<CancellationToken>());
        _eventos.Obter(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Resumo());

        // Act
        await Servico.Criar(new DadosDoEvento("  Prova da beca  ", TipoDeEvento.Prazo, SituacaoDoEvento.AConfirmar, Hoje, null, "   ", null), Ct);

        // Assert
        gravado.ShouldNotBeNull();
        gravado.Titulo.ShouldBe("Prova da beca");
        gravado.Local.ShouldBeNull();
    }

    private static EventoResumo Resumo(Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), "Reunião da comissão", TipoDeEvento.Reuniao, SituacaoDoEvento.AConfirmar, Hoje, null, null, null);
}
