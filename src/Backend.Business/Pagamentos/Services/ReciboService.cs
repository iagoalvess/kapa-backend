using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Interfaces;

namespace Backend.Business.Pagamentos.Services;

/// <summary>
/// O recibo de cada baixa (Sprint 22): confere quem pede e entrega o PDF montado pelo <see cref="ReciboEmPdf"/>.
/// </summary>
/// <param name="recebimentoRepository">Baixas, de onde sai o recibo.</param>
/// <param name="contaRepository">Os meios de recebimento da comissão, os de hoje e os do dia do pagamento.</param>
/// <param name="perfilRepository">Quem pede, e com que papel.</param>
public sealed class ReciboService(
    IRecebimentoRepository recebimentoRepository,
    IContaDeRecebimentoRepository contaRepository,
    IPerfilRepository perfilRepository
) : IReciboService
{
    /// <inheritdoc />
    /// <remarks>
    /// O titular é o da conta como estava no dia do pagamento, lido da trilha de auditoria (P2 da
    /// Sprint 22): a conta de hoje pode ser outra, e o recibo não muda com uma troca de chave. O
    /// instante é o fim do dia do pagamento, ou a baixa se veio antes — a troca feita depois de o
    /// dinheiro entrar não é a conta que o recebeu. Sem evento até lá (conta anterior à auditoria, ou
    /// além da retenção), vale a conta atual.
    /// </remarks>
    public async Task<Result<ArquivoParaDownload>> Obter(Guid formaturaId, Guid usuarioId, Guid recebimentoId, CancellationToken ct = default)
    {
        var recibo = await recebimentoRepository.ObterParaRecibo(recebimentoId, DataUtils.Hoje(), ct);
        var titular = await perfilRepository.ObterTitular(formaturaId, usuarioId, ct);

        if (recibo is null || titular is null)
            return ErrosDePagamento.RecebimentoNaoEncontrado;

        var proprio = recibo.Parcela.VinculoId == titular.VinculoId;

        if (
            !proprio
            && !(await perfilRepository.ObterMembro(formaturaId, usuarioId, ct) is { } membro && PapelNaFormatura.Gestao.Contains(membro.Papel))
        )
            return ErrosDePagamento.RecebimentoNaoEncontrado;

        if (recibo.Estornado)
            return ErrosDePagamento.RecebimentoEstornado;

        var instante = new[] { recibo.BaixadoEm, DataUtils.FimDoDiaEmUtc(recibo.PagoEm) }.Min();
        var meios = await contaRepository.ObterMeiosVigentesEm(formaturaId, instante, ct) ?? (await contaRepository.ObterDetalhe(ct))?.Meios;

        return new ArquivoParaDownload(
            new MemoryStream(ReciboEmPdf.Gerar(recibo, meios, mascararCpf: !proprio)),
            $"recibo-{recibo.PagoEm.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf",
            "application/pdf"
        );
    }
}
