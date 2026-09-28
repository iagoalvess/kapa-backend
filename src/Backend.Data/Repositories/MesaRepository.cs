using Backend.Business.Cobrancas.Models;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// As mesas do jantar da formatura selecionada, e o direito de cada formando a elas.
/// </summary>
/// <remarks>
/// O direito é a soma da quantidade dos pedidos <b>confirmados</b> de itens do tipo <c>Mesa</c>
/// (23/09/2026): não espera a quitação, e o estorno de uma parcela não o tira. O nome do dono é o
/// civil do cadastro quando houver, senão o da conta — a mesma regra do pedido e da portaria.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class MesaRepository(AppDbContext db) : IMesaRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Uma consulta, com o dono por <c>LEFT JOIN</c>; a ordem é na memória, porque o EF não ordena sobre o
    /// construtor do record. Por tamanho e depois por texto, para "Mesa 2" vir
    /// antes de "Mesa 12".
    /// <para>ponytail: ordem natural barata; "Mesa dos pais" cai depois das numeradas curtas. Ordem manual quando a comissão pedir.</para>
    /// </remarks>
    public async Task<IReadOnlyList<MesaResumo>> Listar(Guid? vinculoId = null, CancellationToken ct = default)
    {
        var mesas = db.Mesas.AsNoTracking();

        if (vinculoId is { } dono)
            mesas = mesas.Where(mesa => mesa.VinculoId == dono);

        var consulta = Projetar(mesas);

        return
        [
            .. (await consulta.ToListAsync(ct))
                .OrderBy(mesa => mesa.Identificacao.Length)
                .ThenBy(mesa => mesa.Identificacao, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    /// <inheritdoc />
    public Task<MesaResumo?> Obter(Guid id, CancellationToken ct = default) =>
        Projetar(db.Mesas.AsNoTracking().Where(mesa => mesa.Id == id)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>Uma consulta: compradas e atribuídas por subconsulta correlacionada, por formando.</remarks>
    public async Task<IReadOnlyList<CompradorDeMesa>> ListarCompradores(CancellationToken ct = default) =>
        (
            await db
                .Vinculos.AsNoTracking()
                .Where(vinculo => PedidosDeMesa().Any(pedido => pedido.VinculoId == vinculo.Id))
                .Select(vinculo => new CompradorDeMesa(
                    vinculo.Id,
                    db.PerfisDeFormandos.Where(perfil => perfil.VinculoId == vinculo.Id).Select(perfil => perfil.NomeCompleto).FirstOrDefault()
                        ?? db.Users.Where(usuario => usuario.Id == vinculo.UsuarioId).Select(usuario => usuario.Nome).FirstOrDefault()
                        ?? string.Empty,
                    PedidosDeMesa().Where(pedido => pedido.VinculoId == vinculo.Id).Sum(pedido => pedido.Quantidade),
                    db.Mesas.Count(mesa => mesa.VinculoId == vinculo.Id)
                ))
                .ToListAsync(ct)
        )
            .OrderBy(comprador => comprador.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <inheritdoc />
    public Task<int> Compradas(Guid vinculoId, CancellationToken ct = default) =>
        PedidosDeMesa().Where(pedido => pedido.VinculoId == vinculoId).SumAsync(pedido => pedido.Quantidade, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Trava consultiva de transação, pelo vínculo: não há linha única do formando para travar — ele
    /// pode ter mais de um pedido de mesa, e zero mesas. Sai sozinha no fim da transação.
    /// </remarks>
    public async Task TravarDono(Guid vinculoId, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({vinculoId.ToString()}, 0))", ct);

    /// <inheritdoc />
    public Task<Mesa?> ObterParaEdicao(Guid id, CancellationToken ct = default) => db.Mesas.FirstOrDefaultAsync(mesa => mesa.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Mesa>> ListarDoDonoParaEdicao(Guid vinculoId, CancellationToken ct = default) =>
        await db.Mesas.Where(mesa => mesa.VinculoId == vinculoId).OrderByDescending(mesa => mesa.AtualizadoEm).ToListAsync(ct);

    /// <inheritdoc />
    public Task<bool> IdentificacaoEmUso(string identificacao, Guid? exceto, CancellationToken ct = default)
    {
        var padrao = Busca.Literal(identificacao);

        return db.Mesas.AnyAsync(mesa => mesa.Id != exceto && EF.Functions.ILike(mesa.Identificacao, padrao), ct);
    }

    /// <inheritdoc />
    public async Task Adicionar(Mesa mesa, CancellationToken ct = default) => await db.Mesas.AddAsync(mesa, ct);

    /// <inheritdoc />
    public void Remover(Mesa mesa) => db.Mesas.Remove(mesa);

    private IQueryable<Pedido> PedidosDeMesa() =>
        db.Pedidos.Where(pedido =>
            pedido.Status == StatusDoPedido.Confirmado
            && db.ItensDeCobranca.Any(item => item.Id == pedido.ItemDeCobrancaId && item.Tipo == TipoDeCobranca.Mesa)
        );

    /// <summary>As mesas já filtradas, com o nome do dono.</summary>
    /// <remarks>Filtro antes da projeção: o EF não traduz <c>Where</c> sobre o construtor do record.</remarks>
    /// <param name="mesas">Mesas a projetar.</param>
    private IQueryable<MesaResumo> Projetar(IQueryable<Mesa> mesas) =>
        from mesa in mesas
        join vinculo in db.Vinculos.AsNoTracking() on mesa.VinculoId equals (Guid?)vinculo.Id into vinculos
        from vinculo in vinculos.DefaultIfEmpty()
        join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id into usuarios
        from usuario in usuarios.DefaultIfEmpty()
        join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
        from perfil in perfis.DefaultIfEmpty()
        select new MesaResumo(
            mesa.Id,
            mesa.Identificacao,
            mesa.Lugares,
            mesa.Observacao,
            mesa.Reservada,
            mesa.VinculoId,
            perfil != null && perfil.NomeCompleto != null ? perfil.NomeCompleto
                : usuario != null ? usuario.Nome
                : null
        );
}
