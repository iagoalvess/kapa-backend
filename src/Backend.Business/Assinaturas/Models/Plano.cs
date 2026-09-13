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
    /// <summary>Identificador estável e legível (<c>completo</c>). É o que o front envia no checkout.</summary>
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Nome exibido no card.</summary>
    public string Nome { get; set; } = string.Empty;

    /// <summary>Preço de um ciclo, em centavos de real.</summary>
    public long PrecoEmCentavos { get; set; }

    /// <summary>Periodicidade da cobrança.</summary>
    public CicloDeCobranca Ciclo { get; set; } = CicloDeCobranca.Mensal;

    /// <summary>Quantos formandos a turma pode ter neste plano.</summary>
    public int LimiteDeFormandos { get; set; }

    /// <summary>Destacado na tela de planos.</summary>
    public bool Recomendado { get; set; }

    /// <summary>Se ainda pode ser contratado. Plano retirado continua valendo para quem já assinou.</summary>
    public bool Ativo { get; set; } = true;
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
