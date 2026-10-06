using Backend.Business.Abstractions;
using Backend.Business.Recebimentos.Models;

namespace Backend.Business.Recebimentos.Interfaces;

/// <summary>
/// Como a turma recebe: cadastrar os meios, trocá-los, testar a chave com R$ 1,00 e confirmar o titular.
/// </summary>
public interface IContaDeRecebimentoService
{
    /// <summary>A conta da turma, se já cadastrada.</summary>
    Task<Result<ContaDeRecebimentoDaTurma>> Obter(CancellationToken ct = default);

    /// <summary>
    /// Cadastra ou troca os meios. Mexer no PIX ou na transferência só pede — o link vai ao e-mail de quem pediu;
    /// mexer só no dinheiro vale na hora. Mexer no PIX desfaz a conferência.
    /// </summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem grava.</param>
    /// <param name="meios">Os meios que a turma passa a aceitar — ao menos um.</param>
    Task<Result<GravacaoDaConta>> Gravar(Guid formaturaId, Guid usuarioId, MeiosDaConta meios, CancellationToken ct = default);

    /// <summary>Aplica a troca pedida em <see cref="Gravar"/>, pelo link do e-mail.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem confirma — tem de ser quem pediu.</param>
    /// <param name="token">O token do link.</param>
    Task<Result<ContaDeRecebimentoDetalhe>> Confirmar(Guid formaturaId, Guid usuarioId, string? token, CancellationToken ct = default);

    /// <summary>O copia-e-cola de R$ 1,00 para a chave gravada. 409 se a turma não aceita PIX.</summary>
    Task<Result<PixDeTeste>> GerarPixDeTeste(CancellationToken ct = default);

    /// <summary>Registra que o banco mostrou o titular cadastrado no PIX de teste.</summary>
    /// <param name="usuarioId">Quem confirma.</param>
    Task<Result<ContaDeRecebimentoDetalhe>> Conferir(Guid usuarioId, CancellationToken ct = default);
}
