using System.Globalization;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Pdf;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Festa.Models;
using QRCoder;

namespace Backend.Business.Festa.Services;

/// <summary>
/// O convite e a lista da portaria em PDF, remontados a cada pedido (decisão 10).
/// </summary>
/// <remarks>
/// Nada é guardado: os dados estão gravados e o <see cref="DocumentoPdf"/> é determinístico. O QR
/// carrega a mesma URL que a tela mostra (decisão 3) — a câmera nativa do celular abre a página, e o
/// PDF impresso e a tela levam ao mesmo lugar.
/// <para>
/// O <c>QRCoder</c> entra só para dar a matriz de módulos; quem desenha é o documento, como vetor
/// (decisão 9). Correção de erro nível M: aguenta dobra e mancha de papel sem inflar o QR.
/// </para>
/// </remarks>
public static class ConviteEmPdf
{
    /// <summary>Lado do QR na folha, em pontos — uns 8 cm, lido de longe por qualquer câmera.</summary>
    private const float LadoDoQr = 220;

    /// <summary>Proporção das colunas da lista: convidado, documento, código, de quem, situação.</summary>
    private static readonly (float, bool)[] ColunasDaLista = [(26, false), (19, false), (13, false), (20, false), (22, false)];

    /// <summary>O convite de uma pessoa.</summary>
    /// <param name="convite">O convite como a página pública o mostra — documento já mascarado.</param>
    /// <param name="url">A URL da página do convite, que vai no QR.</param>
    public static byte[] Gerar(ConvitePublico convite, string url)
    {
        var evento = convite.Evento;
        var quandoEOnde = evento.Local is { } local ? $"{Quando(evento)} · {local}" : Quando(evento);
        var pdf = new DocumentoPdf()
            .Capa(evento.Titulo, quandoEOnde, $"{convite.Turma} · {convite.Instituicao}", Mascote.Acenando)
            .MatrizDeModulos(Modulos(url), LadoDoQr, centralizado: true)
            .Destaque(convite.Codigo, centralizado: true);

        pdf.Paragrafo($"Convidado: {convite.NomeDoConvidado}", negrito: true, centralizado: true);

        if (convite.Documento is { } documento)
            pdf.Paragrafo($"Documento: {documento}", centralizado: true);

        return pdf.Paragrafo("Apresente este convite e um documento com foto na entrada. Cada convite vale uma entrada.", centralizado: true)
            .Paragrafo(url, discreto: true, centralizado: true)
            .Gerar($"Convite {convite.Codigo}");
    }

    /// <summary>
    /// A lista da portaria: a redundância de papel, com o documento <b>inteiro</b> (P5 e P5.1).
    /// </summary>
    /// <remarks>
    /// É a lista que o salão pede e a que a portaria confere se o QR ou a rede falharem. Por nome, e
    /// com a situação de cada convite no instante da geração — a mesma da tela.
    /// <para>
    /// O revogado continua na lista — é como a portaria reconhece o print antigo de um convite
    /// transferido —, mas <b>sem o documento</b>: quem deixou de ser convidado não tem por que ter o
    /// número impresso no papel que circula na porta.
    /// </para>
    /// </remarks>
    /// <param name="turma">Nome da turma.</param>
    /// <param name="evento">Evento.</param>
    /// <param name="linhas">Convites, com o documento decifrado.</param>
    /// <param name="paraPortaria">Como a portaria lê cada convite — a situação sai dali, igual à da tela.</param>
    public static byte[] Lista(
        string turma,
        EventoDoConvite evento,
        IReadOnlyList<ConviteGravadoNaPortaria> linhas,
        Func<ConviteGravadoNaPortaria, ConviteNaPortaria> paraPortaria
    )
    {
        var agora = DataUtils.ParaExibicao(DateTime.UtcNow);
        var ordenadas = linhas
            .OrderBy(linha => linha.Convite.NomeDoConvidado is null)
            .ThenBy(linha => linha.Convite.NomeDoConvidado, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(linha => linha.Convite.Codigo, StringComparer.Ordinal)
            .ToList();
        var validos = ordenadas.Count(linha => linha.Convite.Valido);

        return new DocumentoPdf(OrientacaoDaPagina.Paisagem)
            .Capa(
                "Lista da portaria",
                $"{turma} · {evento.Titulo} · {Quando(evento)}{(evento.Local is { } local ? $" · {local}" : string.Empty)}",
                $"Gerada em {agora.ToString("dd/MM/yyyy 'às' HH:mm", CultureInfo.InvariantCulture)} — {validos} convites válidos. Confira nome e documento com foto."
            )
            .Tabela(
                ColunasDaLista,
                ["Convidado", "Documento", "Código", "Convidado de", "Situação"],
                ordenadas.Select(linha =>
                    new[]
                    {
                        linha.Convite.NomeDoConvidado ?? "(a definir)",
                        (linha.Convite.Valido ? DocumentoDoConvidado.Inteiro(linha.Convite.TipoDoDocumento, linha.Convite.NumeroDoDocumento) : null)
                            ?? "—",
                        linha.Convite.Codigo,
                        linha.ConvidadoDe ?? "Cortesia da turma",
                        Situacao(paraPortaria(linha)),
                    }
                )
            )
            .Gerar($"Lista da portaria · {turma}");
    }

    /// <summary>A matriz do QR da URL, com a margem clara em volta.</summary>
    /// <param name="url">Conteúdo do QR.</param>
    public static IReadOnlyList<IReadOnlyList<bool>> Modulos(string url)
    {
        using var gerador = new QRCodeGenerator();
        using var dados = gerador.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);

        return [.. dados.ModuleMatrix.Select(linha => (IReadOnlyList<bool>)[.. linha.Cast<bool>()])];
    }

    private static string Situacao(ConviteNaPortaria convite) =>
        convite.Situacao switch
        {
            SituacaoNaPortaria.Validado when convite.Entrada is { } entrada =>
                $"Entrou às {DataUtils.ParaExibicao(entrada.ValidadoEm).ToString("HH'h'mm", CultureInfo.InvariantCulture)}",
            SituacaoNaPortaria.Revogado => $"Revogado: {convite.MotivoDaRevogacao}",
            SituacaoNaPortaria.SemTitular => "Pendente: sem nome ou documento",
            _ => "Válido",
        };

    private static string Quando(EventoDoConvite evento) =>
        $"{evento.Data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}{(evento.Hora is { } hora ? $", {hora.ToString("HH'h'mm", CultureInfo.InvariantCulture)}" : string.Empty)}";
}
