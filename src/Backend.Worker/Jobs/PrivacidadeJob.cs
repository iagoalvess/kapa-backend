using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Privacidade.Models;
using Backend.Worker.Configuration;

namespace Backend.Worker.Jobs;

/// <summary>
/// Atende o que os titulares pediram: gera as exportações e executa as eliminações vencidas.
/// </summary>
/// <remarks>
/// Mesma forma dos demais jobs — escopo por execução, roda ao subir, engole a exceção e segue. A
/// regra está em <see cref="IProcessamentoDePrivacidadeService"/>; aqui ficam o relógio, o log e o
/// escopo.
/// <para>
/// <b>Sem <c>FormaturaDoProcessamento</c></b>, e é a diferença para o job de relatórios: titular é
/// pessoa e atravessa turmas, então apontar o escopo para uma formatura esconderia metade dos dados
/// dele. O isolamento é substituído pelo recorte por <c>usuarioId</c>, que vem gravado na própria
/// solicitação.
/// </para>
/// <para>
/// Um escopo por solicitação, e não um para o lote: a eliminação escreve na conta do Identity e em
/// todos os cadastros da pessoa, e um <c>DbContext</c> compartilhado carregaria para a segunda
/// solicitação o rastreador já povoado pela primeira.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="lideranca">Trava que deixa só uma réplica rodar este job por vez.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class PrivacidadeJob(IServiceScopeFactory scopeFactory, LiderancaDeJob lideranca, ILogger<PrivacidadeJob> logger)
    : JobPeriodico(lideranca, logger)
{
    /// <summary>
    /// De quanto em quanto tempo a fila é olhada.
    /// </summary>
    /// <remarks>
    /// Meio minuto, e não cinco segundos como a fila de relatórios: ninguém está com a tela aberta
    /// esperando — a exportação avisa por e-mail, e a eliminação tem quinze dias de prazo. Uma
    /// passada vazia é uma consulta a um índice parcial que, na esmagadora maioria dos dias, tem
    /// zero linhas.
    /// </remarks>
    private static readonly TimeSpan IntervaloDaFila = TimeSpan.FromSeconds(30);

    /// <summary>Solicitações por passada. Cada uma abre o próprio escopo.</summary>
    private const int TamanhoDoLote = 5;

    /// <summary>De quantas em quantas passadas a limpeza dos pacotes vencidos roda.</summary>
    /// <remarks>Uma vez por hora: a validade é de dias, e varrer o índice junto com a fila não adianta nada.</remarks>
    private static readonly int PassadasEntreLimpezas = (int)(TimeSpan.FromHours(1) / IntervaloDaFila);

    /// <inheritdoc />
    protected override TimeSpan Intervalo => IntervaloDaFila;

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha no processamento de privacidade. A próxima execução tentará de novo.");

    /// <inheritdoc />
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        foreach (var pendente in await ListarPendentes(ct))
            await Processar(pendente, ct);

        if (passada % PassadasEntreLimpezas == 0)
            await Limpar(ct);
    }

    private async Task<IReadOnlyList<PrivacidadePendente>> ListarPendentes(CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        return await escopo.ServiceProvider.GetRequiredService<IProcessamentoDePrivacidadeService>().ListarPendentes(TamanhoDoLote, ct);
    }

    private async Task Processar(PrivacidadePendente pendente, CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        var atendida = await escopo.ServiceProvider.GetRequiredService<IProcessamentoDePrivacidadeService>().Processar(pendente.SolicitacaoId, ct);

        Logger.LogInformation(
            "Solicitação de privacidade {SolicitacaoId} ({Tipo}): {Resultado}.",
            pendente.SolicitacaoId,
            pendente.Tipo,
            atendida ? "atendida" : "não atendida"
        );
    }

    private async Task Limpar(CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        var expirados = await escopo.ServiceProvider.GetRequiredService<IProcessamentoDePrivacidadeService>().ExpirarVencidas(ct);

        if (expirados > 0)
            Logger.LogInformation("Limpeza de privacidade removeu {Expirados} pacotes vencidos.", expirados);
    }
}
