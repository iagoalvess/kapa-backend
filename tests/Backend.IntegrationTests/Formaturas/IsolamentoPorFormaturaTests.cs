using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Formaturas;

/// <summary>
/// O entregável não negociável da fundação multi-tenant: prova, contra Postgres de verdade,
/// que a consulta de uma formatura não enxerga linha de outra.
/// </summary>
/// <remarks>
/// Sem este teste, o filtro global é uma promessa. Ele grava o <b>mesmo texto</b> nas duas
/// turmas de propósito: se o isolamento cair, a consulta devolve dois registros idênticos e a
/// falha é inequívoca.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class IsolamentoPorFormaturaTests(ApiFactory fabrica)
{
    private const string MesmoTexto = "Reunião de comissão na quinta.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Consulta_nao_enxerga_dados_de_outra_formatura()
    {
        // Arrange
        var (a, b) = await DuasFormaturas();

        await GravarAviso(a);
        await GravarAviso(b);

        // Act
        await using var contexto = fabrica.ContextoDe(a);
        var avisos = await contexto.Avisos.Where(aviso => aviso.Texto == MesmoTexto).ToListAsync(Ct);

        // Assert
        avisos.Count.ShouldBe(1);
        avisos[0].FormaturaId.ShouldBe(a);
    }

    /// <summary>
    /// O carimbo vem do contexto, e nenhum service atribui <c>FormaturaId</c>.
    /// </summary>
    [Fact]
    public async Task O_contexto_carimba_a_formatura_na_gravacao()
    {
        var (a, _) = await DuasFormaturas();

        var id = await GravarAviso(a);

        await using var contexto = fabrica.ContextoDe(a);
        var gravado = await contexto.Avisos.SingleAsync(aviso => aviso.Id == id, Ct);

        gravado.FormaturaId.ShouldBe(a);
    }

    /// <summary>
    /// Gravar sem formatura selecionada estoura em vez de mandar a linha para <c>Guid.Empty</c>,
    /// onde ela sumiria de toda consulta sem nenhum erro.
    /// </summary>
    [Fact]
    public async Task Gravar_sem_formatura_selecionada_estoura()
    {
        await using var contexto = fabrica.ContextoDe(null);
        contexto.Avisos.Add(new Aviso { Texto = MesmoTexto });

        await Should.ThrowAsync<InvalidOperationException>(() => contexto.SaveChangesAsync(Ct));
    }

    /// <summary>
    /// O filtro só protege a leitura: uma linha que chegou ao contexto por outro caminho vira
    /// <c>DELETE</c> por id, sem <c>where</c> de formatura. O contexto recusa antes de o comando sair.
    /// </summary>
    [Fact]
    public async Task Remover_linha_de_outra_formatura_estoura()
    {
        var (a, b) = await DuasFormaturas();
        var deB = await GravarAviso(b);

        await using var contexto = fabrica.ContextoDe(a);
        var alheio = await contexto.Avisos.IgnoreQueryFilters().SingleAsync(aviso => aviso.Id == deB, Ct);
        contexto.Avisos.Remove(alheio);

        await Should.ThrowAsync<InvalidOperationException>(() => contexto.SaveChangesAsync(Ct));
    }

    /// <summary>
    /// Sem formatura selecionada — o estado do worker e da CLI do EF Core — o filtro não casa
    /// com nada. Quem precisa cruzar turmas usa <c>IgnoreQueryFilters</c> explicitamente.
    /// </summary>
    [Fact]
    public async Task Sem_formatura_selecionada_a_consulta_nao_devolve_nada()
    {
        var (a, b) = await DuasFormaturas();

        await GravarAviso(a);
        await GravarAviso(b);

        await using var contexto = fabrica.ContextoDe(null);

        var filtrado = await contexto.Avisos.Where(aviso => aviso.Texto == MesmoTexto).CountAsync(Ct);
        var todos = await contexto.Avisos.IgnoreQueryFilters().Where(aviso => aviso.Texto == MesmoTexto).CountAsync(Ct);

        filtrado.ShouldBe(0);
        todos.ShouldBeGreaterThanOrEqualTo(2);
    }

    private async Task<(Guid A, Guid B)> DuasFormaturas()
    {
        await using var contexto = fabrica.ContextoDe(null);

        var a = FormaturaDeTeste.NovaFormatura();
        var b = FormaturaDeTeste.NovaFormatura();

        contexto.Formaturas.AddRange(a, b);
        await contexto.SaveChangesAsync(Ct);

        return (a.Id, b.Id);
    }

    private async Task<Guid> GravarAviso(Guid formaturaId)
    {
        await using var contexto = fabrica.ContextoDe(formaturaId);

        var aviso = new Aviso { Texto = MesmoTexto };
        contexto.Avisos.Add(aviso);
        await contexto.SaveChangesAsync(Ct);

        return aviso.Id;
    }
}
