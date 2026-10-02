using Backend.Business.Abstractions;
using Backend.Business.Agenda.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Texto;
using Backend.Business.Loja.Interfaces;
using Backend.Business.Loja.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Data.Context;
using Backend.Data.Criptografia;
using Backend.Data.Mappings;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// As compras da loja pública da formatura selecionada (Sprint 26).
/// </summary>
/// <remarks>
/// As duas escritas que decidem lugar — reservar e expirar — são SQL à mão, uma instrução condicional cada,
/// na transação de quem chama (decisões 7 e 8): a garantia mora no <c>WHERE</c> e no <c>CHECK</c>, não numa
/// leitura antes. Como todo SQL à mão daqui, levam a <c>formatura_id</c> da sessão.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
/// <param name="cifra">HMAC do CPF, a coluna pesquisável ao lado da cifrada.</param>
public sealed class CompraDeConviteRepository(AppDbContext db, CifraDeCampo cifra) : ICompraDeConviteRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemDeCobranca>> ListarItensDaLoja(CancellationToken ct = default) =>
        await (
            from item in db.ItensDeCobranca.AsNoTracking()
            join plano in db.PlanosDeCobranca.AsNoTracking() on item.PlanoId equals plano.Id
            where plano.Status == StatusDoPlano.Vigente && item.Opcional && item.ModoDeVenda == ModoDeVenda.Publica && item.EncerradoEm == null
            select item
        ).ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>Uma ida ao banco: a compra repetida vem por subconsulta na mesma instrução.</remarks>
    public async Task<(ItemDeCobranca? Item, CompraDeConvite? Repetida)> ObterParaComprar(Guid itemId, Guid chave, CancellationToken ct = default)
    {
        var linha = await db
            .ItensDeCobranca.AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => new { Item = i, Repetida = db.ComprasDeConvite.AsNoTracking().FirstOrDefault(c => c.ChaveDeIdempotencia == chave) })
            .FirstOrDefaultAsync(ct);

        return linha is null ? (null, await ObterPorChave(chave, ct)) : (linha.Item, linha.Repetida);
    }

    /// <inheritdoc />
    public Task<ItemDeCobranca?> ObterItem(Guid itemId, CancellationToken ct = default) =>
        db.ItensDeCobranca.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);

    /// <inheritdoc />
    public async Task<bool> ReservarNoItem(Guid itemId, int quantidade, CancellationToken ct = default)
    {
        var formaturaId = FormaturaDaSessao();
        var agora = DateTime.UtcNow;

        var reservados = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE itens_de_cobranca
               SET reservados = reservados + {quantidade}, atualizado_em = {agora}
             WHERE id = {itemId}
               AND formatura_id = {formaturaId}
               AND opcional
               AND modo_de_venda = 'Publica'
               AND encerrado_em IS NULL
               AND (estoque IS NULL OR reservados + {quantidade} <= estoque)
            """,
            ct
        );

        return reservados == 1;
    }

    /// <inheritdoc />
    public Task TravarChave(Guid chave, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({chave.ToString("N")}, 0))", ct);

    /// <inheritdoc />
    public Task<CompraDeConvite?> ObterPorChave(Guid chave, CancellationToken ct = default) =>
        db.ComprasDeConvite.AsNoTracking().FirstOrDefaultAsync(c => c.ChaveDeIdempotencia == chave, ct);

    /// <inheritdoc />
    public async Task<int> ContarDoCpf(Guid itemId, string cpf, CancellationToken ct = default)
    {
        var hmac = cifra.Hmac(cpf);

        return await db
            .ComprasDeConvite.Where(c =>
                c.ItemDeCobrancaId == itemId
                && EF.Property<string?>(c, CompraDeConviteMapping.PropriedadeDoHmac) == hmac
                && c.Status != StatusDaCompra.Expirada
            )
            .SumAsync(c => c.Quantidade - c.ConvitesCancelados, ct);
    }

    /// <inheritdoc />
    public async Task Adicionar(CompraDeConvite compra, CancellationToken ct = default)
    {
        var entrada = await db.ComprasDeConvite.AddAsync(compra, ct);

        entrada.Property(CompraDeConviteMapping.PropriedadeDoHmac).CurrentValue = compra.Cpf is { } cpf ? cifra.Hmac(cpf) : null;
    }

    /// <inheritdoc />
    public Task<Guid?> ObterFormaturaDeTodasAsFormaturas(Guid compraId, CancellationToken ct = default) =>
        db
            .ComprasDeConvite.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == compraId)
            .Select(c => (Guid?)c.FormaturaId)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<CompraDeConvite?> Obter(Guid compraId, CancellationToken ct = default) =>
        db.ComprasDeConvite.AsNoTracking().FirstOrDefaultAsync(c => c.Id == compraId, ct);

    /// <inheritdoc />
    public Task<CompraDeConvite?> ObterParaEdicao(Guid compraId, CancellationToken ct = default) =>
        db.ComprasDeConvite.FirstOrDefaultAsync(c => c.Id == compraId, ct);

    /// <inheritdoc />
    public Task<CompraDeConvite?> Travar(Guid compraId, CancellationToken ct = default) =>
        db.ComprasDeConvite.FromSql($"SELECT * FROM compras_de_convite WHERE id = {compraId} FOR UPDATE").FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompraDeConvite>> ListarDoEmailParaEdicao(string email, CancellationToken ct = default) =>
        await db.ComprasDeConvite.Where(c => c.Email == email).ToListAsync(ct);

    /// <inheritdoc />
    public Task DevolverAoItem(Guid itemId, int quantidade, CancellationToken ct = default)
    {
        var formaturaId = FormaturaDaSessao();
        var agora = DateTime.UtcNow;

        return db.Database.ExecuteSqlAsync(
            $"""
            UPDATE itens_de_cobranca
               SET reservados = GREATEST(0, reservados - {quantidade}), atualizado_em = {agora}
             WHERE id = {itemId} AND formatura_id = {formaturaId}
            """,
            ct
        );
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConviteDaCompra>> ListarConvites(Guid compraId, CancellationToken ct = default) =>
        await db
            .ConvitesDoEvento.AsNoTracking()
            .Where(c => c.CompraId == compraId)
            .OrderBy(c => c.RevogadoEm != null)
            .ThenBy(c => c.Sequencial)
            .ThenByDescending(c => c.EmitidoEm)
            .Select(c => new ConviteDaCompra(
                c.Id,
                c.Sequencial,
                c.Codigo,
                c.NomeDoConvidado,
                db.CheckIns.Where(k => k.ConviteId == c.Id && k.DesfeitoEm == null).Select(k => (DateTime?)k.ValidadoEm).FirstOrDefault(),
                c.RevogadoEm,
                c.MotivoDaRevogacao
            ))
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<PedidoDeCancelamento?> ObterUltimoPedido(Guid compraId, CancellationToken ct = default) =>
        db
            .PedidosDeCancelamento.AsNoTracking()
            .Where(p => p.CompraId == compraId)
            .OrderBy(p => p.Status != StatusDoPedidoDeCancelamento.Aberto)
            .ThenByDescending(p => p.PedidoEm)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<PedidoDeCancelamento?> TravarPedido(Guid pedidoId, CancellationToken ct = default) =>
        db.PedidosDeCancelamento.FromSql($"SELECT * FROM pedidos_de_cancelamento WHERE id = {pedidoId} FOR UPDATE").FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PedidoNaGestao>> ListarPedidosAbertos(CancellationToken ct = default) =>
        await (
            from pedido in db.PedidosDeCancelamento.AsNoTracking()
            join compra in db.ComprasDeConvite.AsNoTracking() on pedido.CompraId equals compra.Id
            join item in db.ItensDeCobranca.AsNoTracking() on compra.ItemDeCobrancaId equals item.Id
            where pedido.Status == StatusDoPedidoDeCancelamento.Aberto
            orderby pedido.PedidoEm
            select new PedidoNaGestao(
                pedido.Id,
                compra.Id,
                compra.NomeDoComprador,
                compra.Email,
                string.IsNullOrWhiteSpace(item.Descricao) ? "Convite da festa" : item.Descricao,
                compra.Quantidade,
                pedido.ConviteIds.Length,
                pedido.Motivo,
                pedido.PedidoEm
            )
        ).ToListAsync(ct);

    /// <inheritdoc />
    public async Task AdicionarPedido(PedidoDeCancelamento pedido, CancellationToken ct = default) =>
        await db.PedidosDeCancelamento.AddAsync(pedido, ct);

    /// <inheritdoc />
    public void EsquecerCpf(CompraDeConvite compra) => db.Entry(compra).Property(CompraDeConviteMapping.PropriedadeDoHmac).CurrentValue = null;

    /// <inheritdoc />
    public async Task<bool> Expirar(Guid compraId, DateTime agora, CancellationToken ct = default)
    {
        var formaturaId = FormaturaDaSessao();

        var devolvidos = await db.Database.ExecuteSqlAsync(
            $"""
            WITH expirada AS (
                UPDATE compras_de_convite
                   SET status = 'Expirada', atualizado_em = {agora}
                 WHERE id = {compraId} AND formatura_id = {formaturaId} AND status = 'Pendente' AND expira_em < {agora}
             RETURNING item_de_cobranca_id, quantidade
            )
            UPDATE itens_de_cobranca AS item
               SET reservados = GREATEST(0, item.reservados - expirada.quantidade), atualizado_em = {agora}
              FROM expirada
             WHERE item.id = expirada.item_de_cobranca_id
            """,
            ct
        );

        return devolvidos == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompraAExpirar>> ListarAExpirarDeTodasAsFormaturas(DateTime agora, int limite, CancellationToken ct = default) =>
        await db
            .ComprasDeConvite.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Status == StatusDaCompra.Pendente && c.ExpiraEm < agora)
            .OrderBy(c => c.ExpiraEm)
            .Take(limite)
            .Select(c => new CompraAExpirar(c.Id, c.FormaturaId))
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompraDeConvite>> ListarPagasSemConvite(Guid eventoId, CancellationToken ct = default) =>
        await db
            .ComprasDeConvite.AsNoTracking()
            .Where(c =>
                c.Status == StatusDaCompra.Paga
                && db.ConvitesDoEvento.Count(convite => convite.CompraId == c.Id && convite.EventoId == eventoId && convite.RevogadoEm == null)
                    < c.Quantidade
            )
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<PaginaDe<CompraNaGestao>> Listar(PaginacaoRequest paginacao, FiltroDeCompras filtro, CancellationToken ct = default)
    {
        var consulta = Filtrar(filtro);

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<CompraNaGestao>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "comprador" => consulta.Por(linha => linha.Compra.NomeDoComprador, desc),
            "valor" => consulta.Por(linha => linha.Compra.ValorEmCentavos, desc),
            "comprou_em" => consulta.Por(linha => linha.Compra.CriadoEm, desc),
            _ => consulta.OrderByDescending(linha => linha.Compra.CriadoEm),
        };

        var linhas = await ordenada.ThenBy(linha => linha.Compra.Id).Skip(paginacao.Pular).Take(paginacao.Tamanho).ToListAsync(ct);

        return new PaginaDe<CompraNaGestao>([.. linhas.Select(NaGestao)], paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompraNaGestao>> ListarTodas(FiltroDeCompras filtro, CancellationToken ct = default) =>
        [
            .. (await Filtrar(filtro).OrderByDescending(linha => linha.Compra.CriadoEm).ThenBy(linha => linha.Compra.Id).ToListAsync(ct)).Select(
                NaGestao
            ),
        ];

    /// <inheritdoc />
    public async Task<ResumoDaLoja> Resumir(CancellationToken ct = default)
    {
        var grupos = await db
            .ComprasDeConvite.AsNoTracking()
            .GroupBy(c => new { c.Status, c.Meio })
            .Select(g => new
            {
                g.Key.Status,
                g.Key.Meio,
                Compras = g.Count(),
                Convites = g.Sum(c => c.Quantidade - c.ConvitesCancelados),
                Pago = g.Sum(c => (c.ValorPagoEmCentavos ?? 0) - c.ValorEstornadoEmCentavos),
            })
            .ToListAsync(ct);

        return new ResumoDaLoja(
            grupos.Where(g => g.Status != StatusDaCompra.Pendente && g.Status != StatusDaCompra.Expirada).Sum(g => g.Convites),
            grupos.Where(g => g.Status == StatusDaCompra.Pendente && g.Meio == MeioDePagamento.Pix).Sum(g => g.Convites),
            grupos.Where(g => g.Status == StatusDaCompra.ADevolver).Sum(g => g.Compras),
            grupos.Sum(g => g.Pago)
        );
    }

    /// <inheritdoc />
    public Task<int> DescartarDadosDeTodasAsFormaturas(DateOnly eventosAte, CancellationToken ct = default) =>
        db
            .ComprasDeConvite.IgnoreQueryFilters()
            .Where(c =>
                c.DadosApagadosEm == null
                && db.EventosDaTurma.IgnoreQueryFilters()
                    .Any(e => e.FormaturaId == c.FormaturaId && e.Tipo == TipoDeEvento.Festa && e.Data < eventosAte)
            )
            .ExecuteUpdateAsync(
                campos =>
                    campos
                        .SetProperty(c => c.Email, (string?)null)
                        .SetProperty(c => c.Cpf, (string?)null)
                        .SetProperty(c => c.CpfDoPagador, (string?)null)
                        .SetProperty(c => c.Convidados, (string?)null)
                        .SetProperty(c => EF.Property<string?>(c, CompraDeConviteMapping.PropriedadeDoHmac), (string?)null)
                        .SetProperty(c => c.DadosApagadosEm, DateTime.UtcNow)
                        .SetProperty(c => c.VersaoDoLink, c => c.VersaoDoLink + 1),
                ct
            );

    /// <summary>As compras com o nome do item, filtradas por status e pela busca no nome ou no e-mail.</summary>
    private IQueryable<LinhaDeCompra> Filtrar(FiltroDeCompras filtro)
    {
        var consulta =
            from compra in db.ComprasDeConvite.AsNoTracking()
            join item in db.ItensDeCobranca.AsNoTracking() on compra.ItemDeCobrancaId equals item.Id
            select new LinhaDeCompra
            {
                Compra = compra,
                Item = item.Descricao,
                PedidoAberto = db.PedidosDeCancelamento.Any(p => p.CompraId == compra.Id && p.Status == StatusDoPedidoDeCancelamento.Aberto),
            };

        if (filtro.Status is { } status)
            consulta = consulta.Where(linha => linha.Compra.Status == status);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = Busca.Padrao(Busca.Literal(filtro.Busca.Trim()));
            consulta = consulta.Where(linha =>
                EF.Functions.ILike(EF.Functions.Unaccent(linha.Compra.NomeDoComprador ?? string.Empty), termo)
                || EF.Functions.ILike(linha.Compra.Email ?? string.Empty, termo)
            );
        }

        return consulta;
    }

    private static CompraNaGestao NaGestao(LinhaDeCompra linha)
    {
        var compra = linha.Compra;

        return new CompraNaGestao(
            compra.Id,
            compra.CriadoEm,
            compra.NomeDoComprador,
            compra.Email,
            FormatosBrasileiros.MascararCpf(compra.Cpf),
            string.IsNullOrWhiteSpace(linha.Item) ? "Convite da festa" : linha.Item,
            compra.Quantidade,
            compra.ValorEmCentavos,
            compra.Meio,
            compra.Status,
            compra.ExpiraEm,
            compra.PagaEm,
            compra.ValorPagoEmCentavos,
            compra.CpfDoPagador is { } pagador && compra.Cpf is { } comprador && FormatosBrasileiros.SomenteDigitos(pagador) != comprador,
            compra.ConvitesCancelados,
            compra.ValorADevolverEmCentavos,
            linha.PedidoAberto
        );
    }

    private Guid FormaturaDaSessao() => db.FormaturaAtualId ?? throw new InvalidOperationException("Escrita da loja sem formatura selecionada.");
}

/// <summary>Uma compra com o nome do item — a forma intermediária da lista da Gestão.</summary>
internal sealed class LinhaDeCompra
{
    /// <summary>A compra.</summary>
    public required CompraDeConvite Compra { get; init; }

    /// <summary>Descrição do item.</summary>
    public string? Item { get; init; }

    /// <summary>Se há pedido de cancelamento esperando a Gestão.</summary>
    public bool PedidoAberto { get; init; }
}
