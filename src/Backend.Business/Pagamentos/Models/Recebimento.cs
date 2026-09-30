using Backend.Business.Abstractions;

namespace Backend.Business.Pagamentos.Models;

/// <summary>
/// Uma entrada no caixa da turma: a parcela baixada, com quanto entrou, como e quem baixou.
/// </summary>
/// <remarks>
/// Nasce só pela <c>BaixaService</c>. Guarda o <see cref="DevidoEmCentavos"/> do dia do pagamento ao
/// lado do recebido: é o fato do dia da baixa, e não uma dívida recalculada — e é o que a aba
/// Divergências compara (decisão 5 da Sprint 9). Autor, IP e momento são a trilha da decisão 6.
/// <para>
/// O estorno não apaga: marca <see cref="EstornadoEm"/>, e a linha continua lá. O índice único parcial
/// em <c>parcela_id</c> onde <c>estornado_em is null</c> é a última barreira contra duas baixas da mesma
/// parcela — a segunda falha no insert.
/// </para>
/// </remarks>
public class Recebimento : EntidadeDaFormatura
{
    /// <summary>Parcela baixada.</summary>
    public Guid ParcelaId { get; private set; }

    /// <summary>Informe confirmado, se a baixa veio da conferência.</summary>
    public Guid? InformeId { get; private set; }

    /// <summary>
    /// A cobrança do Mercado Pago que pagou, se a baixa foi automática (Sprint 42, F3).
    /// </summary>
    /// <remarks>
    /// É o que faz a devolução no Mercado Pago estornar <b>esta</b> baixa, e não a mais recente da parcela: PIX do
    /// Mercado Pago e PIX manual gravam a mesma forma, e a baixa manual feita depois era a que sumia.
    /// </remarks>
    public Guid? CobrancaId { get; private set; }

    /// <summary>Como o dinheiro chegou.</summary>
    public FormaDePagamento Forma { get; private set; }

    /// <summary>O que entrou na conta, em centavos.</summary>
    public long ValorEmCentavos { get; private set; }

    /// <summary>O valor do dia do pagamento, pelas regras aceitas — com multa e juros, ou com desconto.</summary>
    public long DevidoEmCentavos { get; private set; }

    /// <summary>Dia em que o dinheiro entrou.</summary>
    public DateOnly PagoEm { get; private set; }

    /// <summary>Comprovante da baixa manual, se enviado. O do informe fica no informe.</summary>
    public Guid? ComprovanteArquivoId { get; private set; }

    /// <summary>Quem baixou.</summary>
    public Guid BaixadoPorUsuarioId { get; private set; }

    /// <summary>Quando baixou, em UTC.</summary>
    public DateTime BaixadoEm { get; private set; }

    /// <summary>IP de quem baixou.</summary>
    public string EnderecoIp { get; private set; } = string.Empty;

    /// <summary>Quando a baixa foi desfeita, em UTC. Nulo: vale.</summary>
    public DateTime? EstornadoEm { get; private set; }

    /// <summary>Quem desfez — sempre um Presidente.</summary>
    public Guid? EstornadoPorUsuarioId { get; private set; }

    /// <summary>Por que desfez.</summary>
    public string? JustificativaDoEstorno { get; private set; }

    /// <summary>A entrada de uma baixa.</summary>
    /// <param name="parcelaId">Parcela baixada.</param>
    /// <param name="informeId">Informe confirmado, se houver.</param>
    /// <param name="baixa">Forma, dia, valor, comprovante e autor.</param>
    /// <param name="devidoEmCentavos">Valor do dia do pagamento.</param>
    public static Recebimento Novo(Guid parcelaId, Guid? informeId, DadosDaBaixa baixa, long devidoEmCentavos) =>
        new()
        {
            ParcelaId = parcelaId,
            InformeId = informeId,
            CobrancaId = baixa.CobrancaId,
            Forma = baixa.Forma,
            ValorEmCentavos = baixa.ValorEmCentavos,
            DevidoEmCentavos = devidoEmCentavos,
            PagoEm = baixa.PagoEm,
            ComprovanteArquivoId = baixa.ComprovanteArquivoId,
            BaixadoPorUsuarioId = baixa.UsuarioId,
            BaixadoEm = baixa.AgoraUtc,
            EnderecoIp = baixa.EnderecoIp ?? string.Empty,
        };

    /// <summary>Desfaz a baixa, com a justificativa. A linha fica.</summary>
    /// <param name="usuarioId">Presidente que estornou.</param>
    /// <param name="justificativa">Por quê.</param>
    /// <param name="agoraUtc">Momento do estorno.</param>
    public void Estornar(Guid usuarioId, string justificativa, DateTime agoraUtc)
    {
        EstornadoEm = agoraUtc;
        EstornadoPorUsuarioId = usuarioId;
        JustificativaDoEstorno = justificativa.Trim();
    }
}
