using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Arquivos.Services;

namespace Backend.Business.Pagamentos.Services;

/// <summary>
/// O comprovante de pagamento — o do aviso do formando e o da baixa manual da tesouraria.
/// </summary>
public static class ComprovanteDePagamento
{
    /// <summary>Categoria dos comprovantes no módulo de arquivos.</summary>
    public const string Categoria = "comprovantes";

    /// <summary>
    /// Comprovante é PDF ou imagem.
    /// </summary>
    /// <remarks>
    /// Planilha e texto passam no módulo de arquivos, mas não comprovam pagamento — e comprovante sem
    /// restrição de tipo vira depósito de arquivos. Mesma lista do comprovante da despesa (Sprint 10),
    /// repetida de propósito: são duas decisões que podem divergir, não uma regra em dois lugares.
    /// </remarks>
    private static readonly HashSet<string> Extensoes = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".png", ".jpg", ".jpeg", ".webp" };

    private static readonly Erro Invalido = Erro.Validacao(
        "pagamento.comprovante_invalido",
        "Envie o comprovante em PDF ou imagem (PNG, JPG ou WebP).",
        "comprovante"
    );

    /// <summary>Grava o comprovante, se veio; devolve o id do arquivo, ou nulo.</summary>
    /// <param name="arquivoService">Módulo de arquivos.</param>
    /// <param name="comprovante">Arquivo recebido; nulo quando não veio.</param>
    /// <param name="usuarioId">Quem envia.</param>
    public static Task<Result<Guid?>> Enviar(IArquivoService arquivoService, NovoArquivo? comprovante, Guid usuarioId, CancellationToken ct) =>
        arquivoService.EnviarComprovante(comprovante, Categoria, Extensoes, Invalido, usuarioId, ct);
}
