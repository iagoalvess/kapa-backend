namespace Backend.Business.Admin.Models;

/// <summary>
/// O que a busca do painel de suporte encontrou.
/// </summary>
/// <remarks>
/// Uma caixa só, dois tipos de resultado: quem atende recebe "paguei e a turma não ativou" e tem
/// na mão ou o nome da turma, ou o e-mail da pessoa — nunca os dois, e nunca um id. Duas caixas de
/// busca separadas obrigariam a adivinhar antes de procurar.
/// </remarks>
/// <param name="Turmas">Formaturas cujo nome, instituição ou curso bate com o termo.</param>
/// <param name="Usuarios">Contas cujo nome ou e-mail bate com o termo.</param>
public sealed record ResultadoDaBusca(IReadOnlyList<TurmaEncontrada> Turmas, IReadOnlyList<UsuarioEncontrado> Usuarios);

/// <summary>Uma turma na lista de resultados.</summary>
/// <param name="Id">Formatura.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Status">Situação da formatura.</param>
/// <param name="Membros">Quantos vínculos ativos ela tem.</param>
public sealed record TurmaEncontrada(Guid Id, string Nome, string Instituicao, string Curso, string Status, int Membros);

/// <summary>Uma conta na lista de resultados.</summary>
/// <param name="Id">Usuário.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Ativo">Se pode autenticar.</param>
/// <param name="Turmas">Em quantas turmas a pessoa tem vínculo ativo.</param>
public sealed record UsuarioEncontrado(Guid Id, string Nome, string Email, bool Ativo, int Turmas);

/// <summary>
/// A turma como o suporte a vê: situação, licença e quem está dentro.
/// </summary>
/// <param name="Id">Formatura.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">Semestre de conclusão.</param>
/// <param name="Status">Situação da formatura.</param>
/// <param name="CriadaEm">Quando a turma nasceu, em UTC.</param>
/// <param name="AtivadaEm">Primeira ativação, em UTC, ou nulo.</param>
/// <param name="Assinatura">A licença mais recente, ou nulo se a turma nunca contratou.</param>
/// <param name="Membros">Vínculos da turma, ativos primeiro.</param>
/// <param name="Parcelas">Quantas parcelas a turma tem geradas.</param>
/// <param name="ParcelasPagas">Quantas já foram baixadas.</param>
/// <param name="Adesoes">Quantos membros assinaram o termo.</param>
public sealed record TurmaNoSuporte(
    Guid Id,
    string Nome,
    string Instituicao,
    string Curso,
    int Ano,
    int Semestre,
    string Status,
    DateTime CriadaEm,
    DateTime? AtivadaEm,
    AssinaturaNoSuporte? Assinatura,
    IReadOnlyList<MembroNoSuporte> Membros,
    int Parcelas,
    int ParcelasPagas,
    int Adesoes
);

/// <summary>A licença da turma, como o suporte a vê.</summary>
/// <param name="Id">Assinatura.</param>
/// <param name="PlanoNome">Nome do plano contratado.</param>
/// <param name="PlanoCodigo">Código do plano.</param>
/// <param name="LimiteDeFormandos">Quantos formandos o plano comporta.</param>
/// <param name="Status">Situação da assinatura.</param>
/// <param name="VigenteAte">Até quando a licença paga vale, em UTC, ou nulo.</param>
/// <param name="CanceladaEm">Quando a renovação foi cancelada, em UTC, ou nulo.</param>
/// <param name="ContratadaEm">Quando a assinatura nasceu, em UTC.</param>
public sealed record AssinaturaNoSuporte(
    Guid Id,
    string PlanoNome,
    string PlanoCodigo,
    int LimiteDeFormandos,
    string Status,
    DateTime? VigenteAte,
    DateTime? CanceladaEm,
    DateTime ContratadaEm
);

/// <summary>
/// Um membro da turma, como o suporte o vê.
/// </summary>
/// <remarks>
/// <see cref="Cpf"/> vem <b>mascarado</b> (<c>***.982.247-**</c>), como sai para a Gestão. Quem
/// atende não precisa do número inteiro para dizer por que o pagamento não entrou; precisa
/// confirmar que está falando com a pessoa certa, e seis dígitos do meio fazem isso.
/// </remarks>
/// <param name="UsuarioId">Conta.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="Ativo">Se o vínculo está ativo.</param>
/// <param name="DesligadoEm">Quando saiu da turma, em UTC, ou nulo.</param>
/// <param name="Cpf">CPF mascarado, ou nulo se o cadastro ainda não o tem.</param>
public sealed record MembroNoSuporte(Guid UsuarioId, string Nome, string Email, string Papel, bool Ativo, DateTime? DesligadoEm, string? Cpf);

/// <summary>
/// A conta como o suporte a vê: situação do acesso e em que turmas a pessoa está.
/// </summary>
/// <param name="Id">Usuário.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="EmailConfirmado">Se o e-mail foi confirmado.</param>
/// <param name="Ativo">Se pode autenticar.</param>
/// <param name="BloqueadoAte">Fim do bloqueio por tentativas de senha, em UTC, ou nulo.</param>
/// <param name="TentativasFalhas">Tentativas de senha erradas acumuladas.</param>
/// <param name="Perfis">Perfis de plataforma.</param>
/// <param name="AnonimizadoEm">Quando a conta foi anonimizada por pedido de eliminação, ou nulo.</param>
/// <param name="CriadoEm">Quando a conta nasceu, em UTC.</param>
/// <param name="Vinculos">Turmas da pessoa, ativas primeiro.</param>
public sealed record UsuarioNoSuporte(
    Guid Id,
    string Nome,
    string Email,
    bool EmailConfirmado,
    bool Ativo,
    DateTime? BloqueadoAte,
    int TentativasFalhas,
    IReadOnlyList<string> Perfis,
    DateTime? AnonimizadoEm,
    DateTime CriadoEm,
    IReadOnlyList<VinculoNoSuporte> Vinculos
);

/// <summary>Uma turma de que a pessoa participa.</summary>
/// <param name="FormaturaId">Formatura.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Status">Situação da formatura.</param>
/// <param name="Papel">Papel da pessoa nela.</param>
/// <param name="Ativo">Se o vínculo está ativo.</param>
/// <param name="DesligadoEm">Quando saiu, em UTC, ou nulo.</param>
public sealed record VinculoNoSuporte(
    Guid FormaturaId,
    string Nome,
    string Instituicao,
    string Status,
    string Papel,
    bool Ativo,
    DateTime? DesligadoEm
);
