using Backend.Business.Abstractions;
using Backend.Business.Recebimentos.Models;

namespace Backend.Business.Recebimentos.Interfaces;

/// <summary>
/// O Mercado Pago da turma: conectar por OAuth, desconectar e renovar a autorização (Sprint 25, Parte A).
/// </summary>
public interface IProvedorDaTurmaService
{
    /// <summary>A conexão da turma, se houver. Nunca o token.</summary>
    Task<Result<ProvedorDaTurma>> Obter(CancellationToken ct = default);

    /// <summary>A página de autorização do Mercado Pago, com o <c>state</c> desta turma e deste presidente.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem clicou em conectar.</param>
    /// <returns>
    /// A URL; 409 <c>recebimento.provedor_desligado</c> quando a aplicação do Kapa não está configurada, e 409
    /// <c>recebimento.chave_pix_obrigatoria</c> quando a turma ainda não tem chave PIX — o chão da P7.
    /// </returns>
    Task<Result<AutorizacaoDoProvedor>> IniciarConexao(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// O retorno do Mercado Pago: confere o <c>state</c>, troca o código pelos tokens e grava a credencial.
    /// </summary>
    /// <remarks>Chega sem sessão; quem diz a turma é o <c>state</c> assinado, e o escopo é apontado a partir dele.</remarks>
    /// <param name="codigo">O <c>code</c> do retorno.</param>
    /// <param name="state">O <c>state</c> assinado em <see cref="IniciarConexao"/>.</param>
    Task<Result<ProvedorConectado>> ConcluirConexao(string? codigo, string? state, CancellationToken ct = default);

    /// <summary>Tira o Mercado Pago da turma. As cobranças já emitidas seguem pagáveis e são conciliadas até vencer.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem desconecta.</param>
    Task<Result> Desconectar(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Renova a autorização da turma do escopo antes de vencer. Para o worker.</summary>
    Task<Result> Renovar(CancellationToken ct = default);
}
