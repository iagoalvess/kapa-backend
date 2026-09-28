using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Adesoes.Settings;
using Backend.Business.IA.Interfaces;
using Backend.Business.IA.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Adesoes.Services;

/// <summary>
/// O resumo do termo por IA: quais versões resumir, o teto que descarta o absurdo e a gravação
/// (Sprint 24, decisões 5 e 7).
/// </summary>
/// <remarks>
/// Quem fala com o provedor é o <see cref="IModeloDeLinguagem"/>; aqui ficam a instrução, os modelos
/// desta feature e a regra. Texto puro na volta, gravado como veio, aparado — sem revisão da comissão
/// (decisão 9). O que sai da plataforma é a instrução e o <c>Conteudo</c> do termo, e mais nada
/// (decisão 12).
/// </remarks>
/// <param name="adesaoRepository">Termos e resumos.</param>
/// <param name="modelo">Porta para o modelo de linguagem.</param>
/// <param name="options">Modelos e teto por rodada.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class ResumoDoTermoService(
    IAdesaoRepository adesaoRepository,
    IModeloDeLinguagem modelo,
    IOptions<ResumoSettings> options,
    IUnitOfWork unitOfWork
) : IResumoDoTermoService
{
    /// <summary>A instrução que acompanha o termo. Fixa, sem nada da turma.</summary>
    /// <remarks>
    /// Tópicos (o produto testou texto corrido em 24/09/2026 e voltou atrás no mesmo dia). Cada tópico é
    /// "Título: frase", separado do outro por uma linha em branco e sem marcador: a tela tira o título
    /// da lista fechada e desenha o ícone dele (28/09/2026). Resumo antigo, sem título, a tela ainda lê
    /// frase a frase.
    /// </remarks>
    public const string Instrucao =
        "Você resume termos de adesão de formatura para o formando que vai assiná-lo. "
        + "Responda em português do Brasil, em 3 a 4 tópicos separados por uma linha em branco. "
        + "Cada tópico é uma única linha no formato \"Título: frase\", com a frase curta de no máximo 20 palavras. "
        + "O título é exatamente um destes, sem repetir: Pagamentos, Desconto, Atraso, Desistência, Reembolso, Prazos, Convites. "
        + "Sem marcador (hífen, asterisco ou número), sem markdown e sem introdução. "
        + "Fique no essencial: não cite todos os itens nem todos os detalhes. "
        + "Trate o formando por \"você\" e diga o que o termo faz com o dinheiro dele (valores, multa, juros, desistência, reembolso) e com os prazos. "
        + "Não invente nada que não esteja no texto.";

    /// <summary>Quatro frases curtas não têm mais que isso; acima, é o modelo devolvendo o termo de volta.</summary>
    public const int TetoDeCaracteres = 1500;

    /// <summary>
    /// Depois disso o job desiste do termo (decisão 5).
    /// </summary>
    /// <remarks>
    /// <c>ponytail:</c> palpite de que o que não resumiu numa semana não vai resumir. Uma linha de
    /// <c>WHERE</c> no lugar de contador de tentativas; reprocessar termo antigo, se um dia for
    /// preciso, é um botão que chama <see cref="Gerar"/> direto — não relaxar a janela.
    /// </remarks>
    public static readonly TimeSpan Janela = TimeSpan.FromDays(7);

    private readonly ResumoSettings _config = options.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TermoSemResumo>> ListarPendentes(DateTime agoraUtc, CancellationToken ct = default)
    {
        if (!modelo.Ligado || _config.Modelos.Length == 0)
            return [];

        return await adesaoRepository.ListarTermosSemResumoDeTodasAsFormaturas(agoraUtc - Janela, _config.PorRodada, ct);
    }

    /// <inheritdoc />
    public async Task<Result> Gerar(Guid termoId, CancellationToken ct = default)
    {
        var termo = await adesaoRepository.ObterTermo(termoId, ct);
        if (termo is null)
            return Result.Falha(Erro.NaoEncontrado("adesao.termo_nao_encontrado", "Versão do termo não encontrada."));

        var resposta = await modelo.Completar(new PedidoAoModelo(Instrucao, termo.Conteudo, _config.Modelos), ct);
        if (resposta.Falhou)
            return Result.Falha(resposta.Erros);

        var gerado = resposta.Valor;
        if (gerado.Texto.Length > TetoDeCaracteres)
            return Result.Falha(Erro.Validacao("adesao.resumo_descartado", $"O modelo {gerado.Modelo} devolveu {gerado.Texto.Length} caracteres."));

        await adesaoRepository.AdicionarResumo(
            new ResumoDoTermo
            {
                TermoId = termo.Id,
                Texto = gerado.Texto,
                Modelo = gerado.Modelo,
                GeradoEm = DateTime.UtcNow,
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }
}
