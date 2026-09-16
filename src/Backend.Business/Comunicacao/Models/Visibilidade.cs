using Backend.Business.Formaturas.Models;

namespace Backend.Business.Comunicacao.Models;

/// <summary>
/// Para quem um aviso ou documento aparece.
/// </summary>
/// <remarks>
/// Existe desde o primeiro aviso (decisão 4): ata de reunião interna e comunicado público não moram
/// no mesmo lugar sem um campo que os separe, e acrescentá-lo depois de 40 documentos publicados é
/// revisar os 40 na mão.
/// <para>
/// Os valores são explícitos e o pedido chega anulável: sem escolha, o validador recusa. Um
/// <c>0</c> implícito que valesse <see cref="Turma"/> publicaria para todo mundo o que a comissão
/// esqueceu de marcar.
/// </para>
/// </remarks>
public enum Visibilidade
{
    /// <summary>Todo membro da formatura lê.</summary>
    Turma = 1,

    /// <summary>Só a gestão (Presidente, Tesoureiro e Comissão) lê.</summary>
    SomenteComissao = 2,
}

/// <summary>Quem enxerga o quê.</summary>
public static class Visibilidades
{
    /// <summary>Se o papel lê o que é <see cref="Visibilidade.SomenteComissao"/>.</summary>
    /// <remarks>Sem papel — vínculo removido no meio do caminho —, lê só o da turma.</remarks>
    /// <param name="papel">Papel ativo de quem consulta.</param>
    public static bool VeInternos(string? papel) => papel is not null && PapelNaFormatura.Gestao.Contains(papel);
}
