using Backend.Business.Abstractions;
using Backend.Business.Common.Texto;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Fornecedores da formatura selecionada.
/// </summary>
/// <remarks>
/// O gasto de cada fornecedor vem por subconsulta correlacionada, e não por <c>JOIN</c> com
/// agrupamento: são dezenas de fornecedores por turma, e assim a lista continua sendo uma projeção
/// direta no <c>SELECT</c>.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class FornecedorRepository(AppDbContext db) : IFornecedorRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Por nome, ou pela coluna que a tela pediu; com o id de desempate, sem o qual dois nomes
    /// iguais trocariam de página. Colunas somadas em outra tabela ("Pago", "A pagar") não ordenam:
    /// virariam <c>ORDER BY</c> sobre subconsulta correlacionada.
    /// </remarks>
    public async Task<PaginaDe<FornecedorResumo>> Listar(PaginacaoRequest paginacao, FiltroDeFornecedores filtro, CancellationToken ct = default)
    {
        var consulta = Filtrar(filtro);

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<FornecedorResumo>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "nome" => consulta.Por(f => f.Nome, desc),
            "categoria" => consulta.Por(f => f.Categoria, desc),
            "situacao" => consulta.Por(f => f.Ativo, desc),
            _ => consulta.OrderBy(f => f.Nome),
        };

        var itens = await Projetar(ordenada.ThenBy(f => f.Id).Skip(paginacao.Pular).Take(paginacao.Tamanho)).ToListAsync(ct);

        return new PaginaDe<FornecedorResumo>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Agrupa por <c>ativo</c> e soma os dois lados na memória: são duas linhas, e assim a contagem
    /// não depende de a turma ter fornecedor inativo — um <c>GROUP BY</c> vazio não devolve zero.
    /// </remarks>
    public async Task<ContagemDeFornecedores> Contar(CancellationToken ct = default)
    {
        var grupos = await db
            .Fornecedores.AsNoTracking()
            .GroupBy(f => f.Ativo)
            .Select(grupo => new { Ativo = grupo.Key, Quantidade = grupo.Count() })
            .ToListAsync(ct);

        return new ContagemDeFornecedores(
            grupos.Where(grupo => grupo.Ativo).Sum(grupo => grupo.Quantidade),
            grupos.Where(grupo => !grupo.Ativo).Sum(grupo => grupo.Quantidade)
        );
    }

    /// <inheritdoc />
    public Task<FornecedorResumo?> ObterResumo(Guid id, CancellationToken ct = default) =>
        Projetar(db.Fornecedores.AsNoTracking().Where(f => f.Id == id)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<Fornecedor?> ObterParaEdicao(Guid id, CancellationToken ct = default) => db.Fornecedores.FirstOrDefaultAsync(f => f.Id == id, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Sem diferenciar caixa: "Buffet Sabor" e "buffet sabor" são o mesmo fornecedor para quem digita.
    /// Por <c>ILIKE</c> sem curinga — é o que o Postgres compara em caixa alta e baixa sem depender da
    /// cultura do processo; <c>%</c> e <c>_</c> do nome são escapados para não virarem curinga.
    /// </remarks>
    public Task<bool> ExisteComNome(string nome, Guid? exceto, CancellationToken ct = default)
    {
        var padrao = Busca.Literal(nome);

        return db.Fornecedores.AnyAsync(f => f.Id != exceto && EF.Functions.ILike(f.Nome, padrao), ct);
    }

    /// <inheritdoc />
    public async Task<CategoriaDeDespesa?> ObterCategoria(Guid id, CancellationToken ct = default) =>
        await db.Fornecedores.AsNoTracking().Where(f => f.Id == id).Select(f => (CategoriaDeDespesa?)f.Categoria).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task Adicionar(Fornecedor fornecedor, CancellationToken ct = default) => await db.Fornecedores.AddAsync(fornecedor, ct);

    /// <inheritdoc />
    public void Remover(Fornecedor fornecedor) => db.Fornecedores.Remove(fornecedor);

    private IQueryable<Fornecedor> Filtrar(FiltroDeFornecedores filtro)
    {
        var consulta = db.Fornecedores.AsNoTracking();

        if (filtro.Ativo is { } ativo)
            consulta = consulta.Where(f => f.Ativo == ativo);

        if (filtro.Categoria is { } categoria)
            consulta = consulta.Where(f => f.Categoria == categoria);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = Busca.Padrao(filtro.Busca);
            var digitos = FormatosBrasileiros.SomenteDigitos(filtro.Busca);

            consulta = consulta.Where(f =>
                EF.Functions.ILike(EF.Functions.Unaccent(f.Nome), termo)
                || (digitos != string.Empty && f.Documento != null && f.Documento.Contains(digitos))
            );
        }

        return consulta;
    }

    /// <summary>O fornecedor com o que já foi gasto e o que ainda vai sair para ele.</summary>
    /// <remarks>Cancelada não conta em lugar nenhum: não é compromisso nem gasto.</remarks>
    private IQueryable<FornecedorResumo> Projetar(IQueryable<Fornecedor> consulta) =>
        consulta.Select(f => new FornecedorResumo(
            f.Id,
            f.Nome,
            f.Documento,
            f.Categoria,
            f.Telefone,
            f.Email,
            f.Observacoes,
            f.Ativo,
            db.Despesas.Count(d => d.FornecedorId == f.Id && d.Status != StatusDaDespesa.Cancelada),
            db.Despesas.Where(d => d.FornecedorId == f.Id && d.Status == StatusDaDespesa.Paga).Sum(d => (long?)d.ValorEmCentavos) ?? 0,
            db.Despesas.Where(d => d.FornecedorId == f.Id && d.Status == StatusDaDespesa.Prevista).Sum(d => (long?)d.ValorEmCentavos) ?? 0
        ));
}
