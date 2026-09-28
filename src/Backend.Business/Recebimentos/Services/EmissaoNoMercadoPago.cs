using Backend.Business.Abstractions;
using Backend.Business.Common.Datas;
using Backend.Business.MercadoPago.Interfaces;
using Backend.Business.MercadoPago.Models;
using Backend.Business.MercadoPago.Settings;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.Recebimentos.Services;

/// <summary>
/// As cobranças que o Mercado Pago da turma faz: devolve a viva ou emite uma nova (Sprint 25, decisões 4 e
/// 12a), para as parcelas e para a compra da loja.
/// </summary>
/// <remarks>
/// Todo pedido passa pelo mesmo caminho — reserva gravada antes da chamada, chamada fora de qualquer
/// transação, e o resultado gravado depois. A estratégia de execução reexecuta a operação inteira em falha
/// transitória do banco, e HTTP para um terceiro não é repetível sem efeito.
/// <para>
/// O PIX da parcela vale até o fim do dia: amanhã o valor do dia muda — juros, ou o fim do desconto —, e ele
/// não pode cobrar o de ontem. Emitido perto da meia-noite, ganha os 30 minutos que o Mercado Pago exige.
/// </para>
/// <para>
/// <c>ponytail:</c> sem disjuntor; o tempo limite curto (<c>MercadoPago:SegundosDeEspera</c>) segura a
/// tela. Se o Mercado Pago cair com muita gente pagando, o <c>Microsoft.Extensions.Http.Resilience</c>
/// entra no <c>HttpClient</c> sem mudar esta classe.
/// </para>
/// </remarks>
/// <param name="repositorio">Credencial e cobranças.</param>
/// <param name="mercadoPago">A API.</param>
/// <param name="options">A aplicação do Kapa.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class EmissaoNoMercadoPago(
    IProvedorDaTurmaRepository repositorio,
    IMercadoPago mercadoPago,
    IOptions<MercadoPagoSettings> options,
    IUnitOfWork unitOfWork,
    ILogger<EmissaoNoMercadoPago> logger
)
{
    /// <summary>A validade mínima que o Mercado Pago aceita num PIX.</summary>
    public static readonly TimeSpan ValidadeMinima = TimeSpan.FromMinutes(30);

    /// <summary>O PIX que já vence antes disto não é reaproveitado: o formando não teria tempo de pagar.</summary>
    public static readonly TimeSpan FolgaParaPagar = TimeSpan.FromMinutes(10);

    private readonly MercadoPagoSettings _config = options.Value;

    /// <summary>A autorização da turma, se o Mercado Pago está ligado e ela conectou.</summary>
    public async Task<CredencialDeProvedor?> Credencial(CancellationToken ct = default) =>
        _config.Ligado ? await repositorio.ObterCredencial(ct) : null;

    /// <summary>O PIX das parcelas, ou nulo quando o Mercado Pago falhou — a tela fica com os meios da comissão (P7).</summary>
    /// <param name="credencial">A autorização da turma.</param>
    /// <param name="parcelaIds">Parcelas que o pagamento cobre.</param>
    /// <param name="valorEmCentavos">O valor do dia, somado.</param>
    /// <param name="emailDoPagador">E-mail do formando.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public async Task<PixDinamicoParaPagar?> Pix(
        CredencialDeProvedor credencial,
        IReadOnlyCollection<Guid> parcelaIds,
        long valorEmCentavos,
        string emailDoPagador,
        CancellationToken ct = default
    )
    {
        if (valorEmCentavos <= 0)
            return null;

        var agora = DateTime.UtcNow;
        var fimDoDia = DataUtils.FimDoDiaEmUtc(DataUtils.Hoje());
        var expiraEm = fimDoDia - agora < ValidadeMinima ? agora + ValidadeMinima : fimDoDia;
        var chave = CobrancaBancaria.ChaveDoPix(credencial.IdNoProvedor, parcelaIds, valorEmCentavos, DataUtils.Hoje());

        var emitida = await ObterOuEmitir(
            credencial,
            new CobrancaBancaria(MeioDePagamento.Pix, credencial.IdNoProvedor, parcelaIds, valorEmCentavos, expiraEm, chave),
            new PagadorNoMercadoPago(emailDoPagador),
            ct
        );

        return emitida.Sucesso ? new PixDinamicoParaPagar(emitida.Valor.CopiaECola!, emitida.Valor.ExpiraEm) : null;
    }

    /// <summary>
    /// A cobrança de uma compra da loja pública (Sprint 26): a viva, se houver; senão, uma nova, com a validade
    /// que a loja calculou — o PIX vence antes da reserva (decisão 9).
    /// </summary>
    /// <param name="credencial">A autorização da turma.</param>
    /// <param name="compraId">A compra.</param>
    /// <param name="meio">Como o comprador paga.</param>
    /// <param name="valorEmCentavos">Total da compra.</param>
    /// <param name="expiraEm">Fim da validade do PIX, em UTC.</param>
    /// <param name="pagador">Quem paga.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>A cobrança, ou nulo quando o Mercado Pago falhou ou outra aba está emitindo.</returns>
    public async Task<CobrancaBancaria?> DaCompra(
        CredencialDeProvedor credencial,
        Guid compraId,
        MeioDePagamento meio,
        long valorEmCentavos,
        DateTime expiraEm,
        PagadorNoMercadoPago pagador,
        CancellationToken ct = default
    )
    {
        if (await repositorio.ObterViva(CobrancaBancaria.ChaveDaCompra(compraId), ct) is { Status: StatusDaCobrancaBancaria.Emitida } viva)
            return viva;

        var emitida = await ObterOuEmitir(
            credencial,
            CobrancaBancaria.DaCompra(meio, credencial.IdNoProvedor, compraId, valorEmCentavos, expiraEm),
            pagador,
            ct
        );

        return emitida is { Sucesso: true, Valor.Status: StatusDaCobrancaBancaria.Emitida } ? emitida.Valor : null;
    }

    /// <summary>
    /// O caminho comum: reaproveita a viva de mesma chave, ou reserva, chama o Mercado Pago e grava o que
    /// voltou. A reserva perdida para outra aba lê a vencedora.
    /// </summary>
    /// <remarks>
    /// Decisão 12a, inteira: a reserva que ficou em <c>Emitindo</c> porque a resposta não chegou (tempo
    /// esgotado, rede, erro do lado de lá) é <b>retomada</b> com o mesmo id — a mesma chave de idempotência —,
    /// e o Mercado Pago devolve o pedido que já tinha criado, em vez de emitir outro. Só depois do tempo de
    /// espera, para não disputar com a chamada que ainda pode estar em curso em outra aba; e só enquanto ainda
    /// dá para pagar — vencida sem resposta, a reserva é solta e nasce outra.
    /// </remarks>
    private async Task<Result<CobrancaBancaria>> ObterOuEmitir(
        CredencialDeProvedor credencial,
        CobrancaBancaria nova,
        PagadorNoMercadoPago pagador,
        CancellationToken ct
    )
    {
        var agora = DateTime.UtcNow;

        if (await repositorio.ObterViva(nova.Chave, ct) is { } viva)
        {
            if (viva.Pagavel(agora + FolgaParaPagar))
                return viva;

            if (viva.Status != StatusDaCobrancaBancaria.Emitindo || viva.CriadoEm > agora - EsperaParaRetomar)
                return EmEmissao;

            var pendente = (await repositorio.ObterCobrancaParaEdicao(viva.Id, ct))!;

            if (viva.ExpiraEm > agora + ValidadeMinima)
                return await Enviar(credencial, pendente, pagador, agora, ct);

            pendente.Falhou();
            await unitOfWork.SalvarAsync(ct);
        }

        if (!await repositorio.ReservarEmissao(nova, ct))
            return await repositorio.ObterViva(nova.Chave, ct) is { } outra && outra.Pagavel(agora + FolgaParaPagar) ? outra : EmEmissao;

        return await Enviar(credencial, (await repositorio.ObterCobrancaParaEdicao(nova.Id, ct))!, pagador, agora, ct);
    }

    /// <summary>Quanto a reserva sem resposta espera antes de ser retomada: o tempo limite da chamada, com folga.</summary>
    private TimeSpan EsperaParaRetomar => TimeSpan.FromSeconds(_config.SegundosDeEspera * 2);

    /// <summary>
    /// Chama o Mercado Pago com o id da reserva como chave e grava o que voltou. Recusa solta a reserva;
    /// resposta que não chegou a mantém em <c>Emitindo</c>, para ser retomada com a mesma chave.
    /// </summary>
    private async Task<Result<CobrancaBancaria>> Enviar(
        CredencialDeProvedor credencial,
        CobrancaBancaria reservada,
        PagadorNoMercadoPago pagador,
        DateTime agora,
        CancellationToken ct
    )
    {
        var emitido = await mercadoPago.Emitir(
            credencial.AccessToken,
            new PedidoDeCobranca(reservada.Id, reservada.Meio, reservada.ValorEmCentavos, pagador, reservada.ExpiraEm - agora),
            ct
        );

        if (emitido.Falhou)
        {
            if (emitido.PrimeiroErro.Tipo == ETipoErro.Indisponivel)
            {
                logger.LogWarning(
                    "{Meio} da cobrança {CobrancaId} sem resposta do Mercado Pago; fica reservado para retomar com a mesma chave.",
                    reservada.Meio,
                    reservada.Id
                );
                return Result.Falha<CobrancaBancaria>(emitido.Erros);
            }

            reservada.Falhou();
            await unitOfWork.SalvarAsync(ct);

            logger.LogWarning("{Meio} da cobrança {CobrancaId} recusado pelo Mercado Pago.", reservada.Meio, reservada.Id);
            return Result.Falha<CobrancaBancaria>(emitido.Erros);
        }

        reservada.Emitida(emitido.Valor.IdExterno, emitido.Valor.CopiaECola);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("{Meio} da cobrança {CobrancaId} emitido.", reservada.Meio, reservada.Id);

        return reservada;
    }

    private static readonly Erro EmEmissao = Erro.Conflito(
        "pagamento.cobranca_em_emissao",
        "Este pagamento está sendo gerado em outra tela. Aguarde um instante e tente de novo."
    );
}
