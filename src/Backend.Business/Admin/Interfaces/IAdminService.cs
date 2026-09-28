using Backend.Business.Abstractions;
using Backend.Business.Admin.Models;
using Backend.Business.Arquivos.Models;

namespace Backend.Business.Admin.Interfaces;

/// <summary>
/// Operações do painel administrativo.
/// </summary>
public interface IAdminService
{
    /// <summary>Apura os números do painel.</summary>
    Task<Result<ResumoAdmin>> ObterResumo(CancellationToken ct = default);

    /// <summary>Procura turmas e contas pelo mesmo termo.</summary>
    /// <remarks>Termo curto demais devolve as duas listas vazias — não é erro, é "continue digitando".</remarks>
    /// <param name="termo">Trecho digitado.</param>
    Task<Result<ResultadoDaBusca>> Buscar(string? termo, CancellationToken ct = default);

    /// <summary>A turma: situação, licença, membros e as contagens de parcela e adesão.</summary>
    /// <param name="formaturaId">Formatura.</param>
    Task<Result<TurmaNoSuporte>> ObterTurma(Guid formaturaId, CancellationToken ct = default);

    /// <summary>A conta: acesso, bloqueio e em que turmas a pessoa está.</summary>
    /// <param name="usuarioId">Conta.</param>
    Task<Result<UsuarioNoSuporte>> ObterUsuario(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Ativa a licença da turma à mão, para o caso em que o pagamento entrou e o webhook se perdeu.
    /// </summary>
    /// <remarks>
    /// É a única ação do painel que mexe em estado de turma, e a razão de ele existir: sem ela, a
    /// resposta a "paguei e a turma não ativou" é um <c>UPDATE</c> no banco de produção.
    /// <para>
    /// Grava evento de auditoria com a formatura no corpo — a comissão vê, na trilha dela, que quem
    /// ativou foi o suporte e não o presidente.
    /// </para>
    /// </remarks>
    /// <param name="formaturaId">Formatura.</param>
    /// <param name="autorId">Quem do suporte executou.</param>
    Task<Result<TurmaNoSuporte>> AtivarAssinatura(Guid formaturaId, Guid autorId, CancellationToken ct = default);

    /// <summary>
    /// Estorna um pagamento do plano e encerra a assinatura na hora (P7): a renovação é cancelada no provedor e a turma
    /// fica só para consulta.
    /// </summary>
    /// <param name="formaturaId">Formatura.</param>
    /// <param name="cobrancaId">Pagamento a estornar.</param>
    /// <param name="modo">Integral (até 7 dias do pagamento) ou proporcional ao que falta do ciclo.</param>
    /// <param name="autorId">Quem do suporte executou.</param>
    Task<Result<TurmaNoSuporte>> Estornar(Guid formaturaId, Guid cobrancaId, ModoDeEstorno modo, Guid autorId, CancellationToken ct = default);

    /// <summary>A planilha dos pagamentos do plano confirmados no mês, para a nota fiscal manual (P6).</summary>
    /// <param name="ano">Ano.</param>
    /// <param name="mes">Mês, de 1 a 12, no fuso de exibição.</param>
    Task<Result<ArquivoParaDownload>> ExportarPagamentos(int ano, int mes, CancellationToken ct = default);

    /// <summary>Reenvia o e-mail de confirmação da conta.</summary>
    /// <param name="usuarioId">Conta.</param>
    /// <param name="autorId">Quem do suporte executou.</param>
    Task<Result> ReenviarConfirmacao(Guid usuarioId, Guid autorId, CancellationToken ct = default);

    /// <summary>Envia o e-mail de redefinição de senha. O suporte <b>não</b> define senha para ninguém.</summary>
    /// <remarks>
    /// Quem escolhe a senha é o dono da conta. Uma senha definida pelo atendente é uma senha que
    /// duas pessoas conhecem, e o log de acesso deixa de dizer quem entrou.
    /// </remarks>
    /// <param name="usuarioId">Conta.</param>
    /// <param name="autorId">Quem do suporte executou.</param>
    Task<Result> DispararRedefinicaoDeSenha(Guid usuarioId, Guid autorId, CancellationToken ct = default);

    /// <summary>Limpa o bloqueio por tentativas de senha e zera o contador.</summary>
    /// <param name="usuarioId">Conta.</param>
    /// <param name="autorId">Quem do suporte executou.</param>
    Task<Result> Desbloquear(Guid usuarioId, Guid autorId, CancellationToken ct = default);
}
