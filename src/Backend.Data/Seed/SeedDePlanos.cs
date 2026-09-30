using Backend.Business.Assinaturas.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Data.Seed;

/// <summary>
/// Catálogo de planos da licença: dois pacotes, cada um em ciclo mensal e anual.
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

    /// <summary>Anual sai 20% mais barato que doze meses avulsos.</summary>
    /// <remarks>
    /// A tela não repete esse número: ela o calcula de <c>PrecoCheioEmCentavos</c>, que sai daqui.
    /// <para>
    /// 20% é o teto usual do mercado — acima disso o mensal deixa de se sustentar. A alternativa
    /// comum, "dois meses grátis" (paga dez, leva doze), dá 16,7%.
    /// </para>
    /// </remarks>
    private const int DescontoAnualEmPorcento = 20;

    /// <summary>
    /// O que o gratuito libera: o ciclo do dinheiro fechado, da cobrança ao caixa, para a comissão conhecer.
    /// </summary>
    /// <remarks>
    /// É a base das três listas — o gratuito, o Essencial (este mais a festa) e o Premium (o Essencial mais
    /// os diferenciais). Repetir a mão os códigos é como o grátis passa a liberar um módulo que o pago cobra.
    /// </remarks>
    private static readonly string[] ModulosDoGratuito = [Modulo.Membros, Modulo.Termo, Modulo.Cobrancas, Modulo.Pix, Modulo.Despesas, Modulo.Caixa];

    /// <summary>O que o Essencial libera: o do gratuito mais a festa, que vende a terceiros (Sprint 45, P1).</summary>
    private static readonly string[] ModulosDoEssencial = [.. ModulosDoGratuito, Modulo.Festa];

    /// <summary>O que o Premium acrescenta: os diferenciais, não o necessário.</summary>
    private static readonly string[] DiferenciaisDoPremium = [Modulo.Mesas, Modulo.Mural, Modulo.Avisos, Modulo.Relatorios, Modulo.Auditoria];

    /// <summary>Código do plano com que toda turma nasce.</summary>
    public const string CodigoGratuito = Plano.CodigoGratuito;

    /// <summary>Quantas pessoas a turma gratuita comporta, Presidente incluso.</summary>
    /// <remarks>Turma que já existe lê a linha do catálogo, que o seed não reescreve: mudar aqui pede migration.</remarks>
    public const int LimiteDoGratuito = 5;

    /// <summary>
    /// O plano com que a turma nasce: a comissão monta tudo e conhece o produto sem pagar.
    /// </summary>
    /// <remarks>
    /// <c>LimiteDeFormandos</c> é o paywall: desde 22/09/2026 <c>VagasDoPlano</c> conta <b>todo</b>
    /// papel, e o grátis comporta <see cref="LimiteDoGratuito"/> pessoas — o Presidente e uma comissão
    /// pequena, para verem o sistema. Antes era zero vaga de formando e comissão à vontade, e a turma
    /// inteira entrava como "Comissão" e pagava as parcelas sem contratar. Convite de Formando continua
    /// exigindo turma contratada (<c>convite.formatura_nao_contratada</c>).
    /// <para>
    /// <c>Ativo = false</c> de propósito: ele não aparece na vitrine e o checkout não o aceita
    /// (<c>ObterPlanoAtivo</c> filtra por <c>Ativo</c>). Plano gratuito é atribuído, nunca escolhido.
    /// </para>
    /// <para>
    /// Os módulos são os de <c>ModulosDoGratuito</c>, a base do Essencial — mexer nela move os dois juntos.
    /// A festa fica de fora desde 29/09/2026 (Sprint 45, P1): vender convite a terceiros é operar dinheiro. O que o grátis libera também pode ser editado na linha do catálogo no banco,
    /// que o seed não reescreve.
    /// </para>
    /// </remarks>
    private static Plano Gratuito() =>
        new()
        {
            Codigo = CodigoGratuito,
            Nome = "Gratuito",
            Descricao = "Para a comissão montar a turma e conhecer o Kapa.",
            PrecoEmCentavos = 0,
            Ciclo = CicloDeCobranca.Mensal,
            LimiteDeFormandos = LimiteDoGratuito,
            Modulos = [.. ModulosDoGratuito],
            Ativo = false,
        };

    /// <summary>Os dois pacotes pagos, do menor para o maior.</summary>
    /// <remarks>
    /// Dois, e não três: a escada de antes (Essencial, Completo, Turma Grande) obrigava a comissão
    /// a comparar três listas de módulos para descobrir de qual precisava. Agora a pergunta é uma
    /// só — a turma cabe em 50? —, e o Premium se vende pelos diferenciais (as mesas entraram entre eles em 29/09/2026), não por um
    /// pedaço do necessário que foi retirado do Essencial.
    /// </remarks>
    private static readonly Pacote[] Pacotes =
    [
        new("essencial", "Essencial", "Cobrar a turma, pagar os fornecedores e fechar o caixa.", 2990, 50, ModulosDoEssencial, false),
        new(
            "premium",
            "Premium",
            "Turma grande, com mural, régua de cobrança e prestação de contas.",
            4990,
            400,
            [.. ModulosDoEssencial, .. DiferenciaisDoPremium],
            true
        ),
    ];

    /// <summary>Insere os planos que ainda não existem.</summary>
    /// <param name="provider">Provedor de serviços com escopo já aberto.</param>
    public static async Task AplicarAsync(IServiceProvider provider, CancellationToken ct = default)
    {
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(SeedDePlanos));
        var db = provider.GetRequiredService<AppDbContext>();

        var existentes = await db.Planos.Select(p => p.Codigo).ToListAsync(ct);
        var novos = Pacotes.SelectMany(Ciclos).Append(Gratuito()).Where(p => !existentes.Contains(p.Codigo)).ToList();

        if (novos.Count == 0)
            return;

        db.Planos.AddRange(novos);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("{Quantidade} planos criados no catálogo.", novos.Count);
    }

    /// <summary>O mesmo pacote nos dois ciclos.</summary>
    /// <remarks>
    /// O anual cobra doze meses menos o desconto, arredondado para real cheio: R$ 287,04 vira
    /// R$ 287,00 — preço de vitrine não tem centavo quebrado. O preço cheio guardado é o dos doze
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
