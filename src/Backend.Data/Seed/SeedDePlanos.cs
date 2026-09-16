using Backend.Business.Assinaturas.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Data.Seed;

/// <summary>
/// Catálogo de planos da licença: três pacotes, cada um em ciclo mensal e anual.
/// </summary>
/// <remarks>
/// Sem catálogo a tela de planos abre vazia e ninguém contrata — por isso ele nasce com o banco,
/// e não por cadastro manual.
/// <para>
/// <b>Só insere o que falta.</b> Preço e módulos de um plano que já existe ficam como estão: o
/// catálogo é editável no banco, e reescrever a cada subida desfaria a edição. Plano que sai de
/// linha vira <c>Ativo = false</c> pelo banco, nunca por remoção — assinatura antiga aponta para ele.
/// </para>
/// </remarks>
public static class SeedDePlanos
{
    /// <summary>Um pacote, nos dois ciclos.</summary>
    /// <param name="Codigo">Código do ciclo mensal; o anual recebe o sufixo <c>-anual</c>.</param>
    /// <param name="Nome">Nome exibido.</param>
    /// <param name="Descricao">Para que turma serve.</param>
    /// <param name="MensalEmCentavos">Preço de um mês.</param>
    /// <param name="Formandos">Limite de formandos.</param>
    /// <param name="Modulos">Módulos incluídos.</param>
    /// <param name="Recomendado">Se é o destacado na tela.</param>
    private sealed record Pacote(
        string Codigo,
        string Nome,
        string Descricao,
        long MensalEmCentavos,
        int Formandos,
        string[] Modulos,
        bool Recomendado
    );

    /// <summary>Anual sai 15% mais barato que doze meses avulsos.</summary>
    /// <remarks>
    /// A tela não repete esse número: ela o calcula de <c>PrecoCheioEmCentavos</c>, que sai daqui.
    /// </remarks>
    private const int DescontoAnualEmPorcento = 15;

    private const string Membros = "Membros e convites";
    private const string Termo = "Termo de adesão";
    private const string Cobrancas = "Cobranças e parcelas";
    private const string Pix = "Recebimento PIX e conferência";
    private const string Despesas = "Despesas e fornecedores";
    private const string Caixa = "Caixa e relatórios";
    private const string Mural = "Mural e acervo de documentos";
    private const string Avisos = "Avisos e régua de cobrança";
    private const string Contabil = "Painel e exportação contábil";
    private const string Auditoria = "Portal LGPD e auditoria";

    /// <summary>Os três pacotes, do menor para o maior.</summary>
    private static readonly Pacote[] Pacotes =
    [
        new("essencial", "Essencial", "Para a turma que está começando a se organizar.", 14990, 60, [Membros, Termo, Cobrancas, Pix], false),
        new(
            "completo",
            "Completo",
            "O dia a dia da comissão inteiro, do termo ao caixa.",
            34990,
            150,
            [Membros, Termo, Cobrancas, Pix, Despesas, Caixa, Mural, Avisos],
            true
        ),
        new(
            "turma-grande",
            "Turma Grande",
            "Turmas grandes, com prestação de contas e auditoria.",
            69990,
            400,
            [Membros, Termo, Cobrancas, Pix, Despesas, Caixa, Mural, Avisos, Contabil, Auditoria],
            false
        ),
    ];

    /// <summary>Insere os planos que ainda não existem.</summary>
    /// <param name="provider">Provedor de serviços com escopo já aberto.</param>
    public static async Task AplicarAsync(IServiceProvider provider, CancellationToken ct = default)
    {
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(SeedDePlanos));
        var db = provider.GetRequiredService<AppDbContext>();

        var existentes = await db.Planos.Select(p => p.Codigo).ToListAsync(ct);
        var novos = Pacotes.SelectMany(Ciclos).Where(p => !existentes.Contains(p.Codigo)).ToList();

        if (novos.Count == 0)
            return;

        db.Planos.AddRange(novos);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("{Quantidade} planos criados no catálogo.", novos.Count);
    }

    /// <summary>O mesmo pacote nos dois ciclos.</summary>
    /// <remarks>
    /// O anual cobra doze meses menos o desconto, arredondado para real cheio: R$ 3.568,98 vira
    /// R$ 3.569,00 — preço de vitrine não tem centavo quebrado. O preço cheio guardado é o dos doze
    /// meses avulsos, que é de onde o card tira o valor riscado e a porcentagem.
    /// </remarks>
    /// <param name="pacote">O pacote.</param>
    private static IEnumerable<Plano> Ciclos(Pacote pacote)
    {
        var cheio = pacote.MensalEmCentavos * 12;
        var comDesconto = (long)Math.Round(cheio * (1 - DescontoAnualEmPorcento / 100m) / 100) * 100;

        yield return Novo(pacote, CicloDeCobranca.Mensal, pacote.MensalEmCentavos, null);
        yield return Novo(pacote, CicloDeCobranca.Anual, comDesconto, cheio);
    }

    /// <summary>Monta a linha do catálogo.</summary>
    /// <param name="pacote">O pacote.</param>
    /// <param name="ciclo">Periodicidade.</param>
    /// <param name="precoEmCentavos">O que se cobra.</param>
    /// <param name="precoCheioEmCentavos">O valor riscado, quando há desconto.</param>
    private static Plano Novo(Pacote pacote, CicloDeCobranca ciclo, long precoEmCentavos, long? precoCheioEmCentavos) =>
        new()
        {
            Codigo = ciclo == CicloDeCobranca.Anual ? $"{pacote.Codigo}-anual" : pacote.Codigo,
            Nome = pacote.Nome,
            Descricao = pacote.Descricao,
            PrecoEmCentavos = precoEmCentavos,
            PrecoCheioEmCentavos = precoCheioEmCentavos,
            Ciclo = ciclo,
            LimiteDeFormandos = pacote.Formandos,
            Modulos = [.. pacote.Modulos],
            Recomendado = pacote.Recomendado,
        };
}
