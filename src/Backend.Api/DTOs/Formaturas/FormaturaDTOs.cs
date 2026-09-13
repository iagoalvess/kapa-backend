using Backend.Business.Formaturas.Models;

namespace Backend.Api.DTOs.Formaturas;

/// <summary>Formatura da qual o usuário participa.</summary>
/// <param name="Id">Identificador da formatura, usado em <c>POST /formaturas/{id}/selecionar</c>.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Curso">Curso, para distinguir turmas homônimas no seletor.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">Semestre de conclusão: 1 ou 2. Com o ano, forma a turma ("2027.1").</param>
/// <param name="Papel">Papel do usuário nesta formatura.</param>
public sealed record FormaturaDoUsuarioDTO(Guid Id, string Nome, string Curso, string Instituicao, int Ano, int Semestre, string Papel);

/// <summary>Corpo da criação e da edição de formatura.</summary>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">1 ou 2.</param>
/// <param name="PrevisaoDeColacao">Data prevista da colação (<c>yyyy-MM-dd</c>), opcional.</param>
/// <param name="QuantidadeEstimadaDeFormandos">Quantos formandos a comissão espera.</param>
/// <param name="RefreshToken">
/// Refresh token atual, só na criação e só com o modo cookie desligado — com ele ligado, é ignorado.
/// </param>
public sealed record DadosDaFormaturaRequestDTO(
    string Nome,
    string Instituicao,
    string Curso,
    int Ano,
    int Semestre,
    DateOnly? PrevisaoDeColacao,
    int QuantidadeEstimadaDeFormandos,
    string? RefreshToken = null
);

/// <summary>A formatura selecionada.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">Semestre de conclusão.</param>
/// <param name="PrevisaoDeColacao">Data prevista da colação.</param>
/// <param name="QuantidadeEstimadaDeFormandos">Quantos formandos a comissão espera.</param>
/// <param name="Status"><c>Rascunho</c>, <c>AguardandoPagamento</c>, <c>Ativa</c>, <c>Suspensa</c> ou <c>Encerrada</c>.</param>
/// <param name="CriadoEm">Criação, em UTC.</param>
/// <param name="AtivadaEm">Primeira ativação, em UTC.</param>
/// <param name="EncerradaEm">Encerramento, em UTC.</param>
public sealed record FormaturaDetalheDTO(
    Guid Id,
    string Nome,
    string Instituicao,
    string Curso,
    int Ano,
    int Semestre,
    DateOnly? PrevisaoDeColacao,
    int QuantidadeEstimadaDeFormandos,
    StatusDaFormatura Status,
    DateTime CriadoEm,
    DateTime? AtivadaEm,
    DateTime? EncerradaEm
);
