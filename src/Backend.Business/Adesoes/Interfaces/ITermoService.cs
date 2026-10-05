using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Models;

namespace Backend.Business.Adesoes.Interfaces;

/// <summary>
/// O termo de adesão da turma: a comissão publica versões, o formando lê a vigente.
/// </summary>
public interface ITermoService
{
    /// <summary>As versões publicadas, da mais nova para a mais antiga.</summary>
    Task<Result<IReadOnlyList<TermoPublicado>>> Listar(CancellationToken ct = default);

    /// <summary>Publica a versão seguinte. As adesões existentes continuam na versão delas.</summary>
    /// <param name="usuarioId">Quem publica.</param>
    /// <param name="dados">Texto da versão.</param>
    Task<Result<VersaoDoTermo>> Publicar(Guid usuarioId, PublicarTermo dados, CancellationToken ct = default);

    /// <summary>O termo vigente, o catálogo, o plano com a cesta como seria aceito agora, e o hash dos dois.</summary>
    /// <param name="formaturaId">Turma da sessão.</param>
    /// <param name="usuarioId">Quem lê — a cesta que já contratou prevalece sobre a escolhida.</param>
    /// <param name="pacotes">A cesta escolhida na tela; vazia mostra só o catálogo e os rateios.</param>
    Task<Result<ConteudoParaAdesao>> ObterParaAdesao(
        Guid formaturaId,
        Guid usuarioId,
        IReadOnlyCollection<Guid> pacotes,
        CancellationToken ct = default
    );
}
