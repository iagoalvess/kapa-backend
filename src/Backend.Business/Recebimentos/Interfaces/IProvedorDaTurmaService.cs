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

    /// <summary>Troca entre a cobrança manual (meios da comissão, aviso e conferência) e a automática (só Mercado Pago).</summary>
    /// <remarks>
    /// 404 <c>recebimento.provedor_nao_conectado</c> sem conexão; 409 <c>recebimento.avisos_pendentes</c> indo para o
    /// automático com aviso na fila, e 409 <c>recebimento.pix_em_aberto</c> voltando ao manual com PIX de parcela ainda
    /// pagável.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem troca — Tesouraria ou Presidente.</param>
    /// <param name="modo">O modo novo.</param>
    /// <returns>A conexão como fica.</returns>
    Task<Result<ProvedorDaTurma>> ConfigurarCobranca(Guid formaturaId, Guid usuarioId, ModoDeCobranca modo, CancellationToken ct = default);

    /// <summary>
    /// Tira o Mercado Pago da turma. As cobranças já emitidas seguem pagáveis e são conciliadas até vencer. 409
    /// <c>recebimento.cobranca_automatica_ligada</c> enquanto a turma cobra por ele, e 409 <c>recebimento.loja_aberta</c> com
    /// item à venda na loja pública.
    /// </summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem desconecta.</param>
    Task<Result> Desconectar(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Liga ou desliga o cartão da turma, com a taxa repassada ou absorvida (Sprint 39, P2 e P7).</summary>
    /// <remarks>
    /// 404 <c>recebimento.provedor_nao_conectado</c> sem conexão; 409 <c>recebimento.reconectar_para_cartao</c> quando
    /// a conexão é anterior à sprint e não tem a chave pública.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem liga — Tesouraria ou Presidente.</param>
    /// <param name="dados">Ligado e a taxa repassada.</param>
    /// <returns>A conexão como fica.</returns>
    Task<Result<ProvedorDaTurma>> ConfigurarCartao(Guid formaturaId, Guid usuarioId, ConfiguracaoDoCartao dados, CancellationToken ct = default);

    /// <summary>Renova a autorização da turma do escopo antes de vencer. Para o worker.</summary>
    Task<Result> Renovar(CancellationToken ct = default);
}
