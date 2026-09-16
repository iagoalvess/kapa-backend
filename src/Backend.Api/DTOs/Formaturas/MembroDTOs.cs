namespace Backend.Api.DTOs.Formaturas;

/// <summary>Membro da formatura selecionada, com a situação do cadastro.</summary>
/// <param name="UsuarioId">Usuário, usado nas rotas de papel, remoção e cadastro.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel"><c>Presidente</c>, <c>Tesoureiro</c>, <c>Comissao</c> ou <c>Formando</c>.</param>
/// <param name="Ativo">Se o vínculo ainda vale.</param>
/// <param name="NomeCompleto">Nome civil, se já informado.</param>
/// <param name="Completude">Percentual do cadastro preenchido, de 0 a 100.</param>
/// <param name="EssencialPendente">Se falta nome completo, CPF ou telefone.</param>
public sealed record MembroDaFormaturaDTO(
    Guid UsuarioId,
    string Nome,
    string Email,
    string Papel,
    bool Ativo,
    string? NomeCompleto,
    int Completude,
    bool EssencialPendente
);

/// <summary>Quantos vínculos a formatura tem num papel e numa situação.</summary>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="Ativo">Situação do vínculo.</param>
/// <param name="Quantidade">Vínculos nessa combinação.</param>
public sealed record ContagemDeMembrosDTO(string Papel, bool Ativo, int Quantidade);

/// <summary>Corpo da troca de papel.</summary>
/// <param name="Papel">Papel novo.</param>
public sealed record AlterarPapelRequestDTO(string Papel);
