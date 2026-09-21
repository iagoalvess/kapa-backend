using Backend.Business.Comunicacao.Models;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Financeiro.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Os itens da festa da formatura selecionada.
/// </summary>
/// <remarks>
/// As somas de despesa de cada item vêm por subconsulta correlacionada, e não por <c>JOIN</c> com
/// agrupamento — o mesmo desenho do <c>FornecedorRepository</c> e pelo mesmo motivo: são poucas
/// dezenas de itens por turma, e assim a lista continua sendo uma projeção direta no <c>SELECT</c>,
/// numa consulta só. É isto que cumpre o critério de "sem N+1 por item".
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class ItemDaFestaRepository(AppDbContext db) : IItemDaFestaRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Na ordem da comissão, com o id de desempate: sem ele, dois itens de mesma posição — o que a
    /// semeadura e a criação manual podem produzir juntas — trocariam de lugar entre duas aberturas
    /// da tela.
    /// </remarks>
    public async Task<IReadOnlyList<ItemDaFestaResumo>> Listar(CancellationToken ct = default) =>
        await Projetar(db.ItensDaFesta.AsNoTracking().OrderBy(i => i.Ordem).ThenBy(i => i.Id)).ToListAsync(ct);

    /// <inheritdoc />
    public Task<ItemDaFestaResumo?> Obter(Guid id, CancellationToken ct = default) =>
        Projetar(db.ItensDaFesta.AsNoTracking().Where(i => i.Id == id)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<ItemDaFesta?> ObterParaEdicao(Guid id, CancellationToken ct = default) => db.ItensDaFesta.FirstOrDefaultAsync(i => i.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> ExisteAlgum(CancellationToken ct = default) => db.ItensDaFesta.AnyAsync(ct);

    /// <inheritdoc />
    /// <remarks>Turma sem item devolve zero, e o primeiro item entra na posição 1.</remarks>
    public async Task<int> UltimaOrdem(CancellationToken ct = default) =>
        await db.ItensDaFesta.AsNoTracking().Select(i => (int?)i.Ordem).MaxAsync(ct) ?? 0;

    /// <inheritdoc />
    public async Task Adicionar(IReadOnlyList<ItemDaFesta> itens, CancellationToken ct = default) => await db.ItensDaFesta.AddRangeAsync(itens, ct);

    /// <inheritdoc />
    public void Remover(ItemDaFesta item) => db.ItensDaFesta.Remove(item);

    /// <summary>
    /// O item com o nome de quem foi contratado e as somas das despesas vinculadas.
    /// </summary>
    /// <remarks>
    /// Despesa cancelada fica fora das somas e da contagem: ela não é compromisso nem gasto — é a
    /// mesma regra que o quadro por categoria do caixa já aplica. Por isso um item cujas despesas
    /// foram todas canceladas volta a ser "a contratar", que é o que de fato aconteceu com ele.
    /// <para>
    /// O fornecedor sai das despesas do item (decisão 15), e não de uma coluna do próprio item: quem
    /// contrata escolhe o fornecedor uma vez, no lançamento da despesa. Com mais de um fornecedor no
    /// mesmo item — raro, e sinal de que são dois itens —, o cartão mostra o primeiro em ordem
    /// alfabética; sem o <c>OrderBy</c>, mostraria um diferente a cada consulta.
    /// </para>
    /// <para>
    /// O contrato só é projetado quando o documento é <see cref="Visibilidade.Turma"/> (decisão 7): o
    /// service já recusa ligar um documento da comissão, e esta cláusula é a segunda barreira — sem
    /// ela, mudar a visibilidade do documento <b>depois</b> de ligado vazaria o título dele para a
    /// turma inteira.
    /// </para>
    /// </remarks>
    /// <param name="consulta">Itens já filtrados e ordenados.</param>
    private IQueryable<ItemDaFestaResumo> Projetar(IQueryable<ItemDaFesta> consulta) =>
        consulta.Select(i => new ItemDaFestaResumo(
            i.Id,
            i.Titulo,
            i.Categoria,
            i.OQueInclui,
            db.Fornecedores.Where(f =>
                    db.Despesas.Any(d => d.ItemDaFestaId == i.Id && d.FornecedorId == f.Id && d.Status != StatusDaDespesa.Cancelada)
                )
                .OrderBy(f => f.Nome)
                .Select(f => f.Nome)
                .FirstOrDefault(),
            (
                from documento in db.Documentos
                join arquivo in db.Arquivos on documento.ArquivoId equals arquivo.Id
                where documento.Id == i.DocumentoId && documento.Visibilidade == Visibilidade.Turma
                select new DocumentoDoItem(documento.Id, documento.Titulo, arquivo.Nome, arquivo.ContentType)
            ).FirstOrDefault(),
            i.Rateio,
            i.ValorPrevistoEmCentavos,
            i.QuantidadeEstimada,
            db.Despesas.Where(d => d.ItemDaFestaId == i.Id && d.Status != StatusDaDespesa.Cancelada).Sum(d => (long?)d.ValorEmCentavos) ?? 0,
            db.Despesas.Where(d => d.ItemDaFestaId == i.Id && d.Status == StatusDaDespesa.Paga).Sum(d => (long?)d.ValorEmCentavos) ?? 0,
            db.Despesas.Count(d => d.ItemDaFestaId == i.Id && d.Status != StatusDaDespesa.Cancelada),
            db.PropostasDoItem.Count(p => p.ItemDaFestaId == i.Id),
            i.CanceladoEm != null,
            i.Ordem
        ));
}
