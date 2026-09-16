using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Relatorios.Models;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Relatorios.Services;

/// <summary>
/// Gera o que está na fila: monta o PDF e guarda o arquivo.
/// </summary>
/// <remarks>
/// A regra mora aqui, e não no job (decisão 2): o job abre o escopo, respeita o intervalo e escreve
/// o log — coisas de host. Esta classe é exercitável por teste sem subir worker nenhum.
/// <para>
/// <b>Falha não perde o pedido.</b> A tentativa é contada antes de gerar, e o motivo fica gravado;
/// esgotadas as tentativas a linha vira <see cref="StatusDaSolicitacao.Falhou"/> com o motivo à
/// vista — a tela consulta a fila de poucos em poucos segundos e mostra os dois desfechos. Nada
/// disso sai por e-mail: quem pediu está com a tela aberta esperando.
/// </para>
/// </remarks>
/// <param name="solicitacaoRepository">A fila.</param>
/// <param name="relatorioService">Quem monta o balancete.</param>
/// <param name="arquivoService">Onde os bytes ficam.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class GeracaoDeRelatoriosService(
    ISolicitacaoDeRelatorioRepository solicitacaoRepository,
    IRelatorioService relatorioService,
    IArquivoService arquivoService,
    IUnitOfWork unitOfWork,
    ILogger<GeracaoDeRelatoriosService> logger
) : IGeracaoDeRelatoriosService
{
    /// <summary>Categoria dos arquivos gerados, no módulo de arquivos.</summary>
    public const string CategoriaDoArquivo = "relatorios";

    /// <summary>Solicitações vencidas limpas por passada.</summary>
    private const int LoteDaExpiracao = 100;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RelatorioPendente>> ListarPendentes(int quantidade, CancellationToken ct = default) =>
        [.. (await solicitacaoRepository.ListarNaFilaDeTodasAsFormaturas(quantidade, ct)).Select(s => new RelatorioPendente(s.Id, s.FormaturaId))];

    /// <inheritdoc />
    public async Task<bool> Gerar(Guid solicitacaoId, CancellationToken ct = default)
    {
        var solicitacao = await solicitacaoRepository.Obter(solicitacaoId, ct);

        if (solicitacao is null || solicitacao.Status != StatusDaSolicitacao.NaFila)
            return false;

        solicitacao.Tentar();

        try
        {
            var pdf = await Montar(solicitacao, ct);

            if (pdf.Falhou)
                return await Desistir(solicitacao, pdf.PrimeiroErro.Mensagem, ct);

            using var conteudo = new MemoryStream(pdf.Valor);

            var arquivo = await arquivoService.Enviar(
                new NovoArquivo(NomeDoArquivo(solicitacao), conteudo.Length, conteudo, CategoriaDoArquivo),
                solicitacao.SolicitadaPorUsuarioId,
                ct
            );

            if (arquivo.Falhou)
                return await Desistir(solicitacao, arquivo.PrimeiroErro.Mensagem, ct);

            solicitacao.Concluir(arquivo.Valor.Id, DateTime.UtcNow);

            await unitOfWork.SalvarAsync(ct);

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            logger.LogError(excecao, "Falha ao gerar o relatório {SolicitacaoId}.", solicitacao.Id);

            return await Desistir(solicitacao, "Erro inesperado ao montar o arquivo.", ct);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Apaga os bytes e zera a referência: a linha fica, porque ela é o registro de que o relatório
    /// foi pedido, e a tela precisa dizer "expirou" em vez de sumir com o item.
    /// </remarks>
    public async Task<int> ExpirarVencidas(CancellationToken ct = default)
    {
        var vencidas = await solicitacaoRepository.ListarVencidasDeTodasAsFormaturas(DateTime.UtcNow, LoteDaExpiracao, ct);

        foreach (var solicitacao in vencidas)
        {
            var remocao = await arquivoService.Remover(
                solicitacao.ArquivoId!.Value,
                new SolicitanteDeArquivo(solicitacao.SolicitadaPorUsuarioId, EhAdministrador: false),
                ct
            );

            if (remocao.Falhou)
                logger.LogWarning(
                    "Relatório {SolicitacaoId} expirou mas o arquivo não foi apagado: {Codigo}.",
                    solicitacao.Id,
                    remocao.PrimeiroErro.Codigo
                );

            solicitacao.Expirar();
        }

        if (vencidas.Count > 0)
            await unitOfWork.SalvarAsync(ct);

        return vencidas.Count;
    }

    /// <summary>
    /// O PDF do tipo pedido.
    /// </summary>
    /// <remarks>
    /// O balancete tem documento próprio — tem capa, resumo e três quadros. Os outros três são
    /// listas, e saem da mesma rotina padrão que vira planilha, pelo <see cref="RelatorioEmPdf"/>.
    /// </remarks>
    /// <param name="solicitacao">Pedido.</param>
    private async Task<Result<byte[]>> Montar(SolicitacaoDeRelatorio solicitacao, CancellationToken ct)
    {
        if (solicitacao.Tipo == TipoDeRelatorio.Balancete)
        {
            var balancete = await relatorioService.Balancete(solicitacao.FormaturaId, solicitacao.Periodo, solicitacao.SolicitadaPorUsuarioId, ct);

            return balancete.Falhou ? Result.Falha<byte[]>(balancete.Erros) : BalanceteEmPdf.Gerar(balancete.Valor);
        }

        var tabela = await relatorioService.Tabela(solicitacao.FormaturaId, solicitacao.Tipo, solicitacao.Filtro, ct);

        return tabela.Falhou ? Result.Falha<byte[]>(tabela.Erros) : RelatorioEmPdf.Gerar(tabela.Valor);
    }

    /// <summary>
    /// Nome que o navegador sugere no download: <c>balancete-2026-01-01-a-2026-09-15.pdf</c>.
    /// </summary>
    /// <remarks>Data em ISO para o arquivo ordenar sozinho na pasta de Downloads.</remarks>
    /// <param name="solicitacao">Pedido.</param>
    private static string NomeDoArquivo(SolicitacaoDeRelatorio solicitacao) =>
        $"{solicitacao.Tipo.ToString().ToLowerInvariant()}-{Iso(solicitacao.De)}-a-{Iso(solicitacao.Ate)}.pdf";

    private static string Iso(DateOnly dia) => dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Grava a falha e o motivo; a tela o mostra na própria fila.</summary>
    /// <param name="solicitacao">Pedido.</param>
    /// <param name="motivo">O que aconteceu.</param>
    private async Task<bool> Desistir(SolicitacaoDeRelatorio solicitacao, string motivo, CancellationToken ct)
    {
        solicitacao.Falhar(motivo);

        await unitOfWork.SalvarAsync(ct);

        return false;
    }
}
