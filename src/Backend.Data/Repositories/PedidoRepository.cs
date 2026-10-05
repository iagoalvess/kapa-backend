using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Loja.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Pedidos dos opcionais da formatura selecionada.
/// </summary>
/// <remarks>
/// Pedido e item são isolados pelo filtro global; vínculo e usuário, não — mas só entram pelo
/// <c>JOIN</c> a partir do pedido, que já veio filtrado. É o mesmo desenho do
/// <c>ParcelaRepository</c>, e o nome de quem pediu sai da mesma regra: o civil do cadastro quando
/// houver, senão o da conta.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class PedidoRepository(AppDbContext db) : IPedidoRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// SQL à mão porque o EF Core não expressa <c>FOR UPDATE</c>; o filtro global da formatura é
    /// aplicado por fora do SQL, como em toda consulta daqui. Rastreado: quem escreve o contador de
    /// reservas é a entidade, e o <c>SalvarAsync</c> do service o persiste na mesma transação.
    /// </remarks>
    public Task<ItemDeCobranca?> TravarItem(Guid itemId, CancellationToken ct = default) =>
        db.ItensDeCobranca.FromSql($"SELECT * FROM itens_de_cobranca WHERE id = {itemId} FOR UPDATE").FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<Pedido?> ObterParaEdicao(Guid vinculoId, Guid itemId, CancellationToken ct = default) =>
        db.Pedidos.FirstOrDefaultAsync(p => p.VinculoId == vinculoId && p.ItemDeCobrancaId == itemId, ct);

    /// <inheritdoc />
    public Task<Pedido?> ObterParaEdicao(Guid pedidoId, CancellationToken ct = default) => db.Pedidos.FirstOrDefaultAsync(p => p.Id == pedidoId, ct);

    /// <inheritdoc />
    public Task<PedidoResumo?> Obter(Guid pedidoId, CancellationToken ct = default) =>
        Projetar(Linhas().Where(linha => linha.Pedido.Id == pedidoId)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PedidoResumo>> ListarDoVinculo(Guid vinculoId, CancellationToken ct = default) =>
        await Projetar(
                Linhas()
                    .Where(linha => linha.Pedido.VinculoId == vinculoId)
                    .OrderByDescending(linha => linha.Pedido.PedidoEm)
                    .ThenBy(linha => linha.Pedido.Id)
            )
            .ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>Do mais novo para o mais antigo, com o id de desempate: sem ele, dois pedidos do mesmo instante trocam de página.</remarks>
    public async Task<PaginaDe<PedidoResumo>> Listar(PaginacaoRequest paginacao, FiltroDePedidos filtro, CancellationToken ct = default)
    {
        var consulta = Filtrar(Linhas(), filtro);

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<PedidoResumo>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "formando" => consulta.Por(linha => linha.Nome, desc),
            "quantidade" => consulta.Por(linha => linha.Pedido.Quantidade, desc),
            "pedido_em" => consulta.Por(linha => linha.Pedido.PedidoEm, desc),
            _ => consulta.OrderByDescending(linha => linha.Pedido.PedidoEm),
        };

        var linhas = await Projetar(ordenada.ThenBy(linha => linha.Pedido.Id).Skip(paginacao.Pular).Take(paginacao.Tamanho)).ToListAsync(ct);

        return new PaginaDe<PedidoResumo>(linhas, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uma consulta só, com as somas por subconsulta correlacionada — o mesmo desenho do
    /// <c>ItemDaFestaRepository</c> e pelo mesmo motivo: são poucos itens opcionais por turma, e
    /// assim a faixa da tela continua sendo uma projeção direta no <c>SELECT</c>.
    /// </remarks>
    public async Task<IReadOnlyList<ResumoDoItemPedido>> Resumir(CancellationToken ct = default)
    {
        var linhas = await db
            .ItensDeCobranca.AsNoTracking()
            .Where(item => item.Opcional)
            .OrderBy(item => item.CriadoEm)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.Tipo,
                item.Descricao,
                item.ValorEmCentavos,
                item.Estoque,
                item.Reservados,
                Pedidos = db.Pedidos.Count(p => p.ItemDeCobrancaId == item.Id && p.Status == StatusDoPedido.Confirmado),
                Unidades = db.Pedidos.Where(p => p.ItemDeCobrancaId == item.Id && p.Status == StatusDoPedido.Confirmado).Sum(p => (int?)p.Quantidade)
                    ?? 0,
                Pago = db.Parcelas.Where(parcela =>
                        parcela.ItemDeCobrancaId == item.Id
                        && db.Pedidos.Any(p =>
                            p.ItemDeCobrancaId == item.Id && p.VinculoId == parcela.VinculoId && p.Status == StatusDoPedido.Confirmado
                        )
                    )
                    .Sum(parcela => (long?)parcela.ValorPagoEmCentavos)
                    ?? 0,
                Quitadas = db.Pedidos.Where(p =>
                        p.ItemDeCobrancaId == item.Id
                        && p.Status == StatusDoPedido.Confirmado
                        && (
                            db.Parcelas.Where(parcela => parcela.ItemDeCobrancaId == item.Id && parcela.VinculoId == p.VinculoId)
                                .Sum(parcela => (long?)parcela.ValorPagoEmCentavos)
                            ?? 0
                        )
                            >= item.ValorEmCentavos * p.Quantidade
                    )
                    .Sum(p => (int?)p.Quantidade)
                    ?? 0,
            })
            .ToListAsync(ct);

        return
        [
            .. linhas.Select(linha => new ResumoDoItemPedido(
                linha.Id,
                linha.Tipo,
                linha.Descricao,
                linha.Pedidos,
                linha.Unidades,
                linha.Quitadas,
                linha.Estoque,
                linha.Estoque is { } teto ? Math.Max(0, teto - linha.Reservados) : null,
                linha.ValorEmCentavos * linha.Unidades,
                linha.Pago
            )),
        ];
    }

    /// <inheritdoc />
    public Task<bool> ExisteDoItem(Guid itemId, CancellationToken ct = default) => db.Pedidos.AnyAsync(p => p.ItemDeCobrancaId == itemId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Parcela>> ListarParcelasParaEdicao(Pedido pedido, CancellationToken ct = default) =>
        await db
            .Parcelas.Where(p => p.VinculoId == pedido.VinculoId && p.ItemDeCobrancaId == pedido.ItemDeCobrancaId)
            .OrderBy(p => p.Numero)
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<Guid?> ObterItemDeConviteEmVenda(CancellationToken ct = default) =>
        db
            .ItensDeCobranca.AsNoTracking()
            .Where(item => item.Opcional && item.Tipo == TipoDeCobranca.ConviteExtra && item.EncerradoEm == null)
            .OrderBy(item => item.CriadoEm)
            .ThenBy(item => item.Id)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Quitado com a mesma regra de <c>PedidoService.AposBaixa</c>: nenhuma parcela aberta e ao menos
    /// uma paga. A conta de convites é por subconsulta — são poucos pedidos por turma.
    /// </remarks>
    public async Task<IReadOnlyList<PedidoDeConviteQuitado>> ListarDeConviteQuitados(Guid eventoId, CancellationToken ct = default)
    {
        var linhas = await (
            from pedido in db.Pedidos.AsNoTracking()
            join item in db.ItensDeCobranca.AsNoTracking() on pedido.ItemDeCobrancaId equals item.Id
            where pedido.Status == StatusDoPedido.Confirmado && item.Tipo == TipoDeCobranca.ConviteExtra
            where
                db.Parcelas.Any(parcela =>
                    parcela.VinculoId == pedido.VinculoId
                    && parcela.ItemDeCobrancaId == pedido.ItemDeCobrancaId
                    && parcela.Status == StatusDaParcela.Paga
                )
                && !db.Parcelas.Any(parcela =>
                    parcela.VinculoId == pedido.VinculoId
                    && parcela.ItemDeCobrancaId == pedido.ItemDeCobrancaId
                    && (parcela.Status == StatusDaParcela.Aberta || parcela.Status == StatusDaParcela.Vencida)
                )
            select new
            {
                Pedido = pedido,
                Validos = db.ConvitesDoEvento.Count(convite =>
                    convite.PedidoId == pedido.Id && convite.EventoId == eventoId && convite.RevogadoEm == null
                ),
            }
        ).ToListAsync(ct);

        return [.. linhas.Select(linha => new PedidoDeConviteQuitado(linha.Pedido, linha.Validos))];
    }

    /// <inheritdoc />
    public Task<int> ContarDeConviteComParcelaDepoisDe(DateOnly dia, CancellationToken ct = default) =>
        (
            from pedido in db.Pedidos
            join item in db.ItensDeCobranca on pedido.ItemDeCobrancaId equals item.Id
            where
                pedido.Status == StatusDoPedido.Confirmado
                && item.Tipo == TipoDeCobranca.ConviteExtra
                && db.Parcelas.Any(parcela =>
                    parcela.VinculoId == pedido.VinculoId
                    && parcela.ItemDeCobrancaId == pedido.ItemDeCobrancaId
                    && parcela.Status == StatusDaParcela.Aberta
                    && parcela.Vencimento > dia
                )
            select pedido.Id
        ).CountAsync(ct);

    /// <inheritdoc />
    public async Task Adicionar(Pedido pedido, CancellationToken ct = default) => await db.Pedidos.AddAsync(pedido, ct);

    /// <summary>O pedido com o item, quem pediu e o que já entrou pelas parcelas dele.</summary>
    /// <remarks>
    /// O pago vem por subconsulta sobre as parcelas do par <c>(vínculo, item)</c>, que é o recorte do
    /// pedido (decisão 3) — a parcela não ganhou coluna nova, e é isso que a deixa igual a todas as
    /// outras para o extrato, o PIX, a baixa e o balancete.
    /// </remarks>
    private IQueryable<LinhaDePedido> Linhas() =>
        from pedido in db.Pedidos.AsNoTracking()
        join item in db.ItensDeCobranca.AsNoTracking() on pedido.ItemDeCobrancaId equals item.Id
        join vinculo in db.Vinculos.AsNoTracking() on pedido.VinculoId equals vinculo.Id
        join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
        join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
        from perfil in perfis.DefaultIfEmpty()
        select new LinhaDePedido
        {
            Pedido = pedido,
            Item = item,
            UsuarioId = usuario.Id,
            Nome = perfil != null && perfil.NomeCompleto != null ? perfil.NomeCompleto : usuario.Nome,
            NomeDaConta = usuario.Nome,
            PagoEmCentavos =
                db.Parcelas.Where(parcela => parcela.VinculoId == pedido.VinculoId && parcela.ItemDeCobrancaId == pedido.ItemDeCobrancaId)
                    .Sum(parcela => (long?)parcela.ValorPagoEmCentavos)
                ?? 0,
            CancelamentoSolicitado = db.SolicitacoesDeCancelamento.Any(s =>
                s.VinculoId == pedido.VinculoId && s.ItemDeCobrancaId == pedido.ItemDeCobrancaId && s.Status == StatusDoPedidoDeCancelamento.Aberto
            ),
        };

    /// <summary>Item, situação, quitação e busca pelo nome de quem pediu.</summary>
    private static IQueryable<LinhaDePedido> Filtrar(IQueryable<LinhaDePedido> consulta, FiltroDePedidos filtro)
    {
        if (filtro.ItemDeCobrancaId is { } itemId)
            consulta = consulta.Where(linha => linha.Pedido.ItemDeCobrancaId == itemId);

        if (filtro.Status is { } status)
            consulta = consulta.Where(linha => linha.Pedido.Status == status);

        if (filtro.Quitado is { } quitado)
            consulta = consulta.Where(linha => (linha.PagoEmCentavos >= linha.Item.ValorEmCentavos * linha.Pedido.Quantidade) == quitado);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = Busca.Padrao(filtro.Busca);
            consulta = consulta.Where(linha =>
                EF.Functions.ILike(EF.Functions.Unaccent(linha.Nome), termo) || EF.Functions.ILike(EF.Functions.Unaccent(linha.NomeDaConta), termo)
            );
        }

        return consulta;
    }

    /// <summary>A linha no modelo de leitura.</summary>
    /// <param name="linhas">Linhas já filtradas e ordenadas.</param>
    private static IQueryable<PedidoResumo> Projetar(IQueryable<LinhaDePedido> linhas) =>
        linhas.Select(linha => new PedidoResumo(
            linha.Pedido.Id,
            linha.Pedido.ItemDeCobrancaId,
            linha.Item.Tipo,
            linha.Item.Descricao,
            linha.UsuarioId,
            linha.Nome,
            linha.Pedido.Quantidade,
            linha.Pedido.Parcelas,
            linha.Item.ValorEmCentavos * linha.Pedido.Quantidade,
            linha.PagoEmCentavos,
            linha.Pedido.Status,
            linha.Pedido.PedidoEm,
            linha.Pedido.Observacao,
            linha.Item.CancelavelAte,
            linha.CancelamentoSolicitado
        ));
}

/// <summary>Um pedido com o item, quem pediu e o que já entrou — a forma intermediária das consultas.</summary>
internal sealed class LinhaDePedido
{
    /// <summary>O pedido.</summary>
    public required Pedido Pedido { get; init; }

    /// <summary>O item opcional.</summary>
    public required ItemDeCobranca Item { get; init; }

    /// <summary>Dono.</summary>
    public Guid UsuarioId { get; init; }

    /// <summary>Nome civil, ou o da conta.</summary>
    public required string Nome { get; init; }

    /// <summary>Nome da conta — a busca procura nos dois.</summary>
    public required string NomeDaConta { get; init; }

    /// <summary>O que já entrou pelas parcelas deste pedido.</summary>
    public long PagoEmCentavos { get; init; }

    /// <summary>Há solicitação de cancelamento esperando a comissão (Sprint 48, D8).</summary>
    public bool CancelamentoSolicitado { get; init; }
}
