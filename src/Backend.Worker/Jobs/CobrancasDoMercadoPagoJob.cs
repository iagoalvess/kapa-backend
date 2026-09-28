using Backend.Business.Abstractions;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Worker.Configuration;

namespace Backend.Worker.Jobs;

/// <summary>
/// A rede embaixo do webhook: consulta no Mercado Pago as cobranças emitidas e ainda não resolvidas, e
/// renova as autorizações perto de vencer (Sprint 25, decisão 12b).
/// </summary>
/// <remarks>
/// O webhook não é garantia de entrega: o Mercado Pago desiste depois de algumas tentativas, e a API pode
/// estar em deploy na hora. A cada dois minutos, o que foi pago e não chegou pelo aviso é baixado pelo
/// mesmo <see cref="BaixaAutomatica.Conciliar"/> — com a mesma trava, então aviso e conciliação juntos
/// baixam uma vez.
/// <para>
/// Um escopo por cobrança, apontado para a turma dela, como os demais jobs por formatura. A renovação vai
/// na mesma passada: é uma consulta que quase sempre volta vazia, e o token vale 180 dias — renovar
/// com 30 de folga dá um mês de rodadas para uma falha se resolver.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Um escopo por turma.</param>
/// <param name="lideranca">Uma réplica por vez.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class CobrancasDoMercadoPagoJob(IServiceScopeFactory scopeFactory, LiderancaDeJob lideranca, ILogger<CobrancasDoMercadoPagoJob> logger)
    : JobPeriodico(lideranca, logger)
{
    /// <summary>Quanto a cobrança recém-emitida espera antes da primeira consulta — o webhook costuma chegar antes.</summary>
    public static readonly TimeSpan Carencia = TimeSpan.FromMinutes(1);

    /// <summary>Com quanto tempo de folga a autorização é renovada.</summary>
    public static readonly TimeSpan FolgaDaRenovacao = TimeSpan.FromDays(30);

    /// <summary>Teto por rodada, de cobranças e de renovações.</summary>
    public const int PorRodada = 50;

    /// <inheritdoc />
    protected override TimeSpan Intervalo { get; } = TimeSpan.FromMinutes(2);

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha na conciliação do Mercado Pago. A próxima execução tentará de novo.");

    /// <inheritdoc />
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        IReadOnlyList<CobrancaAConciliar> cobrancas;
        IReadOnlyList<Guid> aRenovar;

        using (var escopo = scopeFactory.CreateScope())
        {
            var repositorio = escopo.ServiceProvider.GetRequiredService<IProvedorDaTurmaRepository>();
            cobrancas = await repositorio.ListarAConciliarDeTodasAsFormaturas(DateTime.UtcNow - Carencia, PorRodada, ct);
            aRenovar = await repositorio.ListarFormaturasComCredencialAVencerDeTodasAsFormaturas(DateTime.UtcNow + FolgaDaRenovacao, PorRodada, ct);
        }

        foreach (var cobranca in cobrancas)
            await NaTurma(
                cobranca.FormaturaId,
                async sp =>
                {
                    var resultado = await sp.GetRequiredService<BaixaAutomatica>().Conciliar(cobranca.CobrancaId, ct);
                    if (resultado.Falhou)
                        Logger.LogWarning("Cobrança {CobrancaId} não conciliada: {Motivo}", cobranca.CobrancaId, resultado.PrimeiroErro.Mensagem);
                },
                ct
            );

        foreach (var formaturaId in aRenovar)
            await NaTurma(
                formaturaId,
                async sp =>
                {
                    var resultado = await sp.GetRequiredService<IProvedorDaTurmaService>().Renovar(ct);
                    if (resultado.Falhou)
                        Logger.LogWarning(
                            "Autorização do Mercado Pago da formatura {FormaturaId} não renovada: {Motivo}",
                            formaturaId,
                            resultado.PrimeiroErro.Mensagem
                        );
                },
                ct
            );
    }

    /// <summary>Roda o trabalho num escopo novo, apontado para a turma; a falha de uma não para as outras.</summary>
    private async Task NaTurma(Guid formaturaId, Func<IServiceProvider, Task> trabalho, CancellationToken ct)
    {
        try
        {
            using var escopo = scopeFactory.CreateScope();
            escopo.ServiceProvider.GetRequiredService<FormaturaDoProcessamento>().Apontar(formaturaId);

            await trabalho(escopo.ServiceProvider);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            Logger.LogError(excecao, "Falha no Mercado Pago da formatura {FormaturaId}. As demais seguem.", formaturaId);
        }
    }
}
