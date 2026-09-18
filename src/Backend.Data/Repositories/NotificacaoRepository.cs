using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Emails.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Régua, histórico e preferências da formatura selecionada.
/// </summary>
/// <remarks>
/// A seleção de parcelas parte de <c>ParcelaRepository.Devedores</c>, e não de uma consulta própria:
/// a régua precisa chamar a pessoa pelo mesmo nome que a tela de Parcelas e a conferência usam, e
/// essa regra mora lá.
/// <para>
/// O vínculo ativo é exigido <b>aqui</b>, e não em <c>Devedores</c>: a tela de Parcelas continua
/// mostrando o que quem saiu deixou em atraso — sumir esconderia o histórico de quem pagou parte —,
/// mas a régua para no mesmo instante do desligamento (decisão 4 da Sprint 15). Sem esta linha, o
/// job da madrugada seguinte manda cobrança para quem saiu ontem, que é a forma mais rápida de a
/// comissão perder a confiança na ferramenta.
/// </para>
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class NotificacaoRepository(AppDbContext db) : INotificacaoRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// <c>Formatura</c> é a raiz do isolamento e fica fora do filtro global — não há
    /// <c>IgnoreQueryFilters</c> aqui. O sufixo está no nome porque o método atravessa turmas, que é
    /// o que o leitor precisa saber.
    /// </remarks>
    public async Task<IReadOnlyList<FormaturaParaRegua>> ListarFormaturasAtivasDeTodasAsFormaturas(CancellationToken ct = default) =>
        await db
            .Formaturas.AsNoTracking()
            .Where(f => f.Status == StatusDaFormatura.Ativa)
            .OrderBy(f => f.Id)
            .Select(f => new FormaturaParaRegua(f.Id, f.Nome))
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegraResumo>> ListarRegras(CancellationToken ct = default) =>
        await db
            .RegrasDeNotificacao.AsNoTracking()
            .OrderBy(r => r.Gatilho)
            .ThenBy(r => r.DiasDeDeslocamento)
            .Select(r => new RegraResumo(r.Id, r.Gatilho, r.DiasDeDeslocamento, r.Assunto, r.Template, r.Ativa, r.AvisarTesouraria))
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegraDeNotificacao>> ListarRegrasParaEdicao(CancellationToken ct = default) =>
        await db.RegrasDeNotificacao.ToListAsync(ct);

    /// <inheritdoc />
    public Task<RegraDeNotificacao?> ObterRegraParaEdicao(Guid regraId, CancellationToken ct = default) =>
        db.RegrasDeNotificacao.FirstOrDefaultAsync(r => r.Id == regraId, ct);

    /// <inheritdoc />
    public Task AdicionarRegras(IReadOnlyList<RegraDeNotificacao> regras, CancellationToken ct = default) =>
        db.RegrasDeNotificacao.AddRangeAsync(regras, ct);

    /// <inheritdoc />
    public void RemoverRegras(IReadOnlyList<RegraDeNotificacao> regras) => db.RegrasDeNotificacao.RemoveRange(regras);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParcelaParaCobranca>> ListarParaCobranca(DateOnly vencimento, CancellationToken ct = default) =>
        await Projetar(Cobraveis().Where(linha => linha.Parcela.Vencimento == vencimento)).ToListAsync(ct);

    /// <inheritdoc />
    public Task<ParcelaParaCobranca?> ObterParaCobranca(Guid parcelaId, CancellationToken ct = default) =>
        Projetar(Cobraveis().Where(linha => linha.Parcela.Id == parcelaId)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// <c>CriadoEm</c> é UTC; o corte é o fim do dia em Brasília, para "parado há três dias" contar
    /// dias de calendário daqui, e não janelas de 24 horas de UTC.
    /// </remarks>
    public Task<int> ContarInformesPendentesAte(DateOnly ate, CancellationToken ct = default) =>
        db.Informes.AsNoTracking().CountAsync(i => i.Status == StatusDoInforme.Pendente && i.CriadoEm < DataUtils.FimDoDiaEmUtc(ate), ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChaveDeEnvio>> ListarChavesDoDia(DateOnly dia, CancellationToken ct = default) =>
        await db
            .NotificacoesEnviadas.AsNoTracking()
            .Where(n => n.DataDeReferencia == dia)
            .Select(n => new ChaveDeEnvio(n.RegraId, n.ParcelaId))
            .Distinct()
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListarDestinatariosInvalidos(CancellationToken ct = default) =>
        await db
            .NotificacoesEnviadas.AsNoTracking()
            .Where(n => n.Status == StatusDaNotificacao.Falhou)
            .Select(n => n.Destinatario)
            .Distinct()
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task AdicionarEnvios(IReadOnlyList<NotificacaoEnviada> envios, CancellationToken ct = default) =>
        db.NotificacoesEnviadas.AddRangeAsync(envios, ct);

    /// <inheritdoc />
    /// <remarks>
    /// A notificação volta <b>rastreada</b>: quem chama muda o status dela e salva. Por isso não há
    /// <c>AsNoTracking</c> em lugar nenhum desta consulta — nem no lado do e-mail: o operador é da
    /// consulta inteira, e não da fonte em que aparece. Pô-lo só no <c>join</c> devolveria a
    /// notificação solta, e o "Entregue" sumiria sem erro nenhum.
    /// </remarks>
    public async Task<IReadOnlyList<EntregaAConferir>> ListarEntregasAConferir(int limite, CancellationToken ct = default) =>
        await (
            from notificacao in db.NotificacoesEnviadas
            join email in db.EmailsFila on notificacao.EmailNaFilaId equals email.Id
            where
                notificacao.Status == StatusDaNotificacao.Enfileirada
                && email.Status != EEmailStatus.Pendente
                && email.Status != EEmailStatus.Enviando
            orderby notificacao.Id
            select new EntregaAConferir(notificacao, email.Status, email.UltimoErro)
        )
            .Take(limite)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<PaginaDe<NotificacaoNoHistorico>> ListarHistorico(
        PaginacaoRequest paginacao,
        FiltroDeNotificacoes filtro,
        CancellationToken ct = default
    )
    {
        var consulta = Filtrar(Linhas(), filtro);

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<NotificacaoNoHistorico>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "destinatario" => consulta.Por(linha => linha.Notificacao.Destinatario, desc),
            "data" => consulta.Por(linha => linha.Notificacao.DataDeReferencia, desc),
            _ => consulta.OrderByDescending(linha => linha.Notificacao.CriadoEm),
        };

        var itens = await Projetar(ordenada.ThenByDescending(linha => linha.Notificacao.Id).Skip(paginacao.Pular).Take(paginacao.Tamanho))
            .ToListAsync(ct);

        return new PaginaDe<NotificacaoNoHistorico>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PreferenciaDeNotificacao>> ListarPreferenciasParaEdicao(Guid vinculoId, CancellationToken ct = default) =>
        await db.PreferenciasDeNotificacao.Where(p => p.VinculoId == vinculoId).ToListAsync(ct);

    /// <inheritdoc />
    public Task AdicionarPreferencias(IReadOnlyList<PreferenciaDeNotificacao> preferencias, CancellationToken ct = default) =>
        db.PreferenciasDeNotificacao.AddRangeAsync(preferencias, ct);

    /// <summary>
    /// As parcelas que a régua pode cobrar: em aberto, com dono que tem e-mail e sem informe pendente.
    /// </summary>
    /// <remarks>
    /// O informe pendente é descontado <b>na consulta</b> (decisão 1): deixar isso para um <c>if</c>
    /// depois é confiar em alguém lembrar dele no próximo caminho que selecionar parcela para cobrar.
    /// </remarks>
    private IQueryable<LinhaCobravel> Cobraveis() =>
        from devedor in ParcelaRepository.Devedores(db)
        join item in db.ItensDeCobranca.AsNoTracking() on devedor.Parcela.ItemDeCobrancaId equals item.Id
        where
            devedor.Parcela.Status == StatusDaParcela.Aberta
            && devedor.Email != null
            && db.Vinculos.Any(vinculo => vinculo.Id == devedor.Parcela.VinculoId && vinculo.Ativo)
            && !db.Informes.Any(informe => informe.ParcelaId == devedor.Parcela.Id && informe.Status == StatusDoInforme.Pendente)
        select new LinhaCobravel
        {
            Parcela = devedor.Parcela,
            Nome = devedor.Nome,
            Email = devedor.Email!,
            Descricao = item.Descricao,
        };

    /// <summary>
    /// A linha no modelo de leitura — o último passo da consulta.
    /// </summary>
    /// <remarks>
    /// Sempre depois do <c>Where</c>: filtrar sobre um <c>record</c> já projetado não traduz, porque o
    /// EF não enxerga através do construtor posicional dele.
    /// </remarks>
    /// <param name="linhas">Linhas já filtradas.</param>
    private static IQueryable<ParcelaParaCobranca> Projetar(IQueryable<LinhaCobravel> linhas) =>
        linhas.Select(linha => new ParcelaParaCobranca(
            linha.Parcela.Id,
            linha.Parcela.VinculoId,
            linha.Nome,
            linha.Email,
            linha.Parcela.Vencimento,
            linha.Parcela.ValorOriginalEmCentavos,
            linha.Descricao
        ));

    /// <summary>A notificação com o degrau de origem e o nome de quem recebeu.</summary>
    /// <remarks>
    /// <c>LEFT JOIN</c> no vínculo e no usuário: o resumo à tesouraria não tem vínculo, e o histórico
    /// não pode sumir quando alguém sai da turma.
    /// </remarks>
    private IQueryable<LinhaDeNotificacao> Linhas() =>
        from notificacao in db.NotificacoesEnviadas.AsNoTracking()
        join regra in db.RegrasDeNotificacao.AsNoTracking() on notificacao.RegraId equals regra.Id
        join vinculo in db.Vinculos.AsNoTracking() on notificacao.VinculoId equals vinculo.Id into vinculos
        from vinculo in vinculos.DefaultIfEmpty()
        join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id into usuarios
        from usuario in usuarios.DefaultIfEmpty()
        select new LinhaDeNotificacao
        {
            Notificacao = notificacao,
            Regra = regra,
            Nome = usuario == null ? null : usuario.Nome,
        };

    private static IQueryable<LinhaDeNotificacao> Filtrar(IQueryable<LinhaDeNotificacao> consulta, FiltroDeNotificacoes filtro)
    {
        if (filtro.Status is { } status)
            consulta = consulta.Where(linha => linha.Notificacao.Status == status);

        if (filtro.De is { } de)
            consulta = consulta.Where(linha => linha.Notificacao.DataDeReferencia >= de);

        if (filtro.Ate is { } ate)
            consulta = consulta.Where(linha => linha.Notificacao.DataDeReferencia <= ate);

        if (string.IsNullOrWhiteSpace(filtro.Busca))
            return consulta;

        var termo = Busca.Padrao(filtro.Busca);

        return consulta.Where(linha =>
            EF.Functions.ILike(EF.Functions.Unaccent(linha.Notificacao.Destinatario), termo)
            || (linha.Nome != null && EF.Functions.ILike(EF.Functions.Unaccent(linha.Nome), termo))
        );
    }

    private static IQueryable<NotificacaoNoHistorico> Projetar(IQueryable<LinhaDeNotificacao> linhas) =>
        linhas.Select(linha => new NotificacaoNoHistorico(
            linha.Notificacao.Id,
            linha.Notificacao.Destinatario,
            linha.Nome,
            linha.Notificacao.Assunto,
            linha.Notificacao.Status,
            linha.Notificacao.Erro,
            linha.Notificacao.DataDeReferencia,
            linha.Notificacao.CriadoEm,
            linha.Regra.Gatilho,
            linha.Regra.DiasDeDeslocamento
        ));
}

/// <summary>Uma parcela que a régua pode cobrar, antes de virar modelo de leitura.</summary>
internal sealed class LinhaCobravel
{
    /// <summary>A parcela.</summary>
    public required Parcela Parcela { get; init; }

    /// <summary>Nome civil, ou o da conta.</summary>
    public required string Nome { get; init; }

    /// <summary>E-mail da conta.</summary>
    public required string Email { get; init; }

    /// <summary>Descrição do item de origem, se houver.</summary>
    public string? Descricao { get; init; }
}

/// <summary>Uma notificação com o degrau de origem e quem recebeu — a forma intermediária do histórico.</summary>
internal sealed class LinhaDeNotificacao
{
    /// <summary>O registro do envio.</summary>
    public required NotificacaoEnviada Notificacao { get; init; }

    /// <summary>O degrau que disparou.</summary>
    public required RegraDeNotificacao Regra { get; init; }

    /// <summary>Nome da conta de quem recebeu; nulo no resumo à tesouraria.</summary>
    public string? Nome { get; init; }
}
