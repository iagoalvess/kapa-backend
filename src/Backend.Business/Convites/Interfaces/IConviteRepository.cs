using Backend.Business.Convites.Models;

namespace Backend.Business.Convites.Interfaces;

/// <summary>
/// Convites e o registro de quem entrou por eles.
/// </summary>
/// <remarks>
/// A gestão (listar, revogar) passa pelo filtro global da formatura da sessão. O aceite não pode:
/// quem aceita ainda não está na turma, e a sessão dele enxerga outra formatura ou nenhuma — daí
/// os métodos com sufixo <c>DeTodasAsFormaturas</c>, que só procuram pelo hash do token.
/// </remarks>
public interface IConviteRepository
{
    /// <summary>Os convites mais recentes da formatura da sessão, com a situação no instante informado.</summary>
    /// <param name="agoraUtc">Momento que decide pendente e expirado.</param>
    /// <param name="limite">Quantos no máximo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<IReadOnlyList<ConviteResumo>> ListarRecentes(DateTime agoraUtc, int limite, CancellationToken ct = default);

    /// <summary>Um convite da formatura da sessão, rastreado para alteração.</summary>
    /// <param name="conviteId">Convite.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Convite?> ObterParaEdicao(Guid conviteId, CancellationToken ct = default);

    /// <summary>O convite com este hash de token, de qualquer formatura, sem rastreamento.</summary>
    /// <param name="tokenHash">SHA-256 do token recebido.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Convite?> ObterPorHashDeTodasAsFormaturas(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Soma um uso ao convite, se ele ainda for utilizável, num único <c>UPDATE</c> condicional.
    /// </summary>
    /// <remarks>
    /// Precisa rodar dentro de <c>IUnitOfWork.EmTransacaoAsync</c>: a linha fica travada até o
    /// commit, e o segundo aceite simultâneo espera, reavalia o <c>WHERE</c> e não passa do limite.
    /// Exceção documentada ao "repositório não persiste" — é o próprio mecanismo de concorrência,
    /// não há o que marcar e salvar depois.
    /// </remarks>
    /// <param name="conviteId">Convite.</param>
    /// <param name="agoraUtc">Momento que decide a validade.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se o uso foi consumido; <c>false</c> se o convite já não admite mais.</returns>
    Task<bool> ConsumirUsoDeTodasAsFormaturas(Guid conviteId, DateTime agoraUtc, CancellationToken ct = default);

    /// <summary>Registra um convite novo.</summary>
    /// <param name="convite">Convite a persistir.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task Adicionar(Convite convite, CancellationToken ct = default);

    /// <summary>Registra quem entrou pelo convite.</summary>
    /// <param name="aceite">Registro do aceite.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task RegistrarAceite(AceiteDeConvite aceite, CancellationToken ct = default);
}
