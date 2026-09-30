using Backend.Business.Convites.Models;

namespace Backend.Api.DTOs.Convites;

/// <summary>Corpo da criação de convite.</summary>
/// <remarks>
/// Validade e limite não se escolhem: o nominal vale 7 dias e um uso; o link da turma, 30 dias e o
/// número estimado de formandos.
/// </remarks>
/// <param name="Email">E-mail do convite nominal; ausente cria o link da turma.</param>
/// <param name="Papel">Papel oferecido; ausente é <c>Formando</c>. Outros exigem Presidente.</param>
public sealed record CriarConviteRequestDTO(string? Email, string? Papel);

/// <summary>Convite recém-criado. O link do nominal não volta em nenhum outro endpoint; o da turma volta na listagem enquanto valer.</summary>
/// <param name="Link">Endereço de aceite, com o token.</param>
public sealed record ConviteCriadoDTO(string Link);

/// <summary>Convite como a comissão o acompanha.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail convidado; nulo no link da turma.</param>
/// <param name="Papel">Papel oferecido.</param>
/// <param name="ExpiraEm">Validade, em UTC.</param>
/// <param name="UsosMaximos">Limite de entradas; nulo é ilimitado.</param>
/// <param name="UsosFeitos">Quantas pessoas entraram por ele.</param>
/// <param name="Status"><c>Pendente</c>, <c>Aceito</c>, <c>Expirado</c> ou <c>Revogado</c>.</param>
/// <param name="Link">Endereço do link da turma vigente, para copiar de novo. Ausente no nominal e no link que já não vale.</param>
public sealed record ConviteResumoDTO(
    Guid Id,
    string? Email,
    string Papel,
    DateTime ExpiraEm,
    int? UsosMaximos,
    int UsosFeitos,
    StatusDoConvite Status,
    string? Link
);

/// <summary>O que o convidado vê antes de entrar: nada além disto.</summary>
/// <param name="Turma">Nome da formatura.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Papel">Papel oferecido.</param>
/// <param name="EmailMascarado">Para qual e-mail o convite pessoal foi enviado, mascarado. Ausente no link da turma.</param>
public sealed record ConvitePublicoDTO(string Turma, string Instituicao, string Papel, string? EmailMascarado);
