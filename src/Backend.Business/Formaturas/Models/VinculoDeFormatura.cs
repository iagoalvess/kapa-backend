using Backend.Business.Abstractions;

namespace Backend.Business.Formaturas.Models;

/// <summary>
/// Liga um usuário a uma formatura, com o papel que ele exerce nela.
/// </summary>
/// <remarks>
/// O usuário vive **fora** do isolamento: um aluno pode estar em duas turmas e um presidente de
/// comissão pode gerir a formatura de dois cursos. Amarrar a identidade a uma formatura
/// obrigaria a duplicar conta, senha e e-mail — e a explicar isso ao usuário.
/// <para>
/// Herda de <see cref="Entity"/> e carrega <see cref="FormaturaId"/> explícito: listar e
/// selecionar formaturas precisa funcionar **antes** de existir a claim <c>formatura_id</c> no
/// token, o que o filtro global de <see cref="EntidadeDaFormatura"/> impediria.
/// </para>
/// </remarks>
public class VinculoDeFormatura : Entity
{
    /// <summary>Usuário vinculado.</summary>
    public Guid UsuarioId { get; set; }

    /// <summary>Formatura à qual ele pertence.</summary>
    public Guid FormaturaId { get; set; }

    /// <summary>Papel exercido. Ver <see cref="PapelNaFormatura"/>.</summary>
    public string Papel { get; set; } = PapelNaFormatura.Formando;

    /// <summary>Se o vínculo ainda vale. Desligar tira o acesso sem apagar o histórico.</summary>
    public bool Ativo { get; set; } = true;
}
