using Backend.Business.Abstractions;
using Backend.Business.IA.Models;

namespace Backend.Business.IA.Interfaces;

/// <summary>
/// A porta única da plataforma para modelos de linguagem.
/// </summary>
/// <remarks>
/// Feature nova com IA = uma instrução, uma lista de modelos na seção dela e uma chamada a
/// <see cref="Completar"/>. Provedor, chave, fallback entre modelos, tempo de espera e log ficam
/// atrás desta interface — e é por ela que os testes das features trocam o provedor por um dublê.
/// <para>
/// Quem chama é o worker: resposta de modelo leva segundos, e requisição de usuário não espera por
/// ela. O resultado se grava e a tela lê o que foi gravado (ver o resumo do termo, Sprint 24).
/// </para>
/// </remarks>
public interface IModeloDeLinguagem
{
    /// <summary>Se a IA está configurada. Desligada, a feature não deve nem montar o pedido.</summary>
    bool Ligado { get; }

    /// <summary>Pede a resposta, modelo a modelo, até um devolver texto.</summary>
    /// <remarks>
    /// Erro HTTP (inclusive o 429 da cota), tempo esgotado, corpo ilegível ou resposta vazia passam
    /// ao próximo modelo. Acabou a lista: falha <c>ia.indisponivel</c>, e quem chama tenta depois.
    /// </remarks>
    /// <param name="pedido">Instrução, texto e modelos.</param>
    Task<Result<RespostaDoModelo>> Completar(PedidoAoModelo pedido, CancellationToken ct = default);
}
