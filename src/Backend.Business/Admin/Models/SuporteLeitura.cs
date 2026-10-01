using Backend.Business.Assinaturas.Models;
using Backend.Business.Marketing.Models;
using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Admin.Models;

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
/// <param name="MembrosAtivos">Vínculos ativos — a lista, paginada, vem de <c>ListarMembros</c>.</param>
/// <param name="Parcelas">Quantas parcelas a turma tem geradas.</param>
/// <param name="ParcelasPagas">Quantas já foram baixadas.</param>
/// <param name="Adesoes">Quantos membros assinaram o termo.</param>
/// <param name="Pagamentos">Os pagamentos do plano, mais recentes primeiro — de onde o suporte estorna (Sprint 37).</param>
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
    int MembrosAtivos,
    int Parcelas,
    int ParcelasPagas,
    int Adesoes,
    IReadOnlyList<CobrancaDoPlanoResumo> Pagamentos
);

/// <summary>Como o suporte estorna um pagamento do plano (P7).</summary>
public enum ModoDeEstorno
{
    /// <summary>Tudo de volta: desistência em até 7 dias do pagamento (Termos, seção 7; art. 49 do CDC).</summary>
    Integral,

    /// <summary>O que falta do ciclo pago: o Kapa encerrou sem culpa da turma, ou ela recusou os Termos novos (seções 13 e 14).</summary>
    Proporcional,
}

/// <summary>Um pagamento do plano na lista mensal da nota fiscal (P6).</summary>
/// <param name="PagaEm">Quando foi pago, em UTC.</param>
/// <param name="Turma">Nome da turma.</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Plano">Plano pago.</param>
/// <param name="Motivo">Ciclo ou diferença de plano.</param>
/// <param name="Meio">Meio.</param>
/// <param name="ValorEmCentavos">Valor pago.</param>
/// <param name="ValorEstornadoEmCentavos">Quanto voltou, se estornado.</param>
/// <param name="PresidenteNome">O tomador: nome do Presidente.</param>
/// <param name="PresidenteEmail">E-mail do Presidente.</param>
/// <param name="PresidenteCpf">CPF inteiro do Presidente — o portal da prefeitura pede (decisão de 28/09/2026).</param>
/// <param name="IdDoPagamento">Id do pagamento no provedor, para conferir com o extrato.</param>
public sealed record PagamentoParaNota(
    DateTime PagaEm,
    string Turma,
    string Instituicao,
    string Plano,
    MotivoDaCobranca Motivo,
    MeioDePagamento Meio,
    long ValorEmCentavos,
    long? ValorEstornadoEmCentavos,
    string? PresidenteNome,
    string? PresidenteEmail,
    string? PresidenteCpf,
    string? IdDoPagamento
);

/// <summary>A licença da turma, como o suporte a vê.</summary>
/// <param name="Id">Assinatura.</param>
/// <param name="PlanoNome">Nome do plano contratado.</param>
/// <param name="LimiteDeFormandos">Quantos formandos o plano comporta.</param>
/// <param name="Status">Situação da assinatura.</param>
/// <param name="VigenteAte">Até quando a licença paga vale, em UTC, ou nulo.</param>
/// <param name="CanceladaEm">Quando a renovação foi cancelada, em UTC, ou nulo.</param>
/// <param name="ContratadaEm">Quando a assinatura nasceu, em UTC.</param>
public sealed record AssinaturaNoSuporte(
    Guid Id,
    string PlanoNome,
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
/// <param name="ComunicacaoDoKapa">Se recebe as novidades do Kapa, o histórico e os últimos e-mails de marketing (Sprint 40).</param>
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
    IReadOnlyList<VinculoNoSuporte> Vinculos,
    ComunicacaoDoKapa ComunicacaoDoKapa
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
