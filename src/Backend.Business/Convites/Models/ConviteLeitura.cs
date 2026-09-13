namespace Backend.Business.Convites.Models;

/// <summary>
/// O que o convidado vê antes de entrar.
/// </summary>
/// <remarks>
/// O mínimo, de propósito: a URL circula em grupo de WhatsApp e é pública na prática. Nunca
/// membros, valores ou quem convidou.
/// </remarks>
/// <param name="Turma">Nome da formatura.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Papel">Papel oferecido.</param>
/// <param name="EmailMascarado">
/// Só no convite pessoal, mascarado (<c>a*****a@gmail.com</c>): quem abre sabe com qual conta
/// entrar sem precisar tentar a errada. Quem tem o link recebeu o e-mail — o mascarado não conta
/// nada novo.
/// </param>
public sealed record ConvitePublico(string Turma, string Instituicao, string Papel, string? EmailMascarado);

/// <summary>Um convite como a comissão o acompanha. Sem token: ele só existe na criação.</summary>
/// <param name="Id">Identificador, usado na revogação.</param>
/// <param name="Email">E-mail convidado; nulo no link da turma.</param>
/// <param name="Papel">Papel oferecido.</param>
/// <param name="ExpiraEm">Validade, em UTC.</param>
/// <param name="UsosMaximos">Limite de aceites; nulo é ilimitado.</param>
/// <param name="UsosFeitos">Quantas pessoas entraram por ele.</param>
/// <param name="Status">Situação agora.</param>
/// <param name="CriadoEm">Criação, em UTC.</param>
public sealed record ConviteResumo(
    Guid Id,
    string? Email,
    string Papel,
    DateTime ExpiraEm,
    int? UsosMaximos,
    int UsosFeitos,
    StatusDoConvite Status,
    DateTime CriadoEm
);

/// <summary>Pedido de convite.</summary>
/// <param name="Email">E-mail para o convite nominal; nulo cria o link da turma.</param>
/// <param name="Papel">Papel oferecido; nulo é Formando.</param>
/// <param name="DiasDeValidade">Validade em dias; nulo usa o padrão de cada tipo.</param>
/// <param name="UsosMaximos">Limite de aceites do link; ignorado no nominal, que é sempre um.</param>
public sealed record CriarConvite(string? Email, string? Papel, int? DiasDeValidade, int? UsosMaximos);

/// <summary>Convite recém-criado, com o link — a única vez que ele aparece.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Link">Endereço de aceite, com o token.</param>
/// <param name="ExpiraEm">Validade, em UTC.</param>
public sealed record ConviteCriado(Guid Id, string Link, DateTime ExpiraEm);
