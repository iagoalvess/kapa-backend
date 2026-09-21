using Backend.Business.Abstractions;
using Backend.Business.Recebimentos.Models;

namespace Backend.Business.Pagamentos.Models;

/// <summary>
/// O "já paguei" do formando: uma alegação, não um fato.
/// </summary>
/// <remarks>
/// Gravar o informe não muda a parcela — quem muda é a tesouraria, depois de ver o dinheiro no banco
/// (decisão 2 da Sprint 9). Na tela do formando, parcela com informe pendente se lê "Em conferência":
/// é leitura, não status da parcela.
/// <para>
/// Um pendente por parcela, pelo índice único parcial em <c>parcela_id</c>: o toque duplo no "Já paguei"
/// não vira dois itens na fila da tesouraria.
/// </para>
/// </remarks>
public class InformeDePagamento : EntidadeDaFormatura
{
    /// <summary>Parcela que o formando diz ter pago.</summary>
    public Guid ParcelaId { get; private set; }

    /// <summary>Vínculo de quem informou — o dono da parcela.</summary>
    public Guid VinculoId { get; private set; }

    /// <summary>Dia em que o formando diz ter pago.</summary>
    public DateOnly PagoEm { get; private set; }

    /// <summary>Valor que o formando diz ter pago, em centavos.</summary>
    public long ValorEmCentavos { get; private set; }

    /// <summary>Comprovante enviado, se houver. Opcional em qualquer meio (P1 da Sprint 9, mantido no P6 de 21/09/2026).</summary>
    public Guid? ComprovanteArquivoId { get; private set; }

    /// <summary>Como o formando diz ter pago. Nulo nos avisos anteriores à Sprint 18.</summary>
    /// <remarks>
    /// É sugestão à tesouraria, não trava: a conferência baixa com a <see cref="FormaDePagamento"/>
    /// correspondente, e a baixa manual continua deixando-a escolher — quem viu o extrato é ela.
    /// </remarks>
    public MeioDeRecebimento? MeioEscolhido { get; private set; }

    /// <summary>Situação. Muda só por <see cref="Confirmar"/> e <see cref="Recusar"/>.</summary>
    public StatusDoInforme Status { get; private set; } = StatusDoInforme.Pendente;

    /// <summary>Por que a tesouraria recusou.</summary>
    public string? MotivoDaRecusa { get; private set; }

    /// <summary>Quem da tesouraria confirmou ou recusou.</summary>
    public Guid? ConferidoPorUsuarioId { get; private set; }

    /// <summary>Quando foi confirmado ou recusado, em UTC.</summary>
    public DateTime? ConferidoEm { get; private set; }

    /// <summary>Um informe pendente.</summary>
    /// <param name="parcelaId">Parcela paga.</param>
    /// <param name="vinculoId">Dono da parcela.</param>
    /// <param name="pagoEm">Dia informado.</param>
    /// <param name="valorEmCentavos">Valor informado.</param>
    /// <param name="comprovanteArquivoId">Comprovante, se enviado.</param>
    /// <param name="meioEscolhido">Como o formando diz ter pago.</param>
    public static InformeDePagamento Novo(
        Guid parcelaId,
        Guid vinculoId,
        DateOnly pagoEm,
        long valorEmCentavos,
        Guid? comprovanteArquivoId,
        MeioDeRecebimento? meioEscolhido
    ) =>
        new()
        {
            ParcelaId = parcelaId,
            VinculoId = vinculoId,
            PagoEm = pagoEm,
            ValorEmCentavos = valorEmCentavos,
            ComprovanteArquivoId = comprovanteArquivoId,
            MeioEscolhido = meioEscolhido,
        };

    /// <summary>A tesouraria achou o dinheiro. Chamado pela baixa, na mesma transação.</summary>
    /// <param name="usuarioId">Quem confirmou.</param>
    /// <param name="agoraUtc">Momento da confirmação.</param>
    public Result Confirmar(Guid usuarioId, DateTime agoraUtc) => Conferir(StatusDoInforme.Confirmado, usuarioId, agoraUtc, null);

    /// <summary>A tesouraria não achou o dinheiro. A parcela continua aberta.</summary>
    /// <param name="usuarioId">Quem recusou.</param>
    /// <param name="motivo">Motivo, que vai ao formando por e-mail.</param>
    /// <param name="agoraUtc">Momento da recusa.</param>
    public Result Recusar(Guid usuarioId, string motivo, DateTime agoraUtc) => Conferir(StatusDoInforme.Recusado, usuarioId, agoraUtc, motivo.Trim());

    private Result Conferir(StatusDoInforme destino, Guid usuarioId, DateTime agoraUtc, string? motivo)
    {
        if (Status != StatusDoInforme.Pendente)
            return Result.Falha(Erro.Conflito("pagamento.informe_ja_conferido", "Este aviso de pagamento já foi conferido."));

        Status = destino;
        MotivoDaRecusa = motivo;
        ConferidoPorUsuarioId = usuarioId;
        ConferidoEm = agoraUtc;

        return Result.Ok();
    }
}

/// <summary>Situação do aviso de pagamento do formando.</summary>
/// <remarks>Gravado como texto: o índice único parcial filtra por <c>status = 'Pendente'</c>.</remarks>
public enum StatusDoInforme
{
    /// <summary>Esperando a tesouraria olhar o extrato do banco.</summary>
    Pendente,

    /// <summary>A tesouraria achou o dinheiro e baixou a parcela.</summary>
    Confirmado,

    /// <summary>A tesouraria não achou o dinheiro; o motivo foi ao formando por e-mail.</summary>
    Recusado,
}
