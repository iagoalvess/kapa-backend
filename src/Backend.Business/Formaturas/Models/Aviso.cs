using Backend.Business.Abstractions;

namespace Backend.Business.Formaturas.Models;

/// <summary>
/// Recado publicado no mural de uma formatura.
/// </summary>
/// <remarks>
/// Primeira entidade isolada do produto, e por isso a que o teste de isolamento exercita. O
/// mural em si — endpoints, tela, autoria, fixação — é a Sprint 11; aqui existe só a linha e o
/// pertencimento, sem nenhuma configuração de isolamento própria: herdar de
/// <see cref="EntidadeDaFormatura"/> basta.
/// </remarks>
public class Aviso : EntidadeDaFormatura
{
    /// <summary>Texto do recado.</summary>
    public string Texto { get; set; } = string.Empty;
}
