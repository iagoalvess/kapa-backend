using Backend.Api.Analytics;
using Backend.Business.Eventos.Models;
using Shouldly;

namespace Backend.UnitTests.Eventos;

/// <summary>
/// Cobre as duas propriedades que a fila de eventos precisa garantir:
/// registrar nunca falha, e a memória tem teto.
/// </summary>
public sealed class FilaDeEventosTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Evento Novo(string nome = "teste.evento") => new() { Nome = nome };

    [Fact]
    public async Task Um_evento_registrado_sai_no_lote()
    {
        var fila = new FilaDeEventos();

        fila.Registrar(Novo("produto.criado"));

        var lote = await fila.LerLoteAsync(10, Ct);

        lote.Count.ShouldBe(1);
        lote[0].Nome.ShouldBe("produto.criado");
    }

    [Fact]
    public async Task O_lote_respeita_o_tamanho_maximo_pedido()
    {
        var fila = new FilaDeEventos();

        for (var i = 0; i < 50; i++)
            fila.Registrar(Novo());

        var lote = await fila.LerLoteAsync(10, Ct);

        lote.Count.ShouldBe(10);
    }

    [Fact]
    public async Task Ler_espera_em_vez_de_devolver_vazio_quando_a_fila_esta_ociosa()
    {
        var fila = new FilaDeEventos();
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var leitura = fila.LerLoteAsync(10, limite.Token);

        leitura.IsCompleted.ShouldBeFalse();

        fila.Registrar(Novo("chegou.depois"));

        var lote = await leitura;
        lote.Count.ShouldBe(1);
    }

    /// <summary>
    /// Com a fila cheia, registrar continua não lançando e não bloqueando — o evento é
    /// descartado. É a propriedade que impede analytics de derrubar a requisição que media.
    /// </summary>
    [Fact]
    public void Fila_cheia_descarta_em_vez_de_lancar_ou_bloquear()
    {
        var fila = new FilaDeEventos();

        Should.NotThrow(() =>
        {
            for (var i = 0; i < 15_000; i++)
                fila.Registrar(Novo());
        });

        fila.DescartadosEZerar().ShouldBeGreaterThan(0);
    }

    [Fact]
    public void O_contador_de_descartes_zera_ao_ser_lido()
    {
        var fila = new FilaDeEventos();

        for (var i = 0; i < 15_000; i++)
            fila.Registrar(Novo());

        fila.DescartadosEZerar().ShouldBeGreaterThan(0);
        fila.DescartadosEZerar().ShouldBe(0);
    }
}
