using Backend.Business.Common.Datas;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Recebimentos.Models;

namespace Backend.Business.Pagamentos.Services;

/// <summary>
/// O que sobra de uma cobrança paga do Mercado Pago quando as baixas dela são desfeitas: o acréscimo do cartão no caixa
/// e a própria cobrança.
/// </summary>
/// <remarks>
/// Dois caminhos passam por aqui: a devolução que o Mercado Pago avisa (Sprint 39, P4) e o estorno à mão do Presidente
/// sobre uma baixa do Mercado Pago (Sprint 42, decisão 4) — antes, o segundo deixava a cobrança paga e o acréscimo no
/// caixa (F6). A tarifa lançada fica nos dois: o Mercado Pago não a devolve.
/// <para>
/// A cobrança passa a <see cref="StatusDaCobrancaBancaria.Estornada"/>: é o que impede o aviso atrasado de desfazer de
/// novo. Não salva — está na transação de quem chamou, sob a trava da cobrança.
/// </para>
/// </remarks>
/// <param name="receitas">O acréscimo repassado, que volta junto.</param>
public sealed class EstornoDaCobranca(IOutraReceitaRepository receitas)
{
    /// <summary>Estorna o acréscimo repassado, se houve, e marca a cobrança estornada.</summary>
    /// <param name="cobranca">Cobrança paga, travada.</param>
    /// <param name="motivo">Por quê — vai na descrição do estorno do acréscimo.</param>
    public async Task Desfazer(CobrancaBancaria cobranca, string motivo, CancellationToken ct = default)
    {
        if (cobranca.ReceitaDoAcrescimoId is { } receitaDoAcrescimo)
            await receitas.Adicionar(
                OutraReceita.Estorno(
                    receitaDoAcrescimo,
                    $"Estorno da taxa do cartão — {cobranca.Referencia} ({motivo})",
                    cobranca.AcrescimoEmCentavos,
                    DataUtils.Hoje(),
                    CategoriaDeOutraReceita.Outros
                ),
                ct
            );

        cobranca.Estornada();
    }
}
