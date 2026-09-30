using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Formaturas.Services;
using Backend.Data.Context;
using Backend.Data.Criptografia;
using Backend.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Formaturas;

/// <summary>
/// A retenção de turmas (decisão de 25/09/2026): suspensa há 12 meses encerra, encerrada há 5 anos e
/// descartada há 30 dias são eliminadas, e o que é prova fica.
/// </summary>
public sealed class RetencaoDeFormaturasTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRetencaoDeFormaturasRepository _retencao = Substitute.For<IRetencaoDeFormaturasRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly IArmazenamentoDeArquivos _armazenamento = Substitute.For<IArmazenamentoDeArquivos>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IPendenciasDaTurmaRepository _pendencias = Substitute.For<IPendenciasDaTurmaRepository>();
    private readonly IEventoRepository _eventos = Substitute.For<IEventoRepository>();

    public RetencaoDeFormaturasTests() =>
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result>>>()(CancellationToken.None));

    private RetencaoDeFormaturasService Criar() =>
        new(_retencao, _formaturas, _armazenamento, _pendencias, _eventos, _unitOfWork, NullLogger<RetencaoDeFormaturasService>.Instance);

    /// <summary>Uma turma no status pedido, que chegou lá há <paramref name="ha"/>.</summary>
    private Formatura Turma(StatusDaFormatura status, TimeSpan ha)
    {
        var formatura = new Formatura();
        formatura.Transicionar(status);

        var desde = DateTime.UtcNow - ha;
        typeof(Formatura).GetProperty(nameof(Formatura.StatusDesde))!.SetValue(formatura, desde);
        if (status == StatusDaFormatura.Encerrada)
            typeof(Formatura).GetProperty(nameof(Formatura.EncerradaEm))!.SetValue(formatura, desde);

        _formaturas.ObterParaEdicao(formatura.Id, Arg.Any<CancellationToken>()).Returns(formatura);

        return formatura;
    }

    [Fact]
    public async Task Encerrada_ha_mais_de_cinco_anos_perde_os_arquivos_antes_das_linhas()
    {
        var formatura = Turma(StatusDaFormatura.Encerrada, TimeSpan.FromDays(5 * 366));

        var eliminada = await Criar().Eliminar(formatura.Id, Ct);

        eliminada.ShouldBeTrue();
        formatura.EliminadaEm.ShouldNotBeNull();
        Received.InOrder(() =>
        {
            _armazenamento.RemoverPrefixoAsync($"formaturas/{formatura.Id:N}", Arg.Any<CancellationToken>());
            _retencao.ApagarDados(formatura.Id, $"formaturas/{formatura.Id:N}", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Descartada_ha_mais_de_trinta_dias_e_eliminada()
    {
        var formatura = Turma(StatusDaFormatura.Descartada, TimeSpan.FromDays(31));

        (await Criar().Eliminar(formatura.Id, Ct)).ShouldBeTrue();
    }

    /// <summary>
    /// A regra é conferida de novo na linha, e não só na consulta que montou a lista: entre as duas, a
    /// turma pode ter sido eliminada por outra passada — ou a lista pode estar errada.
    /// </summary>
    [Theory]
    [InlineData(StatusDaFormatura.Encerrada, 4 * 365)]
    [InlineData(StatusDaFormatura.Descartada, 29)]
    [InlineData(StatusDaFormatura.Suspensa, 10 * 365)]
    [InlineData(StatusDaFormatura.Ativa, 10 * 365)]
    public async Task Turma_dentro_do_prazo_ou_em_uso_nao_perde_nada(StatusDaFormatura status, int diasAtras)
    {
        var formatura = Turma(status, TimeSpan.FromDays(diasAtras));

        (await Criar().Eliminar(formatura.Id, Ct)).ShouldBeFalse();

        formatura.EliminadaEm.ShouldBeNull();
        await _armazenamento.DidNotReceiveWithAnyArgs().RemoverPrefixoAsync(default!, Ct);
        await _retencao.DidNotReceiveWithAnyArgs().ApagarDados(default, default!, Ct);
    }

    /// <summary>
    /// Sprint 42, F9: a suspensa há um ano encerra mesmo com pendência, e a auditoria guarda quais ficaram abertas — sem
    /// autor, porque foi a retenção.
    /// </summary>
    [Fact]
    public async Task Suspensa_abandonada_encerra_com_as_pendencias_na_auditoria_e_comeca_a_contar_os_cinco_anos()
    {
        // Arrange
        var formatura = Turma(StatusDaFormatura.Suspensa, TimeSpan.FromDays(400));
        _pendencias.ContarParaEncerrar(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(new PendenciasDaTurma(3, 1, 0, 0, 0, 0, 0, 1));

        // Act
        var encerrada = await Criar().EncerrarAbandonada(formatura.Id, Ct);

        // Assert
        encerrada.ShouldBeTrue();
        formatura.Status.ShouldBe(StatusDaFormatura.Encerrada);
        formatura.EncerradaEm!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-1));
        await _eventos
            .Received(1)
            .Adicionar(
                Arg.Is<Evento>(e =>
                    e.Nome == NomesDeAuditoria.EncerradaPorAbandono
                    && e.UsuarioId == null
                    && e.FormaturaId == formatura.Id
                    && e.Dados!.Contains("3 parcelas em aberto")
                    && e.Dados.Contains("1 valor a devolver a formando")
                ),
                Arg.Any<CancellationToken>()
            );
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suspensa_ha_menos_de_um_ano_ou_reativada_nao_encerra()
    {
        var recente = Turma(StatusDaFormatura.Suspensa, TimeSpan.FromDays(300));
        var reativada = Turma(StatusDaFormatura.Ativa, TimeSpan.FromDays(400));

        (await Criar().EncerrarAbandonada(recente.Id, Ct)).ShouldBeFalse();
        (await Criar().EncerrarAbandonada(reativada.Id, Ct)).ShouldBeFalse();

        recente.Status.ShouldBe(StatusDaFormatura.Suspensa);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    /// <summary>
    /// Entidade nova da formatura sem lugar na retenção ficaria para sempre — ou quebraria a remoção pela
    /// chave estrangeira. Este teste obriga a decidir: apagar ou manter.
    /// </summary>
    [Fact]
    public void Toda_entidade_da_formatura_tem_destino_na_retencao()
    {
        var daFormatura = typeof(EntidadeDaFormatura)
            .Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(EntidadeDaFormatura).IsAssignableFrom(t))
            .ToHashSet();

        var comDestino = RetencaoDeFormaturasRepository.Apagadas.Concat(RetencaoDeFormaturasRepository.Mantidas).ToList();

        comDestino.ShouldBeUnique();
        daFormatura.Except(comDestino).ShouldBeEmpty("entidade da formatura sem destino na retenção");
        comDestino.Except(daFormatura).ShouldBeEmpty();
    }

    /// <summary>
    /// Com as chaves <c>Restrict</c>, apagar quem é apontado antes de quem aponta derruba a transação.
    /// A ordem é conferida contra o modelo do EF, que é o que o banco tem. Quem aponta para uma tabela
    /// apagada e não está na lista (uma mantida, ou tabela de fora da formatura) também é violação: a
    /// linha dele seguraria a remoção. A correção de perfil é a exceção tratada à parte no repositório.
    /// </summary>
    [Fact]
    public void A_ordem_de_remocao_respeita_as_chaves_estrangeiras()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost").Options,
            new SemFormaturaSelecionada(),
            new CifraDeCampo(Options.Create(new CriptografiaSettings()))
        );

        var ordem = RetencaoDeFormaturasRepository.Apagadas.Select((tipo, i) => (tipo, i)).ToDictionary(x => x.tipo, x => x.i);

        var violacoes = db
            .Model.GetEntityTypes()
            .SelectMany(tipo => tipo.GetForeignKeys())
            .Where(fk => fk.DeleteBehavior is not (DeleteBehavior.Cascade or DeleteBehavior.SetNull or DeleteBehavior.ClientSetNull))
            .Select(fk => (Aponta: fk.DeclaringEntityType.ClrType, Apontado: fk.PrincipalEntityType.ClrType))
            .Where(par => ordem.ContainsKey(par.Apontado))
            .Where(par => par.Aponta != typeof(CorrecaoDePerfil))
            .Where(par => !ordem.TryGetValue(par.Aponta, out var i) || i > ordem[par.Apontado])
            .Select(par => $"{par.Aponta.Name} -> {par.Apontado.Name}")
            .ToList();

        violacoes.ShouldBeEmpty();
    }
}
