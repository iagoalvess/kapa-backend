using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Eventos;

/// <summary>
/// O funil da trilha de auditoria: o que ele grava, e de onde ele tira a turma.
///
/// A coluna <c>FormaturaId</c> é o que a tela da assembleia filtra. Se ela deixar de ser preenchida,
/// o evento continua no banco e some da tela — uma falha silenciosa e exatamente do tipo que a
/// auditoria existe para não ter.
/// </summary>
public sealed class AuditoriaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IEventoRepository _repositorio = Substitute.For<IEventoRepository>();

    [Fact]
    public async Task Copia_a_formatura_do_corpo_para_a_coluna()
    {
        var formaturaId = Guid.CreateVersion7();

        await _repositorio.Auditar(NomesDeAuditoria.PagamentoBaixado, Guid.CreateVersion7(), new { formaturaId }, Ct);

        (await Gravado()).FormaturaId.ShouldBe(formaturaId);
    }

    /// <summary>
    /// Metade dos services escreve <c>new { formaturaId, … }</c> e a outra metade
    /// <c>new { contexto.FormaturaId, … }</c>. A diferença não pode custar a linha na tela.
    /// </summary>
    [Fact]
    public async Task Aceita_a_chave_em_maiuscula_tambem()
    {
        var formaturaId = Guid.CreateVersion7();

        await _repositorio.Auditar(NomesDeAuditoria.ContaAlterada, Guid.CreateVersion7(), new { FormaturaId = formaturaId }, Ct);

        (await Gravado()).FormaturaId.ShouldBe(formaturaId);
    }

    /// <summary>O bloqueio por tentativas é da conta, e não de turma nenhuma.</summary>
    [Fact]
    public async Task Corpo_sem_formatura_grava_a_coluna_nula()
    {
        await _repositorio.Auditar(NomesDeAuditoria.BloqueioPorTentativas, Guid.CreateVersion7(), new { tentativas = 5 }, Ct);

        (await Gravado()).FormaturaId.ShouldBeNull();
    }

    [Fact]
    public async Task Grava_o_corpo_em_camelCase_e_preserva_o_antes_e_o_depois()
    {
        await _repositorio.Auditar(
            NomesDeAuditoria.PapelAlterado,
            Guid.CreateVersion7(),
            new
            {
                formaturaId = Guid.CreateVersion7(),
                antes = new { papel = "Formando" },
                depois = new { papel = "Tesoureiro" },
            },
            Ct
        );

        var evento = await Gravado();

        evento.Dados.ShouldNotBeNull();
        evento.Dados.ShouldContain("\"antes\"");
        evento.Dados.ShouldContain("\"papel\":\"Tesoureiro\"");
    }

    [Fact]
    public async Task Grava_o_autor_e_o_nome_do_evento()
    {
        var autor = Guid.CreateVersion7();

        await _repositorio.Auditar(NomesDeAuditoria.AvisoExcluido, autor, new { formaturaId = Guid.CreateVersion7() }, Ct);

        var evento = await Gravado();

        evento.UsuarioId.ShouldBe(autor);
        evento.Nome.ShouldBe(NomesDeAuditoria.AvisoExcluido);
    }

    /// <summary>
    /// Todo nome auditável precisa estar na lista: é ela que a tela oferece como filtro e que o job
    /// de retenção poupa do prazo curto.
    /// </summary>
    [Fact]
    public void Todos_os_nomes_da_lista_sao_unicos()
    {
        NomesDeAuditoria.Todos.Distinct(StringComparer.Ordinal).Count().ShouldBe(NomesDeAuditoria.Todos.Count);
    }

    private async Task<Evento> Gravado()
    {
        var chamadas = _repositorio.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IEventoRepository.Adicionar)).ToList();

        chamadas.Count.ShouldBe(1);

        await Task.CompletedTask;

        return (Evento)chamadas[0].GetArguments()[0]!;
    }
}
