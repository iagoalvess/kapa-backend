using Backend.Business.Arquivos.Models;

namespace Backend.Api.Extensions;

/// <summary>
/// O arquivo que chega no multipart, traduzido para o pedido de envio do Business.
/// </summary>
public static class ArquivoDoFormulario
{
    /// <summary>O arquivo do multipart como pedido de envio; ausente ou vazio, nenhum.</summary>
    /// <remarks>
    /// Sai <b>sem categoria</b>: quem decide onde o arquivo mora é o service que o recebe — documento,
    /// comprovante de despesa ou de pagamento —, e não a borda. Antes cada controller escolhia a
    /// constante de um service concreto só para o service sobrescrevê-la.
    /// </remarks>
    /// <param name="arquivo">Campo de arquivo do formulário.</param>
    /// <param name="conteudo">Fluxo aberto do arquivo. Quem abriu é dono do descarte.</param>
    public static NovoArquivo? ParaNovoArquivo(this IFormFile? arquivo, Stream conteudo) =>
        arquivo is { Length: > 0 } ? new NovoArquivo(arquivo.FileName, arquivo.Length, conteudo, string.Empty) : null;
}
