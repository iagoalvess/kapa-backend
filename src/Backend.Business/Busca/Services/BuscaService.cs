using Backend.Business.Abstractions;
using Backend.Business.Busca.Interfaces;
using Backend.Business.Busca.Models;

namespace Backend.Business.Busca.Services;

/// <summary>
/// A busca do topo da tela.
/// </summary>
/// <remarks>
/// Termo curto devolve vazio em vez de erro: quem está digitando "an" não cometeu engano nenhum, e
/// uma mensagem vermelha a cada tecla seria a tela discutindo com quem escreve. Dois caracteres
/// também casariam com meia turma — a lista não diria nada e a consulta varreria tudo.
/// </remarks>
/// <param name="buscaRepository">As consultas.</param>
public sealed class BuscaService(IBuscaRepository buscaRepository) : IBuscaService
{
    /// <summary>A partir de quantos caracteres a busca vale a ida ao banco.</summary>
    public const int TamanhoMinimo = 3;

    /// <summary>Quantos acertos por grupo — o que cabe numa lista que se lê sem rolar.</summary>
    private const int PorGrupo = 5;

    /// <inheritdoc />
    public async Task<Result<BuscaNaTurma>> Buscar(QuemBusca quem, string? termo, CancellationToken ct = default)
    {
        var limpo = termo?.Trim() ?? string.Empty;

        if (limpo.Length < TamanhoMinimo)
            return Result.Ok(BuscaNaTurma.Nada);

        return Result.Ok(await buscaRepository.Buscar(quem, limpo, PorGrupo, ct));
    }
}
