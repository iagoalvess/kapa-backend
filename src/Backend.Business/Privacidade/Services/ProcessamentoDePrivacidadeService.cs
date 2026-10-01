using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Privacidade.Models;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Privacidade.Services;

/// <summary>
/// O lado do worker: gera o pacote da exportação e executa a eliminação vencida.
/// </summary>
/// <remarks>
/// A regra mora aqui e o relógio mora no job, como em <c>GeracaoDeRelatoriosService</c>. Falha não
/// perde o pedido: a tentativa é contada antes, o motivo fica gravado, e esgotadas as tentativas a
/// linha vira <c>Falhou</c> com o motivo à vista. Pedido de titular parado em "pendente" para sempre
/// é o que vira reclamação na ANPD.
/// <para>
/// Este service **não** abre escopo por formatura, e é a diferença para o de relatórios: o titular
/// atravessa turmas, então tudo aqui usa as consultas com sufixo <c>DeTodasAsFormaturas</c>.
/// </para>
/// </remarks>
/// <param name="privacidadeRepository">A fila e os dados do titular.</param>
/// <param name="anonimizacao">Quem torna o titular irreconhecível.</param>
/// <param name="arquivoService">Onde o pacote mora.</param>
/// <param name="emails">Avisos ao titular.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ProcessamentoDePrivacidadeService(
    IPrivacidadeRepository privacidadeRepository,
    IAnonimizacaoDeTitular anonimizacao,
    IArquivoService arquivoService,
    EmailsDePrivacidade emails,
    IUnitOfWork unitOfWork,
    ILogger<ProcessamentoDePrivacidadeService> logger
) : IProcessamentoDePrivacidadeService
{
    /// <summary>Categoria dos pacotes de exportação no módulo de arquivos.</summary>
    public const string CategoriaDoArquivo = "exportacoes-lgpd";

    /// <summary>Pacotes vencidos limpos por passada.</summary>
    private const int LoteDaExpiracao = 100;

    /// <inheritdoc />
    public Task<IReadOnlyList<PrivacidadePendente>> ListarPendentes(int quantidade, CancellationToken ct = default) =>
        privacidadeRepository.ListarVencidas(DateTime.UtcNow, quantidade, ct);

    /// <inheritdoc />
    public async Task<bool> Processar(Guid solicitacaoId, CancellationToken ct = default)
    {
        var solicitacao = await privacidadeRepository.Obter(solicitacaoId, ct);

        if (solicitacao is null || solicitacao.Status != StatusDaSolicitacaoDePrivacidade.Pendente)
            return false;

        solicitacao.Tentar();

        try
        {
            return solicitacao.Tipo == TipoDeSolicitacao.Exportacao ? await Exportar(solicitacao, ct) : await Eliminar(solicitacao, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            logger.LogError(excecao, "Falha ao processar a solicitação de privacidade {SolicitacaoId}.", solicitacao.Id);

            return await Desistir(solicitacao, "Erro inesperado ao processar a solicitação.", ct);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Apaga os bytes e zera a referência; a linha fica, porque ela é o registro de que o titular
    /// exerceu o direito — e é esse registro que a Kapa precisa ter para provar que respondeu.
    /// </remarks>
    public async Task<int> ExpirarVencidas(CancellationToken ct = default)
    {
        var vencidas = await privacidadeRepository.ListarArquivosVencidos(DateTime.UtcNow, LoteDaExpiracao, ct);

        foreach (var solicitacao in vencidas)
        {
            var remocao = await arquivoService.Remover(
                solicitacao.ArquivoId!.Value,
                new SolicitanteDeArquivo(solicitacao.TitularUsuarioId, PeloSistema: true),
                ct
            );

            if (remocao.Falhou)
                logger.LogWarning(
                    "Exportação {SolicitacaoId} expirou mas o pacote não foi apagado: {Codigo}.",
                    solicitacao.Id,
                    remocao.PrimeiroErro.Codigo
                );

            solicitacao.Expirar();
        }

        if (vencidas.Count > 0)
            await unitOfWork.SalvarAsync(ct);

        return vencidas.Count;
    }

    /// <summary>Monta o pacote, guarda e avisa o titular.</summary>
    /// <param name="solicitacao">Pedido de exportação.</param>
    private async Task<bool> Exportar(SolicitacaoDePrivacidade solicitacao, CancellationToken ct)
    {
        var dados = await privacidadeRepository.ObterMeusDadosDeTodasAsFormaturas(solicitacao.TitularUsuarioId, ct);

        if (dados is null)
            return await Desistir(solicitacao, "Titular não encontrado.", ct);

        var agora = DateTime.UtcNow;

        using var conteudo = new MemoryStream(PacoteDeDados.Gerar(dados, agora));

        var arquivo = await arquivoService.Enviar(
            new NovoArquivo(NomeDoPacote(agora), conteudo.Length, conteudo, CategoriaDoArquivo),
            solicitacao.TitularUsuarioId,
            ct
        );

        if (arquivo.Falhou)
            return await Desistir(solicitacao, arquivo.PrimeiroErro.Mensagem, ct);

        solicitacao.ConcluirExportacao(arquivo.Valor, agora);

        await emails.ExportacaoPronta(dados.Conta.Email, SolicitacaoDePrivacidade.DiasDeValidadeDoArquivo, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Exportação {SolicitacaoId} gerada para o titular {UsuarioId}.", solicitacao.Id, solicitacao.TitularUsuarioId);

        return true;
    }

    /// <summary>
    /// Anonimiza o titular e avisa — nesta ordem de escrita, e na ordem inversa de leitura.
    /// </summary>
    /// <remarks>
    /// O e-mail do titular é lido <b>antes</b> da anonimização, porque depois dela o endereço já não
    /// está na conta; e é enfileirado <b>depois</b> dela, para que um erro na anonimização não deixe
    /// a pessoa recebendo "seus dados foram eliminados" sobre dados que continuam lá.
    /// <para>
    /// São duas gravações, e não uma: a anonimização comita por conta própria (ela mexe no Identity,
    /// que persiste sozinho). Se a segunda cair, a pessoa fica anonimizada com a solicitação ainda
    /// pendente — e a próxima passada do worker a fecha, porque <c>Anonimizar</c> é idempotente. O
    /// contrário, marcar a solicitação como atendida sem anonimizar, é que não pode acontecer.
    /// </para>
    /// </remarks>
    /// <param name="solicitacao">Pedido de eliminação.</param>
    private async Task<bool> Eliminar(SolicitacaoDePrivacidade solicitacao, CancellationToken ct)
    {
        var titular = await privacidadeRepository.ObterTitular(solicitacao.TitularUsuarioId, ct);

        if (titular is null)
            return await Desistir(solicitacao, "Titular não encontrado.", ct);

        var anonimizado = await anonimizacao.Anonimizar(solicitacao.TitularUsuarioId, ct);

        if (anonimizado.Falhou)
            return await Desistir(solicitacao, anonimizado.PrimeiroErro.Mensagem, ct);

        solicitacao.ConcluirExclusao(DateTime.UtcNow);

        await emails.ExclusaoConcluida(titular.Email, anonimizado.Valor, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogWarning("Eliminação {SolicitacaoId} atendida: titular {UsuarioId} anonimizado.", solicitacao.Id, solicitacao.TitularUsuarioId);

        return true;
    }

    /// <summary>
    /// Nome que o navegador sugere: <c>meus-dados-kapa-2026-09-17.zip</c>.
    /// </summary>
    /// <remarks>
    /// Sem o nome nem o identificador do titular: o arquivo vai parar na pasta de Downloads de um
    /// computador que pode ser compartilhado, e o nome de um arquivo é a única parte dele que
    /// aparece sem ninguém abrir.
    /// </remarks>
    /// <param name="agora">Momento da geração.</param>
    private static string NomeDoPacote(DateTime agora) => $"meus-dados-kapa-{agora:yyyy-MM-dd}{PacoteDeDados.Extensao}";

    /// <summary>Grava a falha e o motivo; a tela os mostra na própria fila.</summary>
    /// <param name="solicitacao">Pedido.</param>
    /// <param name="motivo">O que aconteceu.</param>
    private async Task<bool> Desistir(SolicitacaoDePrivacidade solicitacao, string motivo, CancellationToken ct)
    {
        solicitacao.Falhar(motivo);

        await unitOfWork.SalvarAsync(ct);

        return false;
    }
}
