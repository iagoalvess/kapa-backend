namespace Backend.Business.Formaturas.Models;

/// <summary>Formatura da qual o usuário participa, com o papel dele.</summary>
/// <remarks>
/// Curso, instituição e ano vêm junto do nome porque duas turmas homônimas no seletor são
/// indistinguíveis — e escolher a errada mostra o caixa de outra turma.
/// </remarks>
/// <param name="Id">Identificador da formatura.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">Semestre de conclusão: 1 ou 2. Com o ano, forma a turma ("2027.1").</param>
/// <param name="Papel">Papel do usuário nesta formatura.</param>
public sealed record FormaturaDoUsuario(Guid Id, string Nome, string Curso, string Instituicao, int Ano, int Semestre, string Papel);

/// <summary>Dados cadastrais da formatura, informados na criação e na edição.</summary>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">Semestre de conclusão: 1 ou 2.</param>
/// <param name="PrevisaoDeColacao">Data prevista da colação, se houver.</param>
/// <param name="PrevisaoDaFesta">Data prevista da festa, se houver.</param>
/// <param name="QuantidadeEstimadaDeFormandos">Quantos formandos a comissão espera.</param>
public sealed record DadosDaFormatura(
    string Nome,
    string Instituicao,
    string Curso,
    int Ano,
    int Semestre,
    DateOnly? PrevisaoDeColacao,
    DateOnly? PrevisaoDaFesta,
    int QuantidadeEstimadaDeFormandos
);

/// <summary>A formatura selecionada, como a tela de configurações e a faixa de status a enxergam.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">Semestre de conclusão.</param>
/// <param name="PrevisaoDeColacao">Data prevista da colação.</param>
/// <param name="PrevisaoDaFesta">Data prevista da festa.</param>
/// <param name="QuantidadeEstimadaDeFormandos">Quantos formandos a comissão espera.</param>
/// <param name="Status">Situação no ciclo de vida.</param>
/// <param name="CriadoEm">Criação, em UTC.</param>
/// <param name="AtivadaEm">Primeira ativação, em UTC.</param>
/// <param name="EncerradaEm">Encerramento, em UTC.</param>
public sealed record FormaturaDetalhe(
    Guid Id,
    string Nome,
    string Instituicao,
    string Curso,
    int Ano,
    int Semestre,
    DateOnly? PrevisaoDeColacao,
    DateOnly? PrevisaoDaFesta,
    int QuantidadeEstimadaDeFormandos,
    StatusDaFormatura Status,
    DateTime CriadoEm,
    DateTime? AtivadaEm,
    DateTime? EncerradaEm
);

/// <summary>Vínculo ativo do usuário, do jeito que a emissão de sessão precisa dele.</summary>
/// <param name="FormaturaId">Formatura à qual o vínculo pertence.</param>
/// <param name="Papel">Papel do usuário nela.</param>
public sealed record VinculoAtivo(Guid FormaturaId, string Papel);

/// <summary>Membro de uma formatura, como a gestão enxerga: acesso e cadastro na mesma linha.</summary>
/// <param name="UsuarioId">Usuário vinculado.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="Ativo">Se o vínculo ainda vale.</param>
/// <param name="NomeCompleto">Nome civil, se já informado no cadastro.</param>
/// <param name="Completude">Percentual do cadastro preenchido, de 0 a 100.</param>
/// <param name="EssencialPendente">Se falta nome completo, CPF ou telefone.</param>
public sealed record MembroDaFormatura(
    Guid UsuarioId,
    string Nome,
    string Email,
    string Papel,
    bool Ativo,
    string? NomeCompleto,
    int Completude,
    bool EssencialPendente
);

/// <summary>Recorte da lista por situação do cadastro.</summary>
public enum SituacaoDoCadastro
{
    /// <summary>Falta nome completo, CPF ou telefone.</summary>
    Pendente,

    /// <summary>Falta qualquer item.</summary>
    Incompleto,

    /// <summary>Tudo preenchido.</summary>
    Completo,
}

/// <summary>Filtros da listagem de membros.</summary>
/// <param name="Busca">Trecho do nome de exibição, do nome civil ou do e-mail, sem diferenciar maiúsculas.</param>
/// <param name="Ativo">Só ativos (<c>true</c>), só removidos (<c>false</c>) ou todos (nulo).</param>
/// <param name="Papel">Só este papel, ou todos (nulo). Ver <see cref="PapelNaFormatura"/>.</param>
/// <param name="Cadastro">Só esta situação do cadastro, ou todas (nulo).</param>
public sealed record FiltroDeMembros(string? Busca, bool? Ativo, string? Papel = null, SituacaoDoCadastro? Cadastro = null);

/// <summary>Quantos vínculos a formatura tem num papel e numa situação.</summary>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="Ativo">Situação do vínculo.</param>
/// <param name="Quantidade">Vínculos nessa combinação.</param>
public sealed record ContagemDeMembros(string Papel, bool Ativo, int Quantidade);

/// <summary>Novo papel de um membro.</summary>
/// <param name="Papel">Papel pretendido. Ver <see cref="PapelNaFormatura"/>.</param>
public sealed record AlterarPapel(string Papel);
