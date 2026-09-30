namespace Backend.Business.Loja.Models;

/// <summary>Quem vende, como o comprador precisa ler (P5).</summary>
/// <param name="Turma">Nome da turma.</param>
/// <param name="Contato">E-mail da comissão, se houver.</param>
public sealed record Vendedor(string Turma, string? Contato);
