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
/// <param name="DesligadoEm">
/// Quando ele foi desligado desta turma, em UTC; nulo para quem continua nela. A turma desligada
/// segue na lista, em leitura: o extrato é a prova do que ele pagou.
/// </param>
public sealed record FormaturaDoUsuarioDTO(
    Guid Id,
    string Nome,
    string Curso,
    string Instituicao,
    int Ano,
    int Semestre,
    string Papel,
    DateTime? DesligadoEm
);

/// <summary>
/// Corpo da criação e da edição de formatura.
/// </summary>
/// <remarks>
/// Sem as datas de colação e de festa desde a Sprint 19: quem as grava é <c>POST /agenda</c>, e é a
/// agenda que as devolve no detalhe da turma.
/// </remarks>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">1 ou 2.</param>
/// <param name="RefreshToken">
/// Refresh token atual, só na criação e só com o modo cookie desligado — com ele ligado, é ignorado.
/// </param>
public sealed record DadosDaFormaturaRequestDTO(string Nome, string Instituicao, string Curso, int Ano, int Semestre, string? RefreshToken = null);

/// <summary>A formatura selecionada.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">Semestre de conclusão.</param>
/// <param name="PrevisaoDeColacao">Data prevista da colação, lida do evento da agenda.</param>
/// <param name="PrevisaoDaFesta">Data prevista da festa, lida do evento da agenda.</param>
/// <param name="Status"><c>Ativa</c>, <c>Suspensa</c>, <c>Encerrada</c> ou <c>Descartada</c>.</param>
/// <param name="EncerradaEm">Encerramento, em UTC.</param>
/// <param name="JaContratou">Se a turma já contratou um plano alguma vez. Falsa é a turma no gratuito.</param>
public sealed record FormaturaDetalheDTO(
    Guid Id,
    string Nome,
    string Instituicao,
    string Curso,
    int Ano,
    int Semestre,
    DateOnly? PrevisaoDeColacao,
    DateOnly? PrevisaoDaFesta,
    StatusDaFormatura Status,
    DateTime? EncerradaEm,
    bool JaContratou
);

/// <summary>Os passos da comissão até a turma estar rodando — o bloco "Primeiros passos" do Início.</summary>
/// <param name="ComissaoMontada">Mais de uma pessoa ativa na gestão. Opcional: não entra em <paramref name="Concluidos"/>.</param>
/// <param name="PlanoDeCobrancaEmVigor">Há plano de cobrança vigente.</param>
/// <param name="TermoPublicado">O termo de adesão tem versão publicada.</param>
/// <param name="RecebimentosConfigurados">Cobrança automática, transferência, dinheiro ou PIX de titular conferido.</param>
/// <param name="PlanoContratado">A turma já contratou um plano — o mesmo <c>ja_contratou</c> da formatura.</param>
/// <param name="FormandosNaTurma">Há ao menos um formando ativo.</param>
/// <param name="Concluidos">Todos os passos obrigatórios feitos: o bloco não aparece.</param>
public sealed record PrimeirosPassosDTO(
    bool ComissaoMontada,
    bool PlanoDeCobrancaEmVigor,
    bool TermoPublicado,
    bool RecebimentosConfigurados,
    bool PlanoContratado,
    bool FormandosNaTurma,
    bool Concluidos
);
