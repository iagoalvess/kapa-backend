using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Parcelas da formatura selecionada.
/// </summary>
/// <remarks>
/// Parcela e item são isolados pelo filtro global; vínculo e usuário, não — mas só entram pelo
/// <c>JOIN</c> a partir da parcela, que já veio filtrada.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class ParcelaRepository(AppDbContext db) : IParcelaRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> ListarNumerosGerados(Guid vinculoId, Guid itemId, CancellationToken ct = default) =>
        await db.Parcelas.AsNoTracking().Where(p => p.VinculoId == vinculoId && p.ItemDeCobrancaId == itemId).Select(p => p.Numero).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListarItensEmUso(Guid planoId, CancellationToken ct = default) =>
        await db
            .ItensDeCobranca.AsNoTracking()
            .Where(i => i.PlanoId == planoId && db.Parcelas.Any(p => p.ItemDeCobrancaId == i.Id))
            .Select(i => i.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<int> ContarVinculosComParcela(Guid planoId, CancellationToken ct = default) =>
        db
            .Parcelas.AsNoTracking()
            .Where(p => db.ItensDeCobranca.Any(i => i.Id == p.ItemDeCobrancaId && i.PlanoId == planoId))
            .Select(p => p.VinculoId)
            .Distinct()
            .CountAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListarVinculosAtivosComParcela(Guid planoId, CancellationToken ct = default) =>
        await db
            .Parcelas.AsNoTracking()
            .Where(p =>
                db.ItensDeCobranca.Any(i => i.Id == p.ItemDeCobrancaId && i.PlanoId == planoId)
                && db.Vinculos.Any(v => v.Id == p.VinculoId && v.Ativo)
            )
            .Select(p => p.VinculoId)
            .Distinct()
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<bool> ExisteDoItem(Guid itemId, CancellationToken ct = default) => db.Parcelas.AnyAsync(p => p.ItemDeCobrancaId == itemId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Parcela>> ListarAbertasParaEdicao(Guid itemId, DateOnly aPartirDe, CancellationToken ct = default) =>
        await db.Parcelas.Where(p => p.ItemDeCobrancaId == itemId && p.Status == StatusDaParcela.Aberta && p.Vencimento >= aPartirDe).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Parcela>> ListarDoItemParaEdicao(Guid itemId, CancellationToken ct = default) =>
        await db.Parcelas.Where(p => p.ItemDeCobrancaId == itemId).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Parcela>> ListarEmAbertoDoVinculoParaEdicao(Guid vinculoId, CancellationToken ct = default) =>
        await db.Parcelas.Where(p => p.VinculoId == vinculoId && p.Status == StatusDaParcela.Aberta).ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Nome civil do cadastro quando houver — é o que a tesouraria confere no comprovante —, senão o
    /// da conta. Ordem por vencimento, nome e id: sem desempate único, duas parcelas do mesmo dia
    /// trocam de página.
    /// <para>
    /// "Situação" não é coluna de ordenação: "Vencida" sai de <c>StatusNoDia</c>, calculado depois da
    /// consulta — ordenar pelo <c>Status</c> gravado deixaria vencida embaixo de aberta, que é
    /// justamente o que a tela separa.
    /// </para>
    /// </remarks>
    public async Task<PaginaDe<ParcelaResumo>> Listar(
        PaginacaoRequest paginacao,
        FiltroDeParcelas filtro,
        DateOnly hoje,
        CancellationToken ct = default
    )
    {
        var consulta = Filtrar(Linhas(), filtro, hoje);

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<ParcelaResumo>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "formando" => consulta.Por(linha => linha.Nome, desc),
            "parcela" => consulta.Por(linha => linha.Parcela.Numero, desc),
            "vencimento" => consulta.Por(linha => linha.Parcela.Vencimento, desc),
            "valor" => consulta.Por(linha => linha.Parcela.ValorOriginalEmCentavos, desc),
            _ => consulta.OrderBy(linha => linha.Parcela.Vencimento).ThenBy(linha => linha.Nome),
        };

        var linhas = await Projetar(ordenada.ThenBy(linha => linha.Parcela.Id).Skip(paginacao.Pular).Take(paginacao.Tamanho)).ToListAsync(ct);

        return new PaginaDe<ParcelaResumo>(NoDia(linhas, hoje), paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    public async Task<ParcelaResumo?> Obter(Guid parcelaId, DateOnly hoje, CancellationToken ct = default) =>
        NoDia(await Projetar(Linhas().Where(linha => linha.Parcela.Id == parcelaId)).ToListAsync(ct), hoje) is [var parcela] ? parcela : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParcelaResumo>> ListarDoVinculo(Guid vinculoId, DateOnly hoje, CancellationToken ct = default) =>
        NoDia(
            await Projetar(
                    Linhas()
                        .Where(linha => linha.Parcela.VinculoId == vinculoId)
                        .OrderBy(linha => linha.Parcela.Vencimento)
                        .ThenBy(linha => linha.Parcela.Numero)
                        .ThenBy(linha => linha.Parcela.Id)
                )
                .ToListAsync(ct),
            hoje
        );

    /// <inheritdoc />
    /// <remarks>
    /// "Vencida" é aberta com vencimento no passado, como em toda leitura daqui; o aviso pendente sai
    /// da mesma subconsulta que alimenta <c>EmConferencia</c> em <see cref="Linhas(AppDbContext)"/>.
    /// </remarks>
    public Task<int> ContarVencidasSemAviso(Guid vinculoId, DateOnly hoje, CancellationToken ct = default) =>
        db
            .Parcelas.AsNoTracking()
            .CountAsync(
                p =>
                    p.VinculoId == vinculoId
                    && p.Status == StatusDaParcela.Aberta
                    && p.Vencimento < hoje
                    && !db.Informes.Any(i => i.ParcelaId == p.Id && i.Status == StatusDoInforme.Pendente),
                ct
            );

    /// <inheritdoc />
    /// <remarks>
    /// Agrupa pelo status gravado e por "venceu antes de hoje", e só junta as duas coisas depois: a
    /// vencida não é coluna, e o <c>GROUP BY</c> numa expressão condicional sobre enum não traduz bem.
    /// </remarks>
    public async Task<IReadOnlyList<ContagemDeParcelas>> Contar(FiltroDeParcelas filtro, DateOnly hoje, CancellationToken ct = default)
    {
        var grupos = await Filtrar(Linhas(), filtro with { Status = null }, hoje)
            .GroupBy(linha => new { linha.Parcela.Status, Vencida = linha.Parcela.Vencimento < hoje })
            .Select(grupo => new
            {
                grupo.Key.Status,
                grupo.Key.Vencida,
                Quantidade = grupo.Count(),
                Original = grupo.Sum(linha => linha.Parcela.ValorOriginalEmCentavos),
                Pago = grupo.Sum(linha => linha.Parcela.ValorPagoEmCentavos ?? 0),
            })
            .ToListAsync(ct);

        return
        [
            .. grupos
                .GroupBy(grupo => grupo.Status == StatusDaParcela.Aberta && grupo.Vencida ? StatusDaParcela.Vencida : grupo.Status)
                .Select(situacao => new ContagemDeParcelas(
                    situacao.Key,
                    situacao.Sum(g => g.Quantidade),
                    situacao.Sum(g => g.Original),
                    situacao.Sum(g => g.Pago)
                )),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParcelaEmAtraso>> ListarEmAtraso(FiltroDeParcelas filtro, DateOnly hoje, CancellationToken ct = default) =>
        await Filtrar(Linhas(), filtro with { Status = StatusDaParcela.Vencida }, hoje)
            .Select(linha => new ParcelaEmAtraso(linha.Parcela.VinculoId, linha.Parcela.ValorOriginalEmCentavos, linha.Parcela.Vencimento))
            .ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// A adesão mais recente de cada vínculo, e o JSON do plano aceito lido de volta pelo mesmo
    /// <see cref="SnapshotDoPlano"/> que o gravou.
    /// <para>
    /// ponytail: traz o snapshot inteiro (a grade vem junto) para ler quatro números. Numa turma são
    /// dezenas de vínculos; se pesar, vira um <c>SELECT</c> com <c>plano_aceito::jsonb -&gt;&gt; '…'</c>.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, RegrasDeAtraso>> ObterRegrasDeAtraso(
        IReadOnlyCollection<Guid> vinculoIds,
        CancellationToken ct = default
    )
    {
        if (vinculoIds.Count == 0)
            return new Dictionary<Guid, RegrasDeAtraso>();

        var aceitos = await db
            .Adesoes.AsNoTracking()
            .Where(a => vinculoIds.Contains(a.VinculoId) && !db.Adesoes.Any(outra => outra.VinculoId == a.VinculoId && outra.Versao > a.Versao))
            .Select(a => new { a.VinculoId, a.PlanoAceito })
            .ToListAsync(ct);

        return aceitos.DistinctBy(a => a.VinculoId).ToDictionary(a => a.VinculoId, a => SnapshotDoPlano.Ler(a.PlanoAceito).Regras());
    }

    /// <inheritdoc />
    /// <remarks>
    /// SQL à mão porque o EF Core não expressa <c>FOR UPDATE</c>; o filtro global da formatura é aplicado
    /// por fora do SQL, como em toda consulta. Ordem por id: dois lotes que travam as mesmas parcelas
    /// travam na mesma ordem, e um não espera o outro em círculo.
    /// </remarks>
    public async Task<IReadOnlyList<Parcela>> TravarParaBaixa(IReadOnlyCollection<Guid> parcelaIds, CancellationToken ct = default) =>
        await db.Parcelas.FromSql($"SELECT *, xmin FROM parcelas WHERE id = ANY({parcelaIds.ToArray()}) ORDER BY id FOR UPDATE").ToListAsync(ct);

    /// <inheritdoc />
    public Task Adicionar(IReadOnlyList<Parcela> parcelas, CancellationToken ct = default) => db.Parcelas.AddRangeAsync(parcelas, ct);

    /// <summary>
    /// A parcela com o item, o dono e o que a lista precisa saber dela — a base de toda leitura daqui.
    /// </summary>
    /// <remarks>
    /// Também é a base da fila de conferência e das divergências (<c>InformeRepository</c>,
    /// <c>RecebimentoRepository</c>): a parcela aparece igual nas três telas.
    /// </remarks>
    /// <param name="db">Contexto de dados da requisição.</param>
    internal static IQueryable<LinhaDeParcela> Linhas(AppDbContext db) =>
        from devedor in Devedores(db)
        join item in db.ItensDeCobranca.AsNoTracking() on devedor.Parcela.ItemDeCobrancaId equals item.Id
        select new LinhaDeParcela
        {
            Parcela = devedor.Parcela,
            Item = item,
            UsuarioId = devedor.UsuarioId,
            Nome = devedor.Nome,
            NomeDaConta = devedor.NomeDaConta,
            Email = devedor.Email,
            EmConferencia = db.Informes.Any(i => i.ParcelaId == devedor.Parcela.Id && i.Status == StatusDoInforme.Pendente),
            RecebimentoId = db
                .Recebimentos.Where(r => r.ParcelaId == devedor.Parcela.Id && r.EstornadoEm == null)
                .OrderByDescending(r => r.BaixadoEm)
                .ThenByDescending(r => r.Id)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefault(),
        };

    /// <summary>
    /// A parcela e quem deve por ela, sem o item e sem a conferência.
    /// </summary>
    /// <remarks>
    /// A regra do nome — o civil do cadastro quando houver, senão o da conta — mora <b>só aqui</b>. É
    /// o que faz a tela de Parcelas, a conferência, o CSV e o painel de gestão chamarem a mesma pessoa
    /// pelo mesmo nome.
    /// <para>
    /// É <see cref="Linhas(AppDbContext)"/> sem o item e sem a subconsulta de <c>EmConferencia</c> —
    /// que a tela usa e a planilha não, e que não sobrevive a um <c>ORDER BY</c> nem a um
    /// <c>GROUP BY</c>: o EF Core não traduz, e a requisição morre em 500. Quem exporta e quem agrega
    /// (Sprint 12) sai daqui.
    /// </para>
    /// <para>
    /// As junções são escritas em linha, e não compostas de uma consulta menor: chave de <c>join</c>
    /// que aponta para um membro de projeção também não traduz.
    /// </para>
    /// </remarks>
    /// <param name="db">Contexto de dados da requisição.</param>
    internal static IQueryable<DevedorDeParcela> Devedores(AppDbContext db) =>
        from parcela in db.Parcelas.AsNoTracking()
        join vinculo in db.Vinculos.AsNoTracking() on parcela.VinculoId equals vinculo.Id
        join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
        join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
        from perfil in perfis.DefaultIfEmpty()
        select new DevedorDeParcela
        {
            Parcela = parcela,
            UsuarioId = usuario.Id,
            Nome = perfil != null && perfil.NomeCompleto != null ? perfil.NomeCompleto : usuario.Nome,
            NomeDaConta = usuario.Nome,
            Email = usuario.Email,
        };

    /// <summary>
    /// As parcelas de quem atende à busca por nome — a subconsulta de quem filtra pelo formando sem
    /// ter a parcela em mãos: a fila da conferência e as divergências.
    /// </summary>
    /// <remarks>Procura no nome civil e no da conta, como a lista de parcelas: são as duas grafias que a turma usa.</remarks>
    /// <param name="db">Contexto de dados da requisição.</param>
    /// <param name="busca">O que a pessoa digitou.</param>
    internal static IQueryable<Guid> ParcelasDe(AppDbContext db, string busca) => ComNome(Linhas(db), busca).Select(linha => linha.Parcela.Id);

    /// <summary>A linha no modelo de leitura, com o status gravado — <see cref="NoDia"/> separa a vencida.</summary>
    /// <param name="linhas">Linhas já filtradas e ordenadas.</param>
    internal static IQueryable<ParcelaResumo> Projetar(IQueryable<LinhaDeParcela> linhas) =>
        linhas.Select(linha => new ParcelaResumo(
            linha.Parcela.Id,
            linha.Parcela.VinculoId,
            linha.UsuarioId,
            linha.Nome,
            linha.Parcela.ItemDeCobrancaId,
            linha.Item.Tipo,
            linha.Item.Descricao,
            linha.Parcela.Numero,
            linha.Item.NumeroDeParcelas,
            linha.Parcela.Vencimento,
            linha.Parcela.ValorOriginalEmCentavos,
            linha.Parcela.Status,
            linha.EmConferencia,
            linha.Parcela.ValorPagoEmCentavos,
            linha.Parcela.PagoEm,
            null,
            linha.RecebimentoId
        ));

    /// <summary>A situação de cada linha no dia, pela mesma regra da entidade.</summary>
    /// <param name="parcelas">Parcelas lidas.</param>
    /// <param name="hoje">Dia de referência.</param>
    internal static IReadOnlyList<ParcelaResumo> NoDia(IEnumerable<ParcelaResumo> parcelas, DateOnly hoje) =>
        [.. parcelas.Select(parcela => parcela with { Status = Parcela.StatusNoDia(parcela.Status, parcela.Vencimento, hoje) })];

    private IQueryable<LinhaDeParcela> Linhas() => Linhas(db);

    /// <summary>
    /// Formando, período, busca e situação.
    /// </summary>
    /// <remarks>"Vencida" não é coluna: o filtro divide as abertas pelo dia de hoje.</remarks>
    private static IQueryable<LinhaDeParcela> Filtrar(IQueryable<LinhaDeParcela> consulta, FiltroDeParcelas filtro, DateOnly hoje)
    {
        if (filtro.UsuarioId is { } usuarioId)
            consulta = consulta.Where(linha => linha.UsuarioId == usuarioId);

        if (filtro.De is { } de)
            consulta = consulta.Where(linha => linha.Parcela.Vencimento >= de);

        if (filtro.Ate is { } ate)
            consulta = consulta.Where(linha => linha.Parcela.Vencimento <= ate);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
            consulta = ComNome(consulta, filtro.Busca);

        return filtro.Status switch
        {
            null => consulta,
            StatusDaParcela.Aberta => consulta.Where(linha => linha.Parcela.Status == StatusDaParcela.Aberta && linha.Parcela.Vencimento >= hoje),
            StatusDaParcela.Vencida => consulta.Where(linha => linha.Parcela.Status == StatusDaParcela.Aberta && linha.Parcela.Vencimento < hoje),
            var status => consulta.Where(linha => linha.Parcela.Status == status),
        };
    }

    /// <summary>As linhas cujo dono atende à busca, pelo nome civil ou pelo da conta, sem acento.</summary>
    /// <param name="consulta">Linhas a filtrar.</param>
    /// <param name="busca">O que a pessoa digitou.</param>
    private static IQueryable<LinhaDeParcela> ComNome(IQueryable<LinhaDeParcela> consulta, string busca)
    {
        var termo = Busca.Padrao(busca);

        return consulta.Where(linha =>
            EF.Functions.ILike(EF.Functions.Unaccent(linha.Nome), termo) || EF.Functions.ILike(EF.Functions.Unaccent(linha.NomeDaConta), termo)
        );
    }
}

/// <summary>Uma parcela com o item, o dono e o aviso pendente — a forma intermediária das consultas.</summary>
/// <remarks>Classe, e não tipo anônimo: é devolvida por método e composta por outros repositórios.</remarks>
internal sealed class LinhaDeParcela
{
    /// <summary>A parcela.</summary>
    public required Parcela Parcela { get; init; }

    /// <summary>O item de origem.</summary>
    public required ItemDeCobranca Item { get; init; }

    /// <summary>Dono.</summary>
    public Guid UsuarioId { get; init; }

    /// <summary>Nome civil, ou o da conta.</summary>
    public required string Nome { get; init; }

    /// <summary>Nome da conta — a busca procura nos dois.</summary>
    public required string NomeDaConta { get; init; }

    /// <summary>E-mail da conta — o destinatário da régua de cobrança (Sprint 13).</summary>
    public string? Email { get; init; }

    /// <summary>Tem informe pendente.</summary>
    public bool EmConferencia { get; init; }

    /// <summary>A última baixa que vale, se houver — o recibo da linha (Sprint 22).</summary>
    public Guid? RecebimentoId { get; init; }
}

/// <summary>A parcela e quem deve por ela — <see cref="LinhaDeParcela"/> sem o item e sem a conferência.</summary>
internal sealed class DevedorDeParcela
{
    /// <summary>A parcela.</summary>
    public required Parcela Parcela { get; init; }

    /// <summary>Dono.</summary>
    public Guid UsuarioId { get; init; }

    /// <summary>Nome civil, ou o da conta.</summary>
    public required string Nome { get; init; }

    /// <summary>Nome da conta — a busca procura nos dois.</summary>
    public required string NomeDaConta { get; init; }

    /// <summary>E-mail da conta — o destinatário da régua de cobrança (Sprint 13).</summary>
    public string? Email { get; init; }
}
