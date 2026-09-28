using Backend.Business.Common.Datas;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Loja.Interfaces;
using Backend.Worker.Configuration;

namespace Backend.Worker.Jobs;

/// <summary>
/// Apaga documento e e-mail dos convidados 30 dias depois do evento (Sprint 21, P5.1) — e e-mail e CPF de
/// quem comprou na loja, 30 dias depois da festa (Sprint 26, decisão 5).
/// </summary>
/// <remarks>
/// O número do documento existe para a porta e para a lista que o salão pede; passada a festa, é dado
/// pessoal de terceiro — inclusive de criança — guardado sem finalidade. O nome fica: é o histórico da
/// festa, e é o que responde "quem entrou com o convite da Ana".
/// <para>
/// Uma vez por dia, atravessando as turmas numa instrução só: não há o que compor, e a janela de 30
/// dias torna irrelevante se roda às 3h ou às 15h.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="lideranca">Trava que deixa só uma réplica rodar este job por vez.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class DescarteDeDocumentosDeConvidadosJob(
    IServiceScopeFactory scopeFactory,
    LiderancaDeJob lideranca,
    ILogger<DescarteDeDocumentosDeConvidadosJob> logger
) : JobPeriodico(lideranca, logger)
{
    /// <summary>Quantos dias depois do evento os documentos ainda ficam.</summary>
    public const int DiasDepoisDoEvento = 30;

    /// <inheritdoc />
    protected override TimeSpan Intervalo { get; } = TimeSpan.FromHours(24);

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha no descarte de documentos de convidados. A próxima execução tentará de novo.");

    /// <inheritdoc />
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();
        var convites = escopo.ServiceProvider.GetRequiredService<IConviteDoEventoRepository>();

        var compras = escopo.ServiceProvider.GetRequiredService<ICompraDeConviteRepository>();
        var ate = DataUtils.Hoje().AddDays(-DiasDepoisDoEvento);

        var descartados = await convites.DescartarDocumentosDeTodasAsFormaturas(ate, ct);
        var compradores = await compras.DescartarDadosDeTodasAsFormaturas(ate, ct);

        if (descartados > 0)
            Logger.LogInformation("Descarte apagou documento e e-mail de {Descartados} convidados.", descartados);

        if (compradores > 0)
            Logger.LogInformation("Descarte apagou e-mail e CPF de {Compradores} compras da loja.", compradores);
    }
}
