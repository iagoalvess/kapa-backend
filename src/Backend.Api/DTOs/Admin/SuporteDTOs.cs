using Backend.Api.DTOs.Assinaturas;
using Backend.Api.DTOs.Privacidade;
using Backend.Business.Admin.Models;

namespace Backend.Api.DTOs.Admin;

/// <summary>A turma como o suporte a vê.</summary>
/// <param name="Id">Formatura.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">Semestre de conclusão.</param>
/// <param name="Status">Situação da formatura.</param>
/// <param name="CriadaEm">Nascimento da turma, em UTC (ISO 8601).</param>
/// <param name="AtivadaEm">Primeira ativação, em UTC, ou nulo.</param>
/// <param name="Assinatura">A licença mais recente, ou nulo.</param>
/// <param name="MembrosAtivos">Vínculos ativos. A lista vem paginada de <c>/membros</c>.</param>
/// <param name="Parcelas">Parcelas geradas, sem as canceladas.</param>
/// <param name="ParcelasPagas">Parcelas já baixadas.</param>
/// <param name="Adesoes">Termos assinados.</param>
/// <param name="Pagamentos">Pagamentos do plano, mais recentes primeiro — de onde o suporte estorna.</param>
public sealed record TurmaNoSuporteDTO(
    Guid Id,
    string Nome,
    string Instituicao,
    string Curso,
    int Ano,
    int Semestre,
    string Status,
    DateTime CriadaEm,
    DateTime? AtivadaEm,
    AssinaturaNoSuporteDTO? Assinatura,
    int MembrosAtivos,
    int Parcelas,
    int ParcelasPagas,
    int Adesoes,
    IReadOnlyList<CobrancaDoPlanoDTO> Pagamentos
);

/// <summary>Corpo do estorno.</summary>
/// <param name="Modo"><c>Integral</c> (até 7 dias do pagamento) ou <c>Proporcional</c> (o que falta do ciclo).</param>
public sealed record EstornarPagamentoRequestDTO(ModoDeEstorno Modo);

/// <summary>A licença da turma.</summary>
/// <param name="Id">Assinatura.</param>
/// <param name="PlanoNome">Nome do plano.</param>
/// <param name="PlanoCodigo">Código do plano.</param>
/// <param name="LimiteDeFormandos">Quantos formandos o plano comporta.</param>
/// <param name="Status">Situação da assinatura.</param>
/// <param name="VigenteAte">Fim da vigência paga, em UTC, ou nulo.</param>
/// <param name="CanceladaEm">Cancelamento da renovação, em UTC, ou nulo.</param>
/// <param name="ContratadaEm">Nascimento da assinatura, em UTC.</param>
public sealed record AssinaturaNoSuporteDTO(
    Guid Id,
    string PlanoNome,
    string PlanoCodigo,
    int LimiteDeFormandos,
    string Status,
    DateTime? VigenteAte,
    DateTime? CanceladaEm,
    DateTime ContratadaEm
);

/// <summary>Um membro da turma. O CPF vem mascarado, como sai para a Gestão.</summary>
/// <param name="UsuarioId">Conta.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="Ativo">Se o vínculo está ativo.</param>
/// <param name="DesligadoEm">Saída da turma, em UTC, ou nulo.</param>
/// <param name="Cpf">CPF mascarado (<c>***.982.247-**</c>), ou nulo.</param>
public sealed record MembroNoSuporteDTO(Guid UsuarioId, string Nome, string Email, string Papel, bool Ativo, DateTime? DesligadoEm, string? Cpf);

/// <summary>A conta como o suporte a vê.</summary>
/// <param name="Id">Usuário.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="EmailConfirmado">Se o e-mail foi confirmado.</param>
/// <param name="Ativo">Se pode autenticar.</param>
/// <param name="BloqueadoAte">Fim do bloqueio por tentativas, em UTC, ou nulo.</param>
/// <param name="TentativasFalhas">Tentativas de senha erradas acumuladas.</param>
/// <param name="Perfis">Perfis de plataforma.</param>
/// <param name="AnonimizadoEm">Anonimização por pedido de eliminação, em UTC, ou nulo.</param>
/// <param name="CriadoEm">Nascimento da conta, em UTC.</param>
/// <param name="Vinculos">Turmas da pessoa, ativas primeiro.</param>
/// <param name="ComunicacaoDoKapa">Se recebe as novidades do Kapa, e os últimos e-mails de marketing (Sprint 40).</param>
public sealed record UsuarioNoSuporteDTO(
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
    IReadOnlyList<VinculoNoSuporteDTO> Vinculos,
    ComunicacaoDoKapaDTO ComunicacaoDoKapa
);

/// <summary>Uma turma de que a pessoa participa.</summary>
/// <param name="FormaturaId">Formatura.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Status">Situação da formatura.</param>
/// <param name="Papel">Papel da pessoa.</param>
/// <param name="Ativo">Se o vínculo está ativo.</param>
/// <param name="DesligadoEm">Saída da turma, em UTC, ou nulo.</param>
public sealed record VinculoNoSuporteDTO(
    Guid FormaturaId,
    string Nome,
    string Instituicao,
    string Status,
    string Papel,
    bool Ativo,
    DateTime? DesligadoEm
);
