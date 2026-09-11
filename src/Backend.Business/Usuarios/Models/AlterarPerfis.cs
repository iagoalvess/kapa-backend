namespace Backend.Business.Usuarios.Models;

/// <summary>
/// Novo conjunto de perfis de um usuário.
/// </summary>
/// <remarks>
/// É uma substituição, não um acréscimo: a lista enviada passa a ser **a** lista de perfis do
/// usuário. Endpoint de "adicionar perfil" e "remover perfil" separados produzem estados
/// intermediários — um usuário sem nenhum perfil entre as duas chamadas.
/// </remarks>
/// <param name="Perfis">Perfis que o usuário deve passar a ter.</param>
public sealed record AlterarPerfis(IReadOnlyList<string> Perfis);
