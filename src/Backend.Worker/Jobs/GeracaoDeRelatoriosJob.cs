using Backend.Business.Abstractions;
using Backend.Business.Relatorios.Interfaces;
using Backend.Worker.Configuration;

namespace Backend.Worker.Jobs;

/// <summary>
/// De poucos em poucos segundos: gera os relatórios pedidos e apaga os que passaram do prazo.
/// </summary>
/// <remarks>
/// Mesma forma dos demais jobs — escopo por execução, roda ao subir, engole a exceção e segue. A
/// regra de gerar está em <see cref="IGeracaoDeRelatoriosService"/>; aqui ficam o relógio, o log e o
/// escopo.
/// <para>
/// <b>Um escopo por solicitação</b>, apontado para a formatura dela. É o que faz o filtro global
/// continuar valendo dentro do worker: sem isso, o balancete sairia vazio — ou, pior, o mesmo
/// <c>DbContext</c> atenderia duas turmas seguidas com o cache da primeira já povoado.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="lideranca">Trava que deixa só uma réplica rodar este job por vez.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class GeracaoDeRelatoriosJob(IServiceScopeFactory scopeFactory, LiderancaDeJob lideranca, ILogger<GeracaoDeRelatoriosJob> logger)
    : JobPeriodico(lideranca, logger)
{
    /// <summary>
    /// De quantos em quantos segundos a fila é olhada.
    /// </summary>
    /// <remarks>
    /// Curto de propósito, e agora de verdade: quem pediu o PDF está com a tela aberta esperando o
    /// arquivo baixar sozinho, e um minuto de espera com o aviso girando parece travamento — era a
    /// reclamação de que "o toast só fica gerando". Uma passada vazia é uma consulta a um índice
    /// parcial de cinco linhas, não um trabalho.
    /// </remarks>
    private static readonly TimeSpan IntervaloDaFila = TimeSpan.FromSeconds(5);

    /// <summary>Solicitações por passada. Cada uma abre o próprio escopo.</summary>
    private const int TamanhoDoLote = 5;

    /// <summary>De quantas em quantas passadas a limpeza dos vencidos roda.</summary>
    /// <remarks>Uma vez por hora: expiração é de dias, e varrer o índice junto com a fila não adianta nada.</remarks>
    private static readonly int PassadasEntreLimpezas = (int)(TimeSpan.FromHours(1) / IntervaloDaFila);

    /// <inheritdoc />
    protected override TimeSpan Intervalo => IntervaloDaFila;

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha na geração de relatórios. A próxima execução tentará de novo.");

    /// <inheritdoc />
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        var pendentes = await ListarPendentes(ct);

        foreach (var pendente in pendentes)
            await Gerar(pendente, ct);

        if (passada % PassadasEntreLimpezas == 0)
            await Limpar(ct);
    }

    private async Task<IReadOnlyList<RelatorioPendente>> ListarPendentes(CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        return await escopo.ServiceProvider.GetRequiredService<IGeracaoDeRelatoriosService>().ListarPendentes(TamanhoDoLote, ct);
    }

    /// <summary>Gera uma solicitação em escopo próprio, apontado para a formatura dela.</summary>
    /// <param name="pendente">Solicitação e a turma a que ela pertence.</param>
    private async Task Gerar(RelatorioPendente pendente, CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        escopo.ServiceProvider.GetRequiredService<FormaturaDoProcessamento>().Apontar(pendente.FormaturaId);

        var gerado = await escopo.ServiceProvider.GetRequiredService<IGeracaoDeRelatoriosService>().Gerar(pendente.SolicitacaoId, ct);

        Logger.LogInformation(
            "Relatório {SolicitacaoId} da formatura {FormaturaId}: {Resultado}.",
            pendente.SolicitacaoId,
            pendente.FormaturaId,
            gerado ? "gerado" : "não gerado"
        );
    }

    private async Task Limpar(CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        var expirados = await escopo.ServiceProvider.GetRequiredService<IGeracaoDeRelatoriosService>().ExpirarVencidas(ct);

        if (expirados > 0)
            Logger.LogInformation("Limpeza de relatórios removeu {Expirados} arquivos vencidos.", expirados);
    }
}
