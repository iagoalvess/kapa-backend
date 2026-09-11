using Backend.Business.Abstractions;

namespace Backend.Business.Formaturas.Models;

/// <summary>
/// A turma de formatura — a unidade de isolamento do produto.
/// </summary>
/// <remarks>
/// Herda de <see cref="Entity"/>, e não de <see cref="EntidadeDaFormatura"/>: é a raiz do
/// isolamento, então não pertence a si mesma.
/// <para>Mínima de propósito. Os demais campos entram na Sprint 2, junto da tela de criação.</para>
/// </remarks>
public class Formatura : Entity
{
    /// <summary>Nome pelo qual a turma se identifica.</summary>
    public string Nome { get; set; } = string.Empty;
}
