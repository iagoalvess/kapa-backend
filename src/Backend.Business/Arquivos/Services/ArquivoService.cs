using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Arquivos.Settings;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.Arquivos.Services;

/// <summary>
/// Envio, download, listagem e remoção de arquivos.
/// </summary>
/// <param name="arquivoRepository">Metadados dos arquivos.</param>
/// <param name="armazenamento">Provedor que guarda os bytes.</param>
/// <param name="validator">Validador do pedido de envio.</param>
/// <param name="options">Limites e cotas configurados.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ArquivoService(
    IArquivoRepository arquivoRepository,
    IArmazenamentoDeArquivos armazenamento,
    IValidator<NovoArquivo> validator,
    IOptions<ArmazenamentoSettings> options,
    IUnitOfWork unitOfWork,
    ILogger<ArquivoService> logger
) : IArquivoService
{
    private static readonly Erro NaoEncontrado = Erro.NaoEncontrado("arquivo.nao_encontrado", "Arquivo não encontrado.");

    /// <summary>
    /// Tipo de conteúdo por extensão, para as extensões que o template já libera.
    /// </summary>
    /// <remarks>
    /// Só as que constam da lista de permissão padrão. Quem acrescentar uma extensão em
    /// <c>Armazenamento:ExtensoesPermitidas</c> e não acrescentar aqui recebe
    /// <c>application/octet-stream</c>, que faz o navegador baixar o arquivo — o comportamento
    /// seguro. O contrário, adivinhar o tipo, é o que cria o problema.
    /// </remarks>
    private static readonly Dictionary<string, string> TiposPorExtensao = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".csv"] = "text/csv",
        [".txt"] = "text/plain",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    /// <inheritdoc />
    /// <remarks>
    /// Grava os bytes **antes** do commit dos metadados. Se a gravação falhar, nada é registrado;
    /// se o commit falhar depois de gravar, sobra um objeto órfão no provedor — desperdício de
    /// espaço, e não um arquivo listado que não abre. Entre os dois erros possíveis, este é o
    /// barato.
    /// </remarks>
    public async Task<Result<ArquivoResumo>> Enviar(NovoArquivo dados, Guid enviadoPorId, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ArquivoResumo>(validacao.Erros);

        var cota = await ConferirCota(dados.Tamanho, enviadoPorId, ct);
        if (cota.Falhou)
            return Result.Falha<ArquivoResumo>(cota.Erros);

        var nome = Path.GetFileName(dados.Nome.Trim());

        var arquivo = new Arquivo
        {
            Nome = nome,
            ContentType = TipoDeConteudoDe(nome),
            Tamanho = dados.Tamanho,
            Categoria = dados.Categoria.Trim().ToLowerInvariant(),
            EnviadoPorId = enviadoPorId,
        };

        arquivo.Chave = MontarChave(arquivo, nome);

        await armazenamento.GravarAsync(arquivo.Chave, dados.Conteudo, arquivo.ContentType, ct);

        await arquivoRepository.Adicionar(arquivo, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Arquivo {ArquivoId} ({Tamanho} bytes) enviado por {UsuarioId}.", arquivo.Id, arquivo.Tamanho, enviadoPorId);

        return Result.Ok(ParaResumo(arquivo));
    }

    /// <inheritdoc />
    public async Task<Result<ArquivoParaDownload>> Baixar(Guid id, SolicitanteDeArquivo solicitante, CancellationToken ct = default)
    {
        var arquivo = await arquivoRepository.ObterPorId(id, ct);

        if (arquivo is null || !PodeAcessar(arquivo, solicitante))
            return NaoEncontrado;

        try
        {
            var conteudo = await armazenamento.AbrirLeituraAsync(arquivo.Chave, ct);

            return Result.Ok(new ArquivoParaDownload(conteudo, arquivo.Nome, arquivo.ContentType));
        }
        catch (FileNotFoundException excecao)
        {
            logger.LogError(excecao, "Arquivo {ArquivoId} está registrado mas o objeto {Chave} não existe no provedor.", id, arquivo.Chave);

            return Erro.NaoEncontrado("arquivo.conteudo_indisponivel", "O conteúdo deste arquivo não está disponível.");
        }
    }

    /// <inheritdoc />
    public async Task<Result<ArquivoResumo>> ObterPorId(Guid id, SolicitanteDeArquivo solicitante, CancellationToken ct = default)
    {
        var arquivo = await arquivoRepository.ObterPorId(id, ct);

        return arquivo is null || !PodeAcessar(arquivo, solicitante) ? NaoEncontrado : Result.Ok(ParaResumo(arquivo));
    }

    /// <inheritdoc />
    public async Task<Result<PaginaDe<ArquivoResumo>>> Listar(
        PaginacaoRequest paginacao,
        string? categoria,
        SolicitanteDeArquivo solicitante,
        CancellationToken ct = default
    )
    {
        var dono = solicitante.EhAdministrador ? (Guid?)null : solicitante.Id;

        var pagina = await arquivoRepository.Listar(paginacao.Normalizar(), categoria?.Trim().ToLowerInvariant(), dono, ct);

        return Result.Ok(pagina);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Remove o registro primeiro e o objeto depois. Falha ao apagar o objeto vira log, não erro:
    /// o resultado é um órfão no provedor, enquanto a ordem inversa poderia deixar um registro
    /// apontando para conteúdo inexistente — que é o que o usuário vê.
    /// </remarks>
    public async Task<Result> Remover(Guid id, SolicitanteDeArquivo solicitante, CancellationToken ct = default)
    {
        var arquivo = await arquivoRepository.ObterPorId(id, ct);

        if (arquivo is null || !PodeAcessar(arquivo, solicitante))
            return Result.Falha(NaoEncontrado);

        var chave = arquivo.Chave;

        arquivoRepository.Remover(arquivo);
        await unitOfWork.SalvarAsync(ct);

        try
        {
            await armazenamento.RemoverAsync(chave, ct);
        }
        catch (Exception excecao)
        {
            logger.LogError(excecao, "Registro do arquivo {ArquivoId} removido, mas o objeto {Chave} permaneceu no provedor.", id, chave);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Monta a chave do objeto no provedor.
    /// </summary>
    /// <remarks>
    /// <c>categoria/ano/mês/identificador.extensão</c>. O identificador vem do domínio e nunca do
    /// nome enviado: isso elimina de uma vez travessia de diretório, colisão entre arquivos de
    /// mesmo nome e caractere inválido no provedor. A divisão por ano e mês evita um único
    /// diretório com centenas de milhares de objetos.
    /// </remarks>
    private static string MontarChave(Arquivo arquivo, string nome) =>
        $"{arquivo.Categoria}/{arquivo.CriadoEm:yyyy/MM}/{arquivo.Id:N}{Path.GetExtension(nome).ToLowerInvariant()}";

    /// <summary>
    /// Recusa o envio que estouraria a cota do usuário.
    /// </summary>
    /// <remarks>
    /// Roda depois do validador e **antes** de gravar qualquer byte: o ponto da cota é justamente
    /// não escrever no provedor.
    /// <para>
    /// A conferência é otimista — entre a soma e a gravação cabe outro envio do mesmo usuário, e
    /// dois pedidos simultâneos podem passar juntos e ultrapassar o teto por um arquivo. Travar
    /// a linha do usuário para fechar essa fresta serializaria todo envio do sistema em troca de
    /// um excedente que o próximo envio já barra. Se o limite precisar ser rígido — cobrança por
    /// GB, cota contratual — o lugar é uma restrição no banco, não um lock aqui.
    /// </para>
    /// </remarks>
    /// <param name="tamanho">Tamanho do arquivo que está entrando.</param>
    /// <param name="enviadoPorId">Dono do envio.</param>
    /// <param name="ct">Token de cancelamento.</param>
    private async Task<Result> ConferirCota(long tamanho, Guid enviadoPorId, CancellationToken ct)
    {
        var settings = options.Value;

        if (settings.MaximoDeArquivosPorUsuario <= 0 && settings.CotaPorUsuarioEmMB <= 0)
            return Result.Ok();

        var uso = await arquivoRepository.ObterUsoDoUsuario(enviadoPorId, ct);

        if (settings.MaximoDeArquivosPorUsuario > 0 && uso.Quantidade >= settings.MaximoDeArquivosPorUsuario)
        {
            logger.LogInformation(
                "Envio recusado para {UsuarioId}: {Quantidade} arquivos, limite de {Limite}.",
                enviadoPorId,
                uso.Quantidade,
                settings.MaximoDeArquivosPorUsuario
            );

            return Result.Falha(
                Erro.Conflito(
                    "arquivo.limite_de_quantidade",
                    $"Você atingiu o limite de {settings.MaximoDeArquivosPorUsuario} arquivos. Remova algum antes de enviar outro."
                )
            );
        }

        if (settings.CotaPorUsuarioEmMB > 0 && uso.Bytes + tamanho > settings.CotaPorUsuarioEmBytes)
        {
            logger.LogInformation(
                "Envio recusado para {UsuarioId}: {Usado} bytes usados, cota de {Cota} bytes.",
                enviadoPorId,
                uso.Bytes,
                settings.CotaPorUsuarioEmBytes
            );

            return Result.Falha(
                Erro.Conflito(
                    "arquivo.cota_excedida",
                    $"Este arquivo ultrapassa sua cota de {settings.CotaPorUsuarioEmMB} MB. Remova outros arquivos para liberar espaço."
                )
            );
        }

        return Result.Ok();
    }

    /// <summary>
    /// Decide o tipo de conteúdo pela extensão, ignorando o que o cliente declarou.
    /// </summary>
    /// <remarks>
    /// O <c>Content-Type</c> do multipart é um cabeçalho que quem envia escolhe, e o que for
    /// gravado aqui é o que o download devolve. Aceitá-lo permitiria subir um <c>.txt</c>
    /// anunciado como <c>text/html</c> e servi-lo de volta a partir do domínio da API — script
    /// do atacante rodando na origem da aplicação. A extensão, ao contrário, já foi conferida
    /// contra a lista de permissão.
    /// </remarks>
    /// <param name="nome">Nome do arquivo, já sem componente de diretório.</param>
    private static string TipoDeConteudoDe(string nome) => TiposPorExtensao.GetValueOrDefault(Path.GetExtension(nome), "application/octet-stream");

    /// <summary>
    /// Resposta idêntica para arquivo inexistente e para arquivo de terceiro.
    /// </summary>
    /// <remarks>
    /// Devolver 403 no segundo caso confirmaria a existência do arquivo, transformando o endpoint
    /// num verificador de identificadores válidos.
    /// </remarks>
    private static bool PodeAcessar(Arquivo arquivo, SolicitanteDeArquivo solicitante) =>
        solicitante.EhAdministrador || arquivo.EnviadoPorId == solicitante.Id;

    private static ArquivoResumo ParaResumo(Arquivo arquivo) =>
        new(arquivo.Id, arquivo.Nome, arquivo.ContentType, arquivo.Tamanho, arquivo.Categoria, arquivo.EnviadoPorId, arquivo.CriadoEm);
}
