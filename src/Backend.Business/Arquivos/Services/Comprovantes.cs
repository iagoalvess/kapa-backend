using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;

namespace Backend.Business.Arquivos.Services;

/// <summary>
/// A ida e a volta de um comprovante — o da despesa e o do aviso de pagamento.
/// </summary>
/// <remarks>
/// O caminho é o mesmo nos dois, e é o que mora aqui. A lista de extensões, a categoria e o código do
/// erro continuam com cada feature: são duas decisões que podem divergir, não uma regra em dois lugares.
/// </remarks>
public static class Comprovantes
{
    /// <summary>Grava o comprovante, se veio; devolve o id do arquivo, ou nulo.</summary>
    /// <param name="arquivoService">Módulo de arquivos.</param>
    /// <param name="comprovante">Arquivo recebido, sem categoria; nulo quando não veio.</param>
    /// <param name="categoria">Onde o comprovante desta feature mora.</param>
    /// <param name="extensoes">O que esta feature aceita como comprovante.</param>
    /// <param name="tipoInvalido">A falha quando a extensão não está na lista.</param>
    /// <param name="usuarioId">Quem envia.</param>
    public static async Task<Result<Guid?>> EnviarComprovante(
        this IArquivoService arquivoService,
        NovoArquivo? comprovante,
        string categoria,
        IReadOnlySet<string> extensoes,
        Erro tipoInvalido,
        Guid usuarioId,
        CancellationToken ct = default
    )
    {
        if (comprovante is null)
            return Result.Ok<Guid?>(null);

        if (!extensoes.Contains(Path.GetExtension(comprovante.Nome)))
            return tipoInvalido;

        var arquivo = await arquivoService.Enviar(comprovante with { Categoria = categoria }, usuarioId, ct);

        return arquivo.Falhou ? Result.Falha<Guid?>(arquivo.Erros) : arquivo.Valor.Id;
    }

    /// <summary>Baixa o comprovante <b>como quem o enviou</b>.</summary>
    /// <remarks>
    /// No módulo de arquivos a regra é "cada um vê os próprios"; quem pode ver o comprovante da turma é
    /// decisão da feature, que já conferiu a política e achou o registro nesta formatura.
    /// </remarks>
    /// <param name="arquivoService">Módulo de arquivos.</param>
    /// <param name="arquivoId">Arquivo do comprovante.</param>
    /// <param name="enviadoPorUsuarioId">Quem o enviou.</param>
    public static Task<Result<ArquivoParaDownload>> BaixarComprovante(
        this IArquivoService arquivoService,
        Guid arquivoId,
        Guid enviadoPorUsuarioId,
        CancellationToken ct = default
    ) => arquivoService.Baixar(arquivoId, new SolicitanteDeArquivo(enviadoPorUsuarioId, PeloSistema: false), ct);

    /// <summary>Apaga o comprovante já gravado quando a operação que ele sustentava não aconteceu.</summary>
    /// <remarks>
    /// O arquivo é gravado e registrado antes da operação — a trava da parcela só vem depois —, então
    /// quem perde a corrida ficaria com um comprovante que nada referencia. Falha ao apagar vira log no
    /// próprio <see cref="IArquivoService.Remover"/>; a resposta ao usuário continua sendo a da operação.
    /// </remarks>
    /// <param name="arquivoService">Módulo de arquivos.</param>
    /// <param name="arquivoId">Arquivo do comprovante; nulo quando não veio.</param>
    /// <param name="enviadoPorUsuarioId">Quem o enviou.</param>
    public static async Task DescartarComprovante(
        this IArquivoService arquivoService,
        Guid? arquivoId,
        Guid enviadoPorUsuarioId,
        CancellationToken ct = default
    )
    {
        if (arquivoId is { } id)
            await arquivoService.Remover(id, new SolicitanteDeArquivo(enviadoPorUsuarioId, PeloSistema: false), ct);
    }
}
