using Backend.Business.Abstractions;
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
public sealed class BaixaService(IRecebimentoRepository recebimentoRepository, IEventoRepository eventos, EmailsDePagamento emails)
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

        var devido = parcela.ValorEm(dados.PagoEm, contexto.Regras).TotalEmCentavos;

        var pagar = parcela.Pagar(dados.ValorEmCentavos, dados.PagoEm);
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
            await emails.Confirmado(email, contexto.NomeDaTurma, parcela.Vencimento, dados.ValorEmCentavos, dados.PagoEm, ct);

        return true;
    }
}
