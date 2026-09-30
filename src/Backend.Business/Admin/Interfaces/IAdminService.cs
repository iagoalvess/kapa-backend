using Backend.Business.Abstractions;
using Backend.Business.Admin.Models;
using Backend.Business.Arquivos.Models;

namespace Backend.Business.Admin.Interfaces;

/// <summary>
/// Operações do painel administrativo.
/// </summary>
public interface IAdminService
{
    /// <summary>Contas, turmas, dinheiro e uso da plataforma no período.</summary>
    /// <remarks>Sem período, os últimos 30 dias até hoje.</remarks>
    /// <param name="de">Primeiro dia, no fuso de exibição.</param>
    /// <param name="ate">Último dia, inclusive.</param>
    Task<Result<AnalyticsDaPlataforma>> ObterAnalytics(DateOnly? de, DateOnly? ate, CancellationToken ct = default);

    /// <summary>A série mensal do painel, terminando no mês corrente.</summary>
    /// <param name="meses">Quantos meses, de 1 a 24.</param>
    Task<Result<IReadOnlyList<MesDaPlataforma>>> ObterSerieMensal(int meses, CancellationToken ct = default);

    /// <summary>Uma página das turmas, com a licença de cada uma.</summary>
    /// <param name="paginacao">Página, tamanho e ordenação.</param>
    /// <param name="filtro">Termo e licença.</param>
    Task<Result<PaginaDe<TurmaNoPainel>>> ListarTurmas(PaginacaoRequest paginacao, FiltroDeTurmasNoPainel filtro, CancellationToken ct = default);

    /// <summary>Uma página das contas.</summary>
    /// <param name="paginacao">Página, tamanho e ordenação.</param>
    /// <param name="filtro">Termo e situação.</param>
    Task<Result<PaginaDe<ContaNoPainel>>> ListarContas(PaginacaoRequest paginacao, FiltroDeContasNoPainel filtro, CancellationToken ct = default);

    /// <summary>Uma página dos membros da turma, ativos primeiro. O CPF sai mascarado.</summary>
    /// <param name="formaturaId">Formatura.</param>
    /// <param name="paginacao">Página e tamanho.</param>
    Task<Result<PaginaDe<MembroNoSuporte>>> ListarMembros(Guid formaturaId, PaginacaoRequest paginacao, CancellationToken ct = default);

    /// <summary>A turma: situação, licença e as contagens de membro, parcela e adesão.</summary>
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
