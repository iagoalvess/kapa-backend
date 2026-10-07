using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Services;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Pagamentos.Services;

/// <summary>
/// A porta única da baixa: o único ponto do sistema que marca parcela como paga.
/// </summary>
/// <remarks>
/// Três caminhos passam por aqui — a confirmação do informe, a baixa manual e, no pós-lançamento, o
/// webhook de um PSP da própria comissão (decisão 3 da Sprint 9). A porta única é o que deixa a baixa
/// automática, se vier, ser uma chamada nova e não uma reescrita.
/// <para>
/// Não chama <c>SalvarAsync</c>: parcela, recebimento, informe, e-mail e auditoria entram na transação
/// de quem chamou — o lote inteiro, ou nada. Quem chama já travou a parcela
/// (<c>IParcelaRepository.TravarParaBaixa</c>).
/// </para>
/// <para>Sem interface, como <c>EmailsDeAdesao</c>: uma implementação, e ninguém de fora a substitui.</para>
/// </remarks>
/// <param name="recebimentoRepository">Entradas no caixa.</param>
/// <param name="eventos">Auditoria.</param>
/// <param name="emails">Aviso ao formando.</param>
/// <param name="quitacao">O que o pedido faz quando a parcela dele é paga — o convite da festa nasce ali.</param>
/// <param name="valoresADevolver">O que a comissão tinha a devolver da parcela cancelada, abatido no estorno (Sprint 42, F2).</param>
public sealed class BaixaService(
    IRecebimentoRepository recebimentoRepository,
    IEventoRepository eventos,
    EmailsDePagamento emails,
    IQuitacaoDePedidos quitacao,
    ValoresADevolver valoresADevolver
)
{
    /// <summary>Evento da baixa, com autor, IP, valores e origem — lido pela trilha de auditoria (Sprint 14).</summary>
    public const string EventoDeBaixa = "pagamento.baixado";

    /// <summary>
    /// Baixa a parcela: paga, recebimento com o devido do dia, informe confirmado, e-mail e auditoria.
    /// </summary>
    /// <remarks>
    /// Parcela que não está em aberto é ignorada, e não erro: é o clique duplo e a confirmação paralela
    /// — a segunda chega depois da trava e encontra a parcela já paga. O lote segue.
    /// </remarks>
    /// <param name="parcela">Parcela travada.</param>
    /// <param name="dados">Forma, dia, valor, comprovante e autor.</param>
    /// <param name="informe">Informe confirmado, se a baixa veio da conferência.</param>
    /// <param name="contexto">Turma, regras aceitas e e-mail do formando.</param>
    /// <returns>Se baixou; <c>false</c> quando a parcela já não estava em aberto.</returns>
    public async Task<Result<bool>> Baixar(
        Parcela parcela,
        DadosDaBaixa dados,
        InformeDePagamento? informe,
        ContextoDaBaixa contexto,
        CancellationToken ct = default
    )
    {
        if (parcela.Status != StatusDaParcela.Aberta)
            return false;

        var valorDoDia = parcela.ValorEm(dados.PagoEm, contexto.Regras);
        var devido = valorDoDia.DevidoEmCentavos;

        var pagar = parcela.Pagar(dados.ValorEmCentavos, dados.PagoEm, devido);
        if (pagar.Falhou)
            return Result.Falha<bool>(pagar.Erros);

        if (informe is not null)
        {
            var confirmar = informe.Confirmar(dados.UsuarioId, dados.AgoraUtc);
            if (confirmar.Falhou)
                return Result.Falha<bool>(confirmar.Erros);
        }

        var recebimento = Recebimento.Novo(parcela.Id, informe?.Id, dados, devido);
        await recebimentoRepository.Adicionar(recebimento, ct);
        await quitacao.AposBaixa(parcela, ct);

        await eventos.Auditar(
            EventoDeBaixa,
            dados.UsuarioId,
            new
            {
                contexto.FormaturaId,
                parcelaId = parcela.Id,
                recebimentoId = recebimento.Id,
                informeId = informe?.Id,
                dados.Forma,
                dados.PagoEm,
                valorEmCentavos = dados.ValorEmCentavos,
                devidoEmCentavos = devido,
                enderecoIp = dados.EnderecoIp,
            },
            ct
        );

        if (contexto.EmailDoFormando is { } email)
        {
            var confirmado = new PagamentoConfirmado(
                email,
                contexto.NomeDaTurma,
                parcela.Vencimento,
                dados.ValorEmCentavos,
                dados.PagoEm,
                pagar.Valor ? 0 : parcela.QuitaCom(devido) - (parcela.ValorPagoEmCentavos ?? 0),
                recebimento.Id
            );

            if (contexto.Confirmados is { } lote)
                lote.Add(confirmado);
            else
                await emails.Confirmados([confirmado], ct);
        }

        return true;
    }

    /// <summary>Evento do estorno, com a justificativa — a segunda linha da auditoria, ao lado da baixa.</summary>
    public const string EventoDeEstorno = "pagamento.estornado";

    /// <summary>
    /// Desfaz a última baixa da parcela: a parcela volta a ser devida, o recebimento fica estornado, o pedido
    /// perde o que o pagamento dava, e o formando fica sabendo por quê.
    /// </summary>
    /// <remarks>
    /// Dois caminhos passam por aqui: o estorno do Presidente e o do Mercado Pago que devolveu o dinheiro — a
    /// contestação no cartão ou a devolução no painel (Sprint 39, P4). Como <see cref="Baixar"/>, não salva, e quem
    /// chama já travou a parcela.
    /// <para>
    /// A parcela cancelada com pagamento também estorna (Sprint 42, F2): continua cancelada, e o que a comissão tinha a
    /// devolver dela diminui do mesmo valor — o dinheiro voltou por outro caminho. O formando não é avisado de parcela
    /// que volta a ser devida, porque ela não volta.
    /// </para>
    /// </remarks>
    /// <param name="parcela">Parcela travada.</param>
    /// <param name="recebimento">A baixa ativa dela, rastreada.</param>
    /// <param name="usuarioId">Quem estorna.</param>
    /// <param name="justificativa">Por quê — vai ao formando e à auditoria.</param>
    /// <param name="enderecoIp">De onde; nulo no automático.</param>
    /// <param name="contexto">Turma e e-mail do formando.</param>
    /// <returns>Falha quando a parcela não tem o que estornar.</returns>
    public async Task<Result> Estornar(
        Parcela parcela,
        Recebimento recebimento,
        Guid usuarioId,
        string justificativa,
        string? enderecoIp,
        ContextoDaBaixa contexto,
        CancellationToken ct = default
    )
    {
        if (parcela.Estornar(recebimento.ValorEmCentavos).Falhou)
            return Result.Falha(Erro.Conflito("pagamento.parcela_nao_paga", "Esta parcela não tem baixa para estornar."));

        recebimento.Estornar(usuarioId, justificativa, DateTime.UtcNow);
        await quitacao.AposEstorno(parcela, ct);
        await valoresADevolver.AposEstorno(parcela, recebimento.ValorEmCentavos, recebimento.JustificativaDoEstorno!, ct);

        await eventos.Auditar(
            EventoDeEstorno,
            usuarioId,
            new
            {
                contexto.FormaturaId,
                parcelaId = parcela.Id,
                recebimentoId = recebimento.Id,
                valorEmCentavos = recebimento.ValorEmCentavos,
                justificativa = recebimento.JustificativaDoEstorno,
                enderecoIp,
            },
            ct
        );

        if (contexto.EmailDoFormando is { } email && parcela.Status != StatusDaParcela.Cancelada)
            await emails.Estornado(
                email,
                contexto.NomeDaTurma,
                parcela.Vencimento,
                recebimento.ValorEmCentavos,
                recebimento.JustificativaDoEstorno!,
                ct
            );

        return Result.Ok();
    }
}
