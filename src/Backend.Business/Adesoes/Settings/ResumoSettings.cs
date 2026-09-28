namespace Backend.Business.Adesoes.Settings;

/// <summary>
/// Seção <c>ResumoDoTermo</c>: o que é só do resumo do termo por IA (Sprint 24, decisão 6).
/// </summary>
/// <remarks>
/// Provedor e chave são da plataforma, na seção <c>IA</c> (<c>IaSettings</c>); aqui ficam os modelos que
/// esta feature pede e o teto por rodada. Sem chave na seção <c>IA</c>, a feature fica desligada.
/// </remarks>
public sealed class ResumoSettings
{
    /// <summary>Nome da seção na configuração.</summary>
    public const string Secao = "ResumoDoTermo";

    /// <summary>Ids dos modelos, em ordem de preferência: o primeiro que responder ganha.</summary>
    /// <remarks>
    /// <c>ponytail:</c> retrato do "most popular" gratuito do OpenRouter em 16/09/2026. Ids <c>:free</c>
    /// entram e saem do catálogo sem aviso — reconferir só quando a feature parar de gravar resumo.
    /// </remarks>
    public string[] Modelos { get; init; } = [];

    /// <summary>Teto de termos por rodada do job, para uma rodada não queimar a cota diária.</summary>
    public int PorRodada { get; init; } = 10;
}
