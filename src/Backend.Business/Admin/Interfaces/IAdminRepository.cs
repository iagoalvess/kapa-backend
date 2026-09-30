using Backend.Business.Abstractions;
using Backend.Business.Admin.Models;

namespace Backend.Business.Admin.Interfaces;

/// <summary>
/// Consultas agregadas do painel administrativo.
/// </summary>
public interface IAdminRepository
{
    /// <summary>Contas, turmas, dinheiro e uso da plataforma no período (Sprint 44).</summary>
    /// <remarks>
    /// Atravessa todas as formaturas: quem chama é perfil de plataforma e não tem turma na sessão. O filtro global
    /// do <c>AppDbContext</c> não alcança <c>Formatura</c>, que é a raiz — mas alcança tudo o que pende dela, e por
    /// isso vínculo, assinatura e parcela são lidos ignorando-o.
    /// </remarks>
    /// <param name="de">Primeiro dia do período, no fuso de exibição.</param>
    /// <param name="ate">Último dia do período, inclusive.</param>
    /// <param name="agoraUtc">Momento da apuração: o corte do "a vencer".</param>
    Task<AnalyticsDaPlataforma> ObterAnalyticsDeTodasAsFormaturas(DateOnly de, DateOnly ate, DateTime agoraUtc, CancellationToken ct = default);

    /// <summary>Cadastros, turmas novas e recebido do Kapa, mês a mês no fuso de exibição.</summary>
    /// <param name="primeiroMes">Primeiro dia do primeiro mês, no fuso de exibição.</param>
    /// <param name="meses">Quantos meses, do primeiro em diante.</param>
    Task<IReadOnlyList<MesDaPlataforma>> ObterSerieMensalDeTodasAsFormaturas(DateOnly primeiroMes, int meses, CancellationToken ct = default);

    /// <summary>Uma página das turmas, com a licença de cada uma.</summary>
    /// <param name="paginacao">Página, tamanho e ordenação — <c>nome</c> ou <c>criada_em</c>; o padrão é a mais nova.</param>
    /// <param name="filtro">Termo e licença.</param>
    Task<PaginaDe<TurmaNoPainel>> ListarTurmasDeTodasAsFormaturas(
        PaginacaoRequest paginacao,
        FiltroDeTurmasNoPainel filtro,
        CancellationToken ct = default
    );

    /// <summary>Uma página das contas, com em quantas turmas cada uma está.</summary>
    /// <param name="paginacao">Página, tamanho e ordenação — <c>nome</c>, <c>email</c> ou <c>criado_em</c>; o padrão é o nome.</param>
    /// <param name="filtro">Termo e situação.</param>
    /// <param name="agoraUtc">O corte do bloqueio por tentativas.</param>
    Task<PaginaDe<ContaNoPainel>> ListarContasDeTodasAsFormaturas(
        PaginacaoRequest paginacao,
        FiltroDeContasNoPainel filtro,
        DateTime agoraUtc,
        CancellationToken ct = default
    );

    /// <summary>Uma página dos membros da turma, ativos primeiro. O CPF sai mascarado.</summary>
    /// <param name="formaturaId">Formatura.</param>
    /// <param name="paginacao">Página e tamanho.</param>
    Task<PaginaDe<MembroNoSuporte>> ListarMembrosDeTodasAsFormaturas(Guid formaturaId, PaginacaoRequest paginacao, CancellationToken ct = default);

    /// <summary>A turma inteira como o suporte a vê, ou nulo se não existir.</summary>
    /// <param name="formaturaId">Formatura.</param>
    Task<TurmaNoSuporte?> ObterTurmaDeTodasAsFormaturas(Guid formaturaId, CancellationToken ct = default);

    /// <summary>A conta inteira como o suporte a vê, ou nulo se não existir.</summary>
    /// <param name="usuarioId">Conta.</param>
    Task<UsuarioNoSuporte?> ObterUsuarioDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Os pagamentos do plano confirmados no período, de todas as turmas, com o Presidente como tomador — a lista da
    /// nota fiscal manual (P6). O CPF sai inteiro.
    /// </summary>
    /// <param name="inicio">Início do período, em UTC, inclusive.</param>
    /// <param name="fim">Fim do período, em UTC, exclusive.</param>
    Task<IReadOnlyList<PagamentoParaNota>> ListarPagamentosParaNotaDeTodasAsFormaturas(DateTime inicio, DateTime fim, CancellationToken ct = default);
}
