namespace Backend.Business.Recebimentos.Models;

/// <summary>
/// Como a turma aceita receber — e, no aviso de pagamento, como o formando diz ter pago.
/// </summary>
/// <remarks>
/// É o eixo da cobrança, e não o da baixa: <c>FormaDePagamento</c> conta como o dinheiro entrou,
/// depois, e está gravada como texto em todo recebimento desde a Sprint 9. Reaproveitar uma pela
/// outra é migration, não refatoração.
/// <para>
/// A turma habilita os três primeiros, e cada um é um destino conferível — uma chave, uma conta,
/// uma pessoa. <see cref="Outro" /> só existe do lado do aviso (P7): o formando pagou de um jeito
/// que ninguém previu, e recusar o aviso não desfaz o pagamento.
/// </para>
/// <para>
/// "Combinar com a comissão" foi retirado em 21/09/2026, depois de implementado: era o único meio
/// sem destino a conferir, e o único que quebrava a sequência da tela — quem escolhe combinar ainda
/// não pagou, e o passo 2 pedia que ele avisasse um pagamento que não houve. Negociar parcela é
/// conversa com a tesouraria, não meio de recebimento.
/// </para>
/// <para>Gravado como texto: renomear um valor aqui é migration.</para>
/// </remarks>
public enum MeioDeRecebimento
{
    /// <summary>PIX para a chave da comissão — o QR e o copia-e-cola.</summary>
    Pix,

    /// <summary>Transferência ou TED para a conta da comissão.</summary>
    Transferencia,

    /// <summary>Dinheiro em mãos, com quem a turma indicou.</summary>
    Dinheiro,

    /// <summary>Um meio que a turma não habilitou. Só no aviso do formando.</summary>
    Outro,
}

/// <summary>Nome de cada meio para gente ler, em e-mail e em log.</summary>
public static class MeiosDeRecebimento
{
    /// <summary>Rótulo do meio.</summary>
    /// <param name="meio">Meio de recebimento.</param>
    public static string Rotulo(MeioDeRecebimento meio) =>
        meio switch
        {
            MeioDeRecebimento.Pix => "PIX",
            MeioDeRecebimento.Transferencia => "Transferência",
            MeioDeRecebimento.Dinheiro => "Dinheiro",
            _ => "Outro meio",
        };
}
