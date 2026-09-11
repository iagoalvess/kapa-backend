namespace Backend.Business.Formaturas.Models;

/// <summary>
/// Papel que um usuário exerce dentro de uma formatura.
/// </summary>
/// <remarks>
/// Não é role do Identity. Role do Identity é o nível plataforma (<c>Administrador</c>,
/// <c>Usuario</c>); o papel aqui é por turma, e a mesma pessoa pode ser tesoureira de uma e
/// formanda de outra. A matriz de permissões por papel é a Sprint 1.
/// </remarks>
public static class PapelNaFormatura
{
    /// <summary>Preside a comissão de formatura.</summary>
    public const string Presidente = nameof(Presidente);

    /// <summary>Responde pelo caixa da turma.</summary>
    public const string Tesoureiro = nameof(Tesoureiro);

    /// <summary>Integra a comissão, sem cargo específico.</summary>
    public const string Comissao = nameof(Comissao);

    /// <summary>Formando, sem responsabilidade de gestão.</summary>
    public const string Formando = nameof(Formando);

    /// <summary>Todos os papéis, na ordem de responsabilidade.</summary>
    public static readonly IReadOnlyList<string> Todos = [Presidente, Tesoureiro, Comissao, Formando];
}
