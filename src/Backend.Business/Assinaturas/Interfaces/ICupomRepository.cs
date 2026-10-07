using Backend.Business.Assinaturas.Models;

namespace Backend.Business.Assinaturas.Interfaces;

/// <summary>Cupons de desconto da primeira cobrança (Sprint 51). Da plataforma: sem filtro de formatura.</summary>
public interface ICupomRepository
{
    /// <summary>Cupom pelo código já normalizado, em qualquer situação — quem decide se vale é o service.</summary>
    /// <param name="codigo">Código normalizado.</param>
    Task<Cupom?> ObterPorCodigo(string codigo, CancellationToken ct = default);

    /// <summary>Cupom pelo id, sem rastreio.</summary>
    /// <param name="cupomId">Cupom.</param>
    Task<Cupom?> Obter(Guid cupomId, CancellationToken ct = default);

    /// <summary>Cupom pelo id, rastreado para desativar.</summary>
    /// <param name="cupomId">Cupom.</param>
    Task<Cupom?> ObterParaEdicao(Guid cupomId, CancellationToken ct = default);

    /// <summary>
    /// Conta um uso, se ainda houver: <c>UPDATE … SET usos = usos + 1 WHERE ativo AND usos &lt; limite AND valido_ate &gt; agora</c>.
    /// </summary>
    /// <remarks>
    /// Exceção documentada a "repositório não persiste", como <c>ConviteRepository.ConsumirUsoDeTodasAsFormaturas</c>:
    /// a conferência do limite tem de acontecer sob a trava da linha, depois de o checkout concorrente já ter contado
    /// o último uso. Falso é esgotado, vencido ou desativado.
    /// </remarks>
    /// <param name="cupomId">Cupom.</param>
    /// <param name="agoraUtc">Momento do checkout.</param>
    Task<bool> ReservarUso(Guid cupomId, DateTime agoraUtc, CancellationToken ct = default);

    /// <summary>Todos os cupons, os mais novos primeiro. A lista é curta: o Administrador cria um por campanha.</summary>
    Task<IReadOnlyList<Cupom>> Listar(CancellationToken ct = default);

    /// <summary>Marca um cupom novo para inclusão.</summary>
    /// <param name="cupom">Cupom.</param>
    Task Adicionar(Cupom cupom, CancellationToken ct = default);
}
