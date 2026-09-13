using Backend.Business.Convites.Models;

namespace Backend.Api.DTOs.Convites;

/// <summary>Corpo da criação de convite.</summary>
/// <param name="Email">E-mail do convite nominal; ausente cria o link da turma.</param>
/// <param name="Papel">Papel oferecido; ausente é <c>Formando</c>. Outros exigem Presidente.</param>
/// <param name="DiasDeValidade">Validade em dias (1 a 180); ausente é 7 no nominal e 30 no link.</param>
/// <param name="UsosMaximos">Limite de entradas do link; ausente é ilimitado até expirar. Ignorado no nominal.</param>
public sealed record CriarConviteRequestDTO(string? Email, string? Papel, int? DiasDeValidade, int? UsosMaximos);

/// <summary>Convite recém-criado. O link não é devolvido de novo em nenhum outro endpoint.</summary>
/// <param name="Id">Identificador, usado na revogação.</param>
/// <param name="Link">Endereço de aceite, com o token.</param>
/// <param name="ExpiraEm">Validade, em UTC.</param>
public sealed record ConviteCriadoDTO(Guid Id, string Link, DateTime ExpiraEm);

/// <summary>Convite como a comissão o acompanha.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail convidado; nulo no link da turma.</param>
/// <param name="Papel">Papel oferecido.</param>
/// <param name="ExpiraEm">Validade, em UTC.</param>
/// <param name="UsosMaximos">Limite de entradas; nulo é ilimitado.</param>
/// <param name="UsosFeitos">Quantas pessoas entraram por ele.</param>
/// <param name="Status"><c>Pendente</c>, <c>Aceito</c>, <c>Expirado</c> ou <c>Revogado</c>.</param>
/// <param name="CriadoEm">Criação, em UTC.</param>
public sealed record ConviteResumoDTO(
    Guid Id,
    string? Email,
    string Papel,
    DateTime ExpiraEm,
    int? UsosMaximos,
    int UsosFeitos,
    StatusDoConvite Status,
    DateTime CriadoEm
);

/// <summary>O que o convidado vê antes de entrar: nada além disto.</summary>
/// <param name="Turma">Nome da formatura.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Papel">Papel oferecido.</param>
/// <param name="EmailMascarado">Para qual e-mail o convite pessoal foi enviado, mascarado. Ausente no link da turma.</param>
public sealed record ConvitePublicoDTO(string Turma, string Instituicao, string Papel, string? EmailMascarado);
