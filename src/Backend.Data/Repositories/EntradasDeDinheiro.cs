using Backend.Business.Financeiro.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// O dinheiro que entrou na conta da turma, das duas origens: parcela paga e receita recebida.
/// </summary>
/// <remarks>
/// É o <c>UNION ALL</c> sobre o qual toda soma de entrada se apoia — o arrecadado e o gráfico do
/// caixa, o balancete e a comparação com o período anterior. Uma fonte só, para que nenhuma dessas
/// consultas esqueça a receita (o risco nº 1 da Sprint 28) e para que as duas origens sigam juntas
/// na mesma ida ao banco.
/// </remarks>
internal static class EntradasDeDinheiro
{
    /// <summary>Recebimentos não estornados e receitas recebidas, pelo dia em que entraram.</summary>
    /// <param name="db">Contexto da requisição — o filtro global da formatura vale para os dois lados.</param>
    public static IQueryable<EntradaDeDinheiro> Realizadas(AppDbContext db) =>
        db
            .Recebimentos.AsNoTracking()
            .Where(r => r.EstornadoEm == null)
            .Select(r => new EntradaDeDinheiro { Data = r.PagoEm, Valor = r.ValorEmCentavos })
            .Concat(
                db.OutrasReceitas.AsNoTracking()
                    .Where(r => r.Status == StatusDaOutraReceita.Recebida)
                    .Select(r => new EntradaDeDinheiro { Data = r.Data, Valor = r.ValorEmCentavos })
            );
}

/// <summary>Uma entrada de dinheiro, de qualquer origem, como o <c>UNION ALL</c> a projeta.</summary>
/// <remarks>Classe com <c>init</c>, e não record posicional: o EF só traduz operação de conjunto sobre inicialização de membros.</remarks>
internal sealed class EntradaDeDinheiro
{
    /// <summary>Dia em que entrou, ou em que vence.</summary>
    public DateOnly Data { get; init; }

    /// <summary>Valor, em centavos.</summary>
    public long Valor { get; init; }
}
