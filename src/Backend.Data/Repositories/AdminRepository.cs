using Backend.Business.Abstractions;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Admin.Models;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Texto;
using Backend.Business.Formaturas.Models;
using Backend.Business.Usuarios.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Consultas agregadas do painel administrativo e as leituras do painel de suporte.
/// </summary>
/// <remarks>
/// <b>Tudo aqui atravessa formaturas.</b> Quem chama é o perfil <c>Administrador</c> da plataforma,
/// que não tem turma na sessão — então o filtro global do <c>AppDbContext</c> devolveria zero linha
/// em toda consulta que passe por vínculo, parcela, adesão ou cadastro. É a exceção documentada de
/// <c>IgnoreQueryFilters</c>, e é por isso que os métodos da interface dizem "de todas".
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class AdminRepository(AppDbContext db) : IAdminRepository
{
    /// <summary>Quantos e-mails de marketing a conta no suporte mostra — os últimos respondem "por que recebi isto?".</summary>
    private const int EnviosDeMarketingNoSuporte = 10;

    /// <summary>A janela do "a vencer" do dinheiro do Kapa.</summary>
    private const int DiasDoAVencer = 30;

    /// <inheritdoc />
    /// <remarks>
    /// Sete idas ao banco, cada uma agregando uma tabela sozinha — o <c>GROUP BY</c> do EF não sobrevive a subconsulta
    /// na projeção de origem, e juntar tudo numa instrução só trocaria sete leituras simples por uma que ninguém
    /// consegue conferir contra o SQL de conferência (critério 4).
    /// <para>
    /// As turmas vêm uma linha por turma, e a distribuição, a média e a mediana saem na memória: a mediana não tem
    /// tradução no EF, e são milhares de linhas de quatro colunas. <c>ponytail:</c> passar a agregar no banco quando
    /// o critério 6 (500 ms em <c>kapa_carga</c>) falhar.
    /// </para>
    /// <para>
    /// O ranking de uso é SQL cru: <c>split_part</c> separa o recurso de <c>recurso.acao</c>, e contar turmas e
    /// pessoas distintas por recurso é um <c>count(DISTINCT)</c> — o índice <c>(ocorrido_em, nome)</c> cobre o recorte
    /// do período.
    /// </para>
    /// </remarks>
    public async Task<AnalyticsDaPlataforma> ObterAnalyticsDeTodasAsFormaturas(
        DateOnly de,
        DateOnly ate,
        DateTime agoraUtc,
        CancellationToken ct = default
    )
    {
        var inicio = DataUtils.InicioDoDiaEmUtc(de);
        var fim = DataUtils.FimDoDiaEmUtc(ate);

        var contas = await db
            .Users.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(grupo => new
            {
                Total = grupo.LongCount(),
                NoPeriodo = grupo.LongCount(u => u.CriadoEm >= inicio && u.CriadoEm < fim),
                Confirmadas = grupo.LongCount(u => u.EmailConfirmed),
            })
            .SingleOrDefaultAsync(ct);

        var semTurma = await db
            .Users.AsNoTracking()
            .LongCountAsync(u => !db.Vinculos.IgnoreQueryFilters().Any(v => v.UsuarioId == u.Id && v.Ativo && v.DesligadoEm == null), ct);

        var turmas = await TurmasComLicenca()
            .Select(t => new
            {
                t.Status,
                t.PlanoPago,
                t.Membros,
                t.CriadaEm,
            })
            .ToListAsync(ct);

        var renovaveis = await (
            from a in db.Assinaturas.AsNoTracking().IgnoreQueryFilters()
            join p in db.Planos.AsNoTracking() on a.PlanoId equals p.Id
            where a.Status == StatusDaAssinatura.Ativa && p.Codigo != Plano.CodigoGratuito
            select new
            {
                a.VigenteAte,
                p.PrecoEmCentavos,
                p.Ciclo,
            }
        ).ToListAsync(ct);

        var cobrancas = await db
            .CobrancasDaAssinatura.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(grupo => new
            {
                Recebido = grupo.Sum(c =>
                    (c.Situacao == SituacaoDaCobrancaDoPlano.Paga || c.Situacao == SituacaoDaCobrancaDoPlano.Estornada)
                    && c.PagaEm >= inicio
                    && c.PagaEm < fim
                        ? c.ValorEmCentavos
                        : 0L
                ),
                Estornado = grupo.Sum(c => c.EstornadaEm >= inicio && c.EstornadaEm < fim ? c.ValorEstornadoEmCentavos ?? 0L : 0L),
            })
            .SingleOrDefaultAsync(ct);

        var parcelas = await db
            .Parcelas.AsNoTracking()
            .IgnoreQueryFilters()
            .GroupBy(_ => 1)
            .Select(grupo => new
            {
                Pagas = grupo.LongCount(p => p.Status == StatusDaParcela.Paga && p.PagoEm >= de && p.PagoEm <= ate),
                Pago = grupo.Sum(p =>
                    p.Status == StatusDaParcela.Paga && p.PagoEm >= de && p.PagoEm <= ate ? p.ValorPagoEmCentavos ?? p.ValorOriginalEmCentavos : 0L
                ),
                Abertas = grupo.LongCount(p => p.Status == StatusDaParcela.Aberta),
                Aberto = grupo.Sum(p => p.Status == StatusDaParcela.Aberta ? p.ValorOriginalEmCentavos : 0L),
            })
            .SingleOrDefaultAsync(ct);

        var uso = await db
            .Database.SqlQuery<UsoDoRecurso>(
                $"""
                SELECT split_part(e.nome, '.', 1) AS recurso,
                       count(*) AS eventos,
                       count(DISTINCT e.formatura_id) AS turmas,
                       count(DISTINCT e.usuario_id) AS usuarios
                FROM eventos e
                WHERE e.ocorrido_em >= {inicio} AND e.ocorrido_em < {fim}
                GROUP BY 1
                ORDER BY 2 DESC, 1
                """
            )
            .ToListAsync(ct);

        var membros = turmas.Where(t => t.Status != StatusDaFormatura.Descartada).Select(t => t.Membros).Order().ToList();
        var vencendoAte = agoraUtc.AddDays(DiasDoAVencer);

        return new AnalyticsDaPlataforma(
            de,
            ate,
            new ContasDaPlataforma(contas?.Total ?? 0, contas?.NoPeriodo ?? 0, contas?.Confirmadas ?? 0, semTurma),
            new TurmasDaPlataforma(
                turmas.Count,
                turmas.LongCount(t => t.CriadaEm >= inicio && t.CriadaEm < fim),
                turmas.LongCount(t => t.PlanoPago != null),
                [
                    .. turmas
                        .GroupBy(t => NomeDaLicenca(t.Status, t.PlanoPago))
                        .Select(grupo => new TurmasNaLicenca(grupo.Key, grupo.LongCount()))
                        .OrderByDescending(licenca => licenca.Turmas)
                        .ThenBy(licenca => licenca.Licenca),
                ],
                membros.Count == 0 ? 0 : membros.Average(),
                Mediana(membros)
            ),
            new DinheiroDoKapa(
                renovaveis.Count,
                renovaveis.Sum(r => r.Ciclo == CicloDeCobranca.Anual ? (long)Math.Round(r.PrecoEmCentavos / 12.0) : r.PrecoEmCentavos),
                cobrancas?.Recebido ?? 0,
                renovaveis.Where(r => r.VigenteAte >= agoraUtc && r.VigenteAte < vencendoAte).Sum(r => r.PrecoEmCentavos),
                cobrancas?.Estornado ?? 0
            ),
            new DinheiroDasTurmas(parcelas?.Pagas ?? 0, parcelas?.Pago ?? 0, parcelas?.Abertas ?? 0, parcelas?.Aberto ?? 0),
            uso
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Um mês por linha, com as fronteiras no fuso de exibição: a conta criada às 22h do dia 31 em Brasília é
    /// daquele mês, ainda que em UTC já seja o seguinte. A fronteira é convertida, e não a coluna — assim o recorte
    /// continua podendo usar índice.
    /// </remarks>
    public async Task<IReadOnlyList<MesDaPlataforma>> ObterSerieMensalDeTodasAsFormaturas(
        DateOnly primeiroMes,
        int meses,
        CancellationToken ct = default
    )
    {
        var ultimoMes = primeiroMes.AddMonths(meses - 1);
        var fuso = DataUtils.FusoIana;
        var paga = nameof(SituacaoDaCobrancaDoPlano.Paga);
        var estornada = nameof(SituacaoDaCobrancaDoPlano.Estornada);

        return await db
            .Database.SqlQuery<MesDaPlataforma>(
                $"""
                SELECT extract(year FROM m.mes)::int AS ano,
                       extract(month FROM m.mes)::int AS mes,
                       (SELECT count(*) FROM usuarios u
                         WHERE u.criado_em >= (m.mes AT TIME ZONE {fuso})
                           AND u.criado_em < ((m.mes + interval '1 month') AT TIME ZONE {fuso})) AS cadastros,
                       (SELECT count(*) FROM formaturas f
                         WHERE f.criado_em >= (m.mes AT TIME ZONE {fuso})
                           AND f.criado_em < ((m.mes + interval '1 month') AT TIME ZONE {fuso})) AS turmas_novas,
                       (SELECT coalesce(sum(c.valor_em_centavos), 0)::bigint FROM cobrancas_da_assinatura c
                         WHERE c.situacao IN ({paga}, {estornada})
                           AND c.paga_em >= (m.mes AT TIME ZONE {fuso})
                           AND c.paga_em < ((m.mes + interval '1 month') AT TIME ZONE {fuso})) AS recebido_em_centavos
                FROM generate_series({primeiroMes}::timestamp, {ultimoMes}::timestamp, interval '1 month') AS m(mes)
                ORDER BY m.mes
                """
            )
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A licença é filtrada no banco: o status para as turmas paradas, o plano vigente para as ativas. O nome da
    /// licença é montado na memória, sobre a página já cortada.
    /// </remarks>
    public async Task<PaginaDe<TurmaNoPainel>> ListarTurmasDeTodasAsFormaturas(
        PaginacaoRequest paginacao,
        FiltroDeTurmasNoPainel filtro,
        CancellationToken ct = default
    )
    {
        var consulta = TurmasComLicenca();

        if (!string.IsNullOrWhiteSpace(filtro.Termo))
        {
            var padrao = Busca.Padrao(filtro.Termo.Trim());
            consulta = consulta.Where(t =>
                EF.Functions.ILike(EF.Functions.Unaccent(t.Nome), padrao)
                || EF.Functions.ILike(EF.Functions.Unaccent(t.Instituicao), padrao)
                || EF.Functions.ILike(EF.Functions.Unaccent(t.Curso), padrao)
            );
        }

        consulta = filtro.Licenca switch
        {
            null or "" => consulta,
            LicencaDaTurma.Gratuita => consulta.Where(t => t.Status == StatusDaFormatura.Ativa && t.PlanoPago == null),
            var licenca when Enum.GetNames<StatusDaFormatura>().Contains(licenca) => consulta.Where(t =>
                t.Status == Enum.Parse<StatusDaFormatura>(licenca)
            ),
            var plano => consulta.Where(t => t.Status == StatusDaFormatura.Ativa && t.PlanoPago == plano),
        };

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<TurmaNoPainel>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "nome" => consulta.Por(t => t.Nome, desc),
            "criada_em" => consulta.Por(t => t.CriadaEm, desc),
            _ => consulta.OrderByDescending(t => t.CriadaEm),
        };

        var linhas = await ordenada.ThenBy(t => t.Id).Skip(paginacao.Pular).Take(paginacao.Tamanho).ToListAsync(ct);

        return new PaginaDe<TurmaNoPainel>(
            [
                .. linhas.Select(t => new TurmaNoPainel(
                    t.Id,
                    t.Nome,
                    t.Instituicao,
                    t.Curso,
                    t.Status.ToString(),
                    NomeDaLicenca(t.Status, t.PlanoPago),
                    t.Membros,
                    t.CriadaEm
                )),
            ],
            paginacao.Pagina,
            paginacao.Tamanho,
            total
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// O e-mail é comparado sem <c>unaccent</c>: endereço não tem acento, e passar a coluna pela função tiraria dela
    /// qualquer chance de índice.
    /// </remarks>
    public async Task<PaginaDe<ContaNoPainel>> ListarContasDeTodasAsFormaturas(
        PaginacaoRequest paginacao,
        FiltroDeContasNoPainel filtro,
        DateTime agoraUtc,
        CancellationToken ct = default
    )
    {
        var consulta = db.Users.AsNoTracking();
        var agora = new DateTimeOffset(agoraUtc);

        if (!string.IsNullOrWhiteSpace(filtro.Termo))
        {
            var padrao = Busca.Padrao(filtro.Termo.Trim());
            consulta = consulta.Where(u => EF.Functions.ILike(EF.Functions.Unaccent(u.Nome), padrao) || EF.Functions.ILike(u.Email!, padrao));
        }

        consulta = filtro.Situacao switch
        {
            SituacaoDaConta.Confirmada => consulta.Where(u => u.Ativo && u.EmailConfirmed),
            SituacaoDaConta.Bloqueada => consulta.Where(u => u.LockoutEnd > agora),
            SituacaoDaConta.Desativada => consulta.Where(u => !u.Ativo),
            _ => consulta,
        };

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<ContaNoPainel>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "email" => consulta.Por(u => u.Email, desc),
            "criado_em" => consulta.Por(u => u.CriadoEm, desc),
            "nome" => consulta.Por(u => u.Nome, desc),
            _ => consulta.OrderBy(u => u.Nome),
        };

        var itens = await ordenada
            .ThenBy(u => u.Id)
            .Skip(paginacao.Pular)
            .Take(paginacao.Tamanho)
            .Select(u => new ContaNoPainel(
                u.Id,
                u.Nome,
                u.Email!,
                u.Ativo,
                u.EmailConfirmed,
                u.LockoutEnd > agora ? u.LockoutEnd!.Value.UtcDateTime : (DateTime?)null,
                db.Vinculos.IgnoreQueryFilters().Count(v => v.UsuarioId == u.Id && v.Ativo && v.DesligadoEm == null),
                u.CriadoEm
            ))
            .ToListAsync(ct);

        return new PaginaDe<ContaNoPainel>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O CPF sai do cadastro <b>já mascarado</b>. A coluna é cifrada, então o valor é decifrado ao materializar e
    /// mascarado aqui, na borda de leitura — o painel nunca vê o número inteiro.
    /// </remarks>
    public async Task<PaginaDe<MembroNoSuporte>> ListarMembrosDeTodasAsFormaturas(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        CancellationToken ct = default
    )
    {
        var total = await db.Vinculos.IgnoreQueryFilters().LongCountAsync(v => v.FormaturaId == formaturaId, ct);

        if (total == 0)
            return PaginaDe<MembroNoSuporte>.Vazia(paginacao);

        var membros = await (
            from v in db.Vinculos.AsNoTracking().IgnoreQueryFilters()
            join u in db.Users.AsNoTracking() on v.UsuarioId equals u.Id
            where v.FormaturaId == formaturaId
            join p in db.PerfisDeFormandos.AsNoTracking().IgnoreQueryFilters() on v.Id equals p.VinculoId into perfis
            orderby v.Ativo descending, u.Nome, v.Id
            select new
            {
                v.UsuarioId,
                u.Nome,
                Email = u.Email!,
                v.Papel,
                v.Ativo,
                v.DesligadoEm,
                Cpf = perfis.Select(p => p.Cpf).FirstOrDefault(),
            }
        )
            .Skip(paginacao.Pular)
            .Take(paginacao.Tamanho)
            .ToListAsync(ct);

        return new PaginaDe<MembroNoSuporte>(
            [
                .. membros.Select(m => new MembroNoSuporte(
                    m.UsuarioId,
                    m.Nome,
                    m.Email,
                    m.Papel,
                    m.Ativo,
                    m.DesligadoEm,
                    FormatosBrasileiros.MascararCpf(m.Cpf)
                )),
            ],
            paginacao.Pagina,
            paginacao.Tamanho,
            total
        );
    }

    /// <summary>
    /// Toda turma com o plano pago em vigor e os membros ativos — a base da lista e do analytics.
    /// </summary>
    /// <remarks>
    /// Projeta numa classe, e não num <c>record</c>: o EF não traduz <c>Where</c> nem <c>OrderBy</c> sobre membro de
    /// <c>record</c> já projetado, e é sobre esta projeção que a lista filtra e ordena. O plano vigente é a leitura
    /// de <c>ObterPlanoVigenteDeTodasAsFormaturas</c> (P4): a assinatura mais recente <c>Ativa</c> ou <c>Cancelada</c>.
    /// </remarks>
    private IQueryable<LinhaDaTurma> TurmasComLicenca() =>
        from f in db.Formaturas.AsNoTracking()
        select new LinhaDaTurma
        {
            Id = f.Id,
            Nome = f.Nome,
            Instituicao = f.Instituicao,
            Curso = f.Curso,
            Status = f.Status,
            CriadaEm = f.CriadoEm,
            PlanoPago = (
                from a in db.Assinaturas.IgnoreQueryFilters()
                join p in db.Planos on a.PlanoId equals p.Id
                where
                    a.FormaturaId == f.Id
                    && (a.Status == StatusDaAssinatura.Ativa || a.Status == StatusDaAssinatura.Cancelada)
                    && p.Codigo != Plano.CodigoGratuito
                orderby a.CriadoEm descending, a.Id descending
                select p.Nome
            ).FirstOrDefault(),
            Membros = db.Vinculos.IgnoreQueryFilters().Count(v => v.FormaturaId == f.Id && v.Ativo && v.DesligadoEm == null),
        };

    /// <summary>A licença da turma (<see cref="LicencaDaTurma"/>): o status, fora de <c>Ativa</c>; o plano, dentro.</summary>
    /// <param name="status">Situação da formatura.</param>
    /// <param name="planoPago">Nome do plano pago em vigor, ou nulo.</param>
    private static string NomeDaLicenca(StatusDaFormatura status, string? planoPago) =>
        status != StatusDaFormatura.Ativa ? status.ToString() : planoPago ?? LicencaDaTurma.Gratuita;

    /// <summary>A mediana de uma lista já ordenada; zero se vazia.</summary>
    /// <param name="ordenados">Valores em ordem crescente.</param>
    private static double Mediana(List<int> ordenados) =>
        ordenados.Count == 0 ? 0
        : ordenados.Count % 2 == 1 ? ordenados[ordenados.Count / 2]
        : (ordenados[ordenados.Count / 2 - 1] + ordenados[ordenados.Count / 2]) / 2.0;

    /// <summary>Uma turma com a licença e os membros, antes de virar linha da lista.</summary>
    private sealed class LinhaDaTurma
    {
        public Guid Id { get; init; }

        public string Nome { get; init; } = string.Empty;

        public string Instituicao { get; init; } = string.Empty;

        public string Curso { get; init; } = string.Empty;

        public StatusDaFormatura Status { get; init; }

        public DateTime CriadaEm { get; init; }

        public string? PlanoPago { get; init; }

        public int Membros { get; init; }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A turma com as contagens, o plano e os pagamentos. Os membros não vêm aqui: são paginados à parte
    /// (<see cref="ListarMembrosDeTodasAsFormaturas"/>) — uma turma de 150 pessoas não precisa descer inteira
    /// para a tela mostrar vinte.
    /// </remarks>
    public async Task<TurmaNoSuporte?> ObterTurmaDeTodasAsFormaturas(Guid formaturaId, CancellationToken ct = default)
    {
        var turma = await (
            from f in db.Formaturas.AsNoTracking()
            where f.Id == formaturaId
            join a in db.Assinaturas.AsNoTracking().IgnoreQueryFilters() on f.Id equals a.FormaturaId into assinaturas
            select new
            {
                f.Id,
                f.Nome,
                f.Instituicao,
                f.Curso,
                f.Ano,
                f.Semestre,
                f.Status,
                f.CriadoEm,
                f.AtivadaEm,
                Assinatura = assinaturas
                    .OrderByDescending(a => a.CriadoEm)
                    .ThenByDescending(a => a.Id)
                    .Select(a => new
                    {
                        a.Id,
                        a.PlanoId,
                        a.Status,
                        a.VigenteAte,
                        a.CanceladaEm,
                        a.CriadoEm,
                    })
                    .FirstOrDefault(),
                Parcelas = db.Parcelas.IgnoreQueryFilters().Count(p => p.FormaturaId == f.Id && p.Status != StatusDaParcela.Cancelada),
                ParcelasPagas = db.Parcelas.IgnoreQueryFilters().Count(p => p.FormaturaId == f.Id && p.Status == StatusDaParcela.Paga),
                Adesoes = db.Adesoes.IgnoreQueryFilters().Count(a => a.FormaturaId == f.Id),
                MembrosAtivos = db.Vinculos.IgnoreQueryFilters().Count(v => v.FormaturaId == f.Id && v.Ativo && v.DesligadoEm == null),
            }
        ).FirstOrDefaultAsync(ct);

        if (turma is null)
            return null;

        var plano = turma.Assinatura is null
            ? null
            : await db
                .Planos.AsNoTracking()
                .Where(p => p.Id == turma.Assinatura.PlanoId)
                .Select(p => new
                {
                    p.Nome,
                    p.Codigo,
                    p.LimiteDeFormandos,
                })
                .FirstOrDefaultAsync(ct);

        var pagamentos = await (
            from c in db.CobrancasDaAssinatura.AsNoTracking()
            join a in db.Assinaturas.AsNoTracking().IgnoreQueryFilters() on c.AssinaturaId equals a.Id
            join p in db.Planos.AsNoTracking() on c.PlanoId equals p.Id
            where a.FormaturaId == formaturaId
            orderby c.CriadoEm descending, c.Id descending
            select new CobrancaDoPlanoResumo(
                c.Id,
                p.Nome,
                c.Motivo,
                c.Meio,
                c.ValorEmCentavos,
                c.Situacao,
                c.Url,
                c.CriadoEm,
                c.PagaEm,
                c.ValorEstornadoEmCentavos,
                c.EstornadaEm
            )
        ).ToListAsync(ct);

        return new TurmaNoSuporte(
            turma.Id,
            turma.Nome,
            turma.Instituicao,
            turma.Curso,
            turma.Ano,
            turma.Semestre,
            turma.Status.ToString(),
            turma.CriadoEm,
            turma.AtivadaEm,
            turma.Assinatura is null
                ? null
                : new AssinaturaNoSuporte(
                    turma.Assinatura.Id,
                    plano?.Nome ?? "(plano removido)",
                    plano?.LimiteDeFormandos ?? 0,
                    turma.Assinatura.Status.ToString(),
                    turma.Assinatura.VigenteAte,
                    turma.Assinatura.CanceladaEm,
                    turma.Assinatura.CriadoEm
                ),
            turma.MembrosAtivos,
            turma.Parcelas,
            turma.ParcelasPagas,
            turma.Adesoes,
            pagamentos
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Paga e estornada entram — a estornada saiu na nota do mês em que foi paga, e quem emite precisa ver a devolução
    /// ao lado. O Presidente é o de vínculo ativo mais antigo; o CPF é decifrado ao materializar e sai inteiro, ao
    /// contrário do resto do painel: é o que o portal da prefeitura pede para identificar o tomador.
    /// </remarks>
    public async Task<IReadOnlyList<PagamentoParaNota>> ListarPagamentosParaNotaDeTodasAsFormaturas(
        DateTime inicio,
        DateTime fim,
        CancellationToken ct = default
    )
    {
        var linhas = await (
            from c in db.CobrancasDaAssinatura.AsNoTracking()
            join a in db.Assinaturas.AsNoTracking().IgnoreQueryFilters() on c.AssinaturaId equals a.Id
            join f in db.Formaturas.AsNoTracking() on a.FormaturaId equals f.Id
            join p in db.Planos.AsNoTracking() on c.PlanoId equals p.Id
            where
                c.PagaEm >= inicio
                && c.PagaEm < fim
                && (c.Situacao == SituacaoDaCobrancaDoPlano.Paga || c.Situacao == SituacaoDaCobrancaDoPlano.Estornada)
            orderby c.PagaEm, c.Id
            select new
            {
                PagaEm = c.PagaEm!.Value,
                Turma = f.Nome,
                f.Instituicao,
                Plano = p.Nome,
                c.Motivo,
                c.Meio,
                c.ValorEmCentavos,
                c.ValorEstornadoEmCentavos,
                c.IdDoPagamento,
                Presidente = (
                    from v in db.Vinculos.IgnoreQueryFilters()
                    join u in db.Users on v.UsuarioId equals u.Id
                    where v.FormaturaId == f.Id && v.Ativo && v.Papel == PapelNaFormatura.Presidente
                    orderby v.CriadoEm
                    select new
                    {
                        u.Nome,
                        u.Email,
                        v.Id,
                    }
                ).FirstOrDefault(),
            }
        ).ToListAsync(ct);

        var vinculos = linhas.Where(l => l.Presidente != null).Select(l => l.Presidente!.Id).Distinct().ToList();

        var cpfs = await db
            .PerfisDeFormandos.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(perfil => vinculos.Contains(perfil.VinculoId))
            .Select(perfil => new { perfil.VinculoId, perfil.Cpf })
            .ToListAsync(ct);

        return
        [
            .. linhas.Select(l => new PagamentoParaNota(
                l.PagaEm,
                l.Turma,
                l.Instituicao,
                l.Plano,
                l.Motivo,
                l.Meio,
                l.ValorEmCentavos,
                l.ValorEstornadoEmCentavos,
                l.Presidente?.Nome,
                l.Presidente?.Email,
                cpfs.FirstOrDefault(cpf => cpf.VinculoId == l.Presidente?.Id)?.Cpf,
                l.IdDoPagamento
            )),
        ];
    }

    /// <inheritdoc />
    public async Task<UsuarioNoSuporte?> ObterUsuarioDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default)
    {
        var conta = await db
            .Users.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => new
            {
                u.Id,
                u.Nome,
                Email = u.Email!,
                u.EmailConfirmed,
                u.Ativo,
                u.LockoutEnd,
                u.AccessFailedCount,
                u.AnonimizadoEm,
                u.CriadoEm,
                Perfis = (
                    from vinculo in db.UserRoles
                    join perfil in db.Roles on vinculo.RoleId equals perfil.Id
                    where vinculo.UserId == u.Id
                    select perfil.Name!
                ).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (conta is null)
            return null;

        var vinculos = await (
            from v in db.Vinculos.AsNoTracking().IgnoreQueryFilters()
            join f in db.Formaturas.AsNoTracking() on v.FormaturaId equals f.Id
            where v.UsuarioId == usuarioId
            orderby v.Ativo descending, f.Nome
            select new VinculoNoSuporte(f.Id, f.Nome, f.Instituicao, f.Status.ToString(), v.Papel, v.Ativo, v.DesligadoEm)
        ).ToListAsync(ct);

        return new UsuarioNoSuporte(
            conta.Id,
            conta.Nome,
            conta.Email,
            conta.EmailConfirmed,
            conta.Ativo,
            conta.LockoutEnd?.UtcDateTime,
            conta.AccessFailedCount,
            conta.Perfis,
            conta.AnonimizadoEm,
            conta.CriadoEm,
            vinculos,
            await ComunicacaoDoKapaRepository.DoTitular(db, usuarioId, EnviosDeMarketingNoSuporte, ct)
        );
    }
}
