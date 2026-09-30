using Backend.Business.Abstractions;

namespace Backend.Business.Assinaturas.Models;

/// <summary>
/// Pacote da licença SaaS que a comissão contrata.
/// </summary>
/// <remarks>
/// Herda de <see cref="Entity"/>, e não de <see cref="EntidadeDaFormatura"/>: o catálogo é da
/// plataforma, igual para toda turma, e é lido antes de haver formatura selecionada.
/// <para>
/// Preço em centavos, <c>long</c>. O provedor trabalha em centavos; converter para
/// <c>decimal</c> na borda e voltar a converter é onde o centavo some.
/// </para>
/// </remarks>
public class Plano : Entity
{
    /// <summary>Código do plano com que toda turma nasce: o que responde quando não há assinatura paga valendo.</summary>
    /// <remarks>
    /// Mora aqui, e não só no seed, porque o domínio precisa dele para responder "a turma pagou?" (Sprint 45,
    /// P3) — e o <c>Business</c> não enxerga o <c>Data</c>.
    /// </remarks>
    public const string CodigoGratuito = "gratuito";

    /// <summary>Identificador estável e legível (<c>completo</c>). É o que o front envia no checkout.</summary>
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Nome exibido no card.</summary>
    public string Nome { get; set; } = string.Empty;

    /// <summary>Uma linha embaixo do nome, no card: para que turma o plano serve.</summary>
    public string Descricao { get; set; } = string.Empty;

    /// <summary>
    /// Módulos incluídos, na ordem em que a tela os lista.
    /// </summary>
    /// <remarks>
    /// Regra de acesso desde 18/09/2026: é sobre esta lista que <c>[ExigeModulo]</c> decide, e o
    /// código de cada item vem de <see cref="Modulo"/>. Guardado como <c>text[]</c> — é lista fechada,
    /// lida junto com o plano e nunca consultada sozinha, então uma tabela filha só custaria um join.
    /// </remarks>
    public List<string> Modulos { get; set; } = [];

    /// <summary>Preço de um ciclo, em centavos de real.</summary>
    public long PrecoEmCentavos { get; set; }

    /// <summary>
    /// Preço sem desconto, em centavos — o valor riscado no card. Nulo quando não há desconto.
    /// </summary>
    /// <remarks>
    /// No anual, é o que doze meses avulsos custariam. Fica aqui, e não no front: a porcentagem
    /// anunciada tem de sair da mesma tabela que cobra, senão a vitrine promete um desconto que a
    /// cobrança não dá. O front só divide um pelo outro para escrever "economize 15%".
    /// </remarks>
    public long? PrecoCheioEmCentavos { get; set; }

    /// <summary>Periodicidade da cobrança.</summary>
    public CicloDeCobranca Ciclo { get; set; } = CicloDeCobranca.Mensal;

    /// <summary>Quantos formandos a turma pode ter neste plano.</summary>
    public int LimiteDeFormandos { get; set; }

    /// <summary>Destacado na tela de planos.</summary>
    public bool Recomendado { get; set; }

    /// <summary>Se ainda pode ser contratado. Plano retirado continua valendo para quem já assinou.</summary>
    public bool Ativo { get; set; } = true;

    /// <summary>Se é um plano contratado, e não o gratuito que responde por quem não pagou.</summary>
    /// <remarks>
    /// É a pergunta de "pode ter formando?" (Sprint 45, P3), lida do plano <b>vigente</b>: a turma cuja
    /// assinatura venceu volta ao gratuito e deixa de convidar, mesmo tendo pago um dia. Sem setter, fica
    /// fora do mapeamento.
    /// </remarks>
    public bool Pago => Codigo != CodigoGratuito;
}

/// <summary>Periodicidade da cobrança de um plano.</summary>
/// <remarks>Gravado como texto, pelo mesmo motivo de <c>StatusDaFormatura</c>.</remarks>
public enum CicloDeCobranca
{
    /// <summary>Um mês por cobrança.</summary>
    Mensal,

    /// <summary>Um ano por cobrança.</summary>
    Anual,
}

/// <summary>Duração de um ciclo de cobrança.</summary>
public static class CicloDeCobrancaExtensions
{
    /// <summary>Soma um ciclo à data.</summary>
    /// <remarks>Por calendário, e não por dias fixos: 31 de janeiro mais um mês é 28 ou 29 de fevereiro.</remarks>
    /// <param name="ciclo">Ciclo do plano.</param>
    /// <param name="inicio">Início do ciclo, em UTC.</param>
    public static DateTime Somar(this CicloDeCobranca ciclo, DateTime inicio) =>
        ciclo == CicloDeCobranca.Anual ? inicio.AddYears(1) : inicio.AddMonths(1);
}
