using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Services;
using Backend.Business.Formaturas.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Comunicacao.Services;

/// <summary>
/// O acervo da turma: a comissão envia, corrige, substitui e exclui; todo membro baixa o que pode ver.
/// </summary>
/// <remarks>
/// O arquivo é gravado pelo módulo de arquivos, em nome de quem enviou. É como quem enviou que este
/// service pede a URL e a remoção depois — lá a regra é "cada um vê os próprios", e quem pode ver o
/// documento da turma é decisão daqui: a política já passou, a formatura já filtrou, e a visibilidade
/// o repositório aplicou. O mesmo caminho do comprovante da despesa.
/// </remarks>
/// <param name="documentoRepository">Documentos da turma.</param>
/// <param name="vinculoRepository">Papel de quem consulta.</param>
/// <param name="arquivoService">Bytes dos documentos.</param>
/// <param name="eventos">Auditoria da substituição e da exclusão.</param>
/// <param name="validator">Forma do documento.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class DocumentoService(
    IDocumentoRepository documentoRepository,
    IVinculoRepository vinculoRepository,
    IArquivoService arquivoService,
    IEventoRepository eventos,
    IValidator<DadosDoDocumento> validator,
    IUnitOfWork unitOfWork,
    ILogger<DocumentoService> logger
) : IDocumentoService
{
    /// <summary>Categoria dos documentos no módulo de arquivos.</summary>
    public const string CategoriaDoArquivo = "documentos";

    /// <summary>Evento da troca do arquivo, com o que saiu e o que entrou.</summary>
    public const string EventoDeSubstituicao = "comunicacao.documento_substituido";

    /// <summary>Evento da exclusão, com quem excluiu e o que foi excluído.</summary>
    public const string EventoDeExclusao = "comunicacao.documento_excluido";

    /// <summary>
    /// Quanto a URL de download vale.
    /// </summary>
    /// <remarks>
    /// Minutos: o suficiente para o navegador seguir o redirecionamento e baixar um arquivo de 20 MB
    /// numa conexão ruim; curto o bastante para a URL copiada de um histórico não servir de nada.
    /// </remarks>
    public static readonly TimeSpan ValidadeDaUrl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// O que o acervo aceita: PDF, imagem, Word e Excel.
    /// </summary>
    /// <remarks>
    /// Menos que a lista do módulo de arquivos: texto e CSV não têm assinatura de bytes para conferir,
    /// e ata, contrato e orçamento chegam nestes formatos.
    /// </remarks>
    private static readonly HashSet<string> Extensoes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".docx",
        ".xlsx",
    };

    private static readonly Erro NaoEncontrado = Erro.NaoEncontrado("comunicacao.documento_nao_encontrado", "Documento não encontrado.");

    /// <inheritdoc />
    public async Task<Result<PaginaDe<DocumentoResumo>>> Listar(
        Guid formaturaId,
        Guid usuarioId,
        PaginacaoRequest paginacao,
        FiltroDeDocumentos filtro,
        CancellationToken ct = default
    )
    {
        var papel = await Papel(formaturaId, usuarioId, ct);

        return Result.Ok(await documentoRepository.Listar(paginacao.Normalizar(), filtro, papel, ct));
    }

    /// <inheritdoc />
    public async Task<Result<ResumoDoAcervo>> Resumir(Guid formaturaId, Guid usuarioId, CancellationToken ct = default) =>
        await documentoRepository.Resumir(await Papel(formaturaId, usuarioId, ct), ct);

    /// <inheritdoc />
    /// <remarks>
    /// O arquivo é gravado antes do documento: se a gravação do documento falhar depois, sobra um
    /// arquivo órfão — nunca um documento apontando para arquivo que não existe.
    /// </remarks>
    public async Task<Result<DocumentoResumo>> Enviar(
        Guid formaturaId,
        Guid usuarioId,
        DadosDoDocumento dados,
        NovoArquivo? arquivo,
        CancellationToken ct = default
    )
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<DocumentoResumo>(validacao.Erros);

        if (arquivo is null)
            return Erro.Validacao("comunicacao.arquivo_obrigatorio", "Anexe o arquivo do documento.", "arquivo");

        var gravado = await Gravar(arquivo, usuarioId, ct);
        if (gravado.Falhou)
            return Result.Falha<DocumentoResumo>(gravado.Erros);

        var documento = Documento.Novo(dados, gravado.Valor);

        await documentoRepository.Adicionar(documento, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation(
            "Documento {DocumentoId} enviado por {UsuarioId} para {Visibilidade}.",
            documento.Id,
            usuarioId,
            documento.Visibilidade
        );

        return await Detalhar(documento.Id, formaturaId, usuarioId, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Na substituição, o arquivo antigo é apagado só depois do commit: se o commit falhar, o documento
    /// continua apontando para um arquivo que existe. A auditoria guarda o nome do que saiu — é o único
    /// rastro dele.
    /// </remarks>
    public async Task<Result<DocumentoResumo>> Atualizar(
        Guid formaturaId,
        Guid usuarioId,
        Guid id,
        DadosDoDocumento dados,
        NovoArquivo? arquivo,
        CancellationToken ct = default
    )
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<DocumentoResumo>(validacao.Erros);

        var documento = await documentoRepository.ObterParaEdicao(id, ct);
        if (documento is null)
            return NaoEncontrado;

        ArquivoDoDocumento? anterior = null;

        if (arquivo is not null)
        {
            anterior = await documentoRepository.ObterArquivo(id, await Papel(formaturaId, usuarioId, ct), ct);
            if (anterior is null)
                return NaoEncontrado;

            var gravado = await Gravar(arquivo, usuarioId, ct);
            if (gravado.Falhou)
                return Result.Falha<DocumentoResumo>(gravado.Erros);

            var versaoAnterior = documento.Versao;
            documento.Substituir(gravado.Valor);

            await eventos.Auditar(
                EventoDeSubstituicao,
                usuarioId,
                new
                {
                    formaturaId,
                    documentoId = id,
                    documento.Titulo,
                    versaoAnterior,
                    documento.Versao,
                    arquivoAnterior = new { id = anterior.ArquivoId, nome = anterior.Nome },
                    arquivoNovo = new { id = gravado.Valor, nome = Path.GetFileName(arquivo.Nome.Trim()) },
                },
                ct
            );
        }

        documento.Aplicar(dados);
        await unitOfWork.SalvarAsync(ct);

        if (anterior is not null)
        {
            await Apagar(anterior, ct);
            logger.LogInformation("Documento {DocumentoId} substituído por {UsuarioId}; versão {Versao}.", id, usuarioId, documento.Versao);
        }

        return await Detalhar(id, formaturaId, usuarioId, ct);
    }

    /// <inheritdoc />
    /// <remarks>Mesma ordem da substituição: auditoria junto da exclusão, e o arquivo apagado depois do commit.</remarks>
    public async Task<Result> Excluir(Guid formaturaId, Guid usuarioId, Guid id, CancellationToken ct = default)
    {
        var documento = await documentoRepository.ObterParaEdicao(id, ct);
        if (documento is null)
            return Result.Falha(NaoEncontrado);

        var arquivo = await documentoRepository.ObterArquivo(id, await Papel(formaturaId, usuarioId, ct), ct);

        documentoRepository.Remover(documento);
        await eventos.Auditar(
            EventoDeExclusao,
            usuarioId,
            new
            {
                formaturaId,
                documentoId = id,
                documento.Titulo,
                documento.Categoria,
                documento.Visibilidade,
                documento.Versao,
                arquivo = arquivo is null ? null : new { id = arquivo.ArquivoId, nome = arquivo.Nome },
            },
            ct
        );
        await unitOfWork.SalvarAsync(ct);

        if (arquivo is not null)
            await Apagar(arquivo, ct);

        logger.LogInformation("Documento {DocumentoId} excluído por {UsuarioId}.", id, usuarioId);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result<string>> Baixar(Guid formaturaId, Guid usuarioId, Guid id, CancellationToken ct = default)
    {
        var arquivo = await documentoRepository.ObterArquivo(id, await Papel(formaturaId, usuarioId, ct), ct);
        if (arquivo is null)
            return NaoEncontrado;

        return await arquivoService.GerarUrlTemporaria(
            arquivo.ArquivoId,
            new SolicitanteDeArquivo(arquivo.EnviadoPorId, PeloSistema: false),
            ValidadeDaUrl,
            ct
        );
    }

    /// <summary>
    /// Confere tipo e tamanho e grava o arquivo; devolve o id dele.
    /// </summary>
    /// <remarks>
    /// Só o que é específico do acervo: a lista de extensões dele, mais estreita que a do módulo de
    /// arquivos, e o teto de tamanho dele, menor que o global. A conferência dos primeiros bytes
    /// contra a extensão é do <c>ArquivoService</c>, por onde todo envio passa.
    /// </remarks>
    private async Task<Result<Guid>> Gravar(NovoArquivo arquivo, Guid usuarioId, CancellationToken ct)
    {
        if (!Extensoes.Contains(Path.GetExtension(arquivo.Nome)))
            return Erro.Validacao(
                "comunicacao.tipo_invalido",
                "Envie o documento em PDF, imagem (PNG, JPG ou WebP), Word (.docx) ou Excel (.xlsx).",
                "arquivo"
            );

        if (arquivo.Tamanho > Documento.TamanhoMaximoEmBytes)
            return Erro.Validacao("comunicacao.arquivo_grande", $"O documento excede o limite de {Documento.TamanhoMaximoEmMB} MB.", "arquivo");

        var enviado = await arquivoService.Enviar(arquivo with { Categoria = CategoriaDoArquivo }, usuarioId, ct);

        return enviado.Falhou ? Result.Falha<Guid>(enviado.Erros) : enviado.Valor;
    }

    /// <summary>Apaga o arquivo que saiu do acervo. Falhar vira log: sobra um órfão, e o documento já está certo.</summary>
    private async Task Apagar(ArquivoDoDocumento arquivo, CancellationToken ct)
    {
        var remocao = await arquivoService.Remover(arquivo.ArquivoId, new SolicitanteDeArquivo(arquivo.EnviadoPorId, PeloSistema: false), ct);

        if (remocao.Falhou)
            logger.LogWarning("Arquivo {ArquivoId} saiu do acervo mas não foi apagado: {Codigo}.", arquivo.ArquivoId, remocao.PrimeiroErro.Codigo);
    }

    /// <summary>O documento como a lista o mostra, depois de uma escrita.</summary>
    private async Task<Result<DocumentoResumo>> Detalhar(Guid id, Guid formaturaId, Guid usuarioId, CancellationToken ct) =>
        await documentoRepository.Obter(id, await Papel(formaturaId, usuarioId, ct), ct) is { } documento ? documento : NaoEncontrado;

    private Task<string?> Papel(Guid formaturaId, Guid usuarioId, CancellationToken ct) =>
        vinculoRepository.ObterPapelAtivo(usuarioId, formaturaId, ct);
}
