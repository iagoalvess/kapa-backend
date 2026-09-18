using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Financeiro.Models;

namespace Backend.Business.Financeiro.Interfaces;

/// <summary>
/// O outro lado do caixa: o que a turma deve e o que ela já pagou.
/// </summary>
/// <remarks>
/// Pagar exige comprovante (decisão 3). Corrigir uma despesa paga é permitido à Tesouraria
/// (decisão 5, de 14/09/2026); cancelar, só a prevista.
/// </remarks>
public interface IDespesaService
{
    /// <summary>Uma página das despesas da turma, por vencimento.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Lançamento, fornecedor, categoria, situação, período e busca.</param>
    Task<Result<PaginaDe<DespesaResumo>>> Listar(PaginacaoRequest paginacao, FiltroDeDespesas filtro, CancellationToken ct = default);

    /// <summary>Quantas e quanto, por situação, dentro do mesmo filtro — a faixa da tela, numa consulta.</summary>
    /// <param name="filtro">Fornecedor, categoria, período e busca; a situação é ignorada.</param>
    Task<Result<ResumoDeDespesas>> Resumir(FiltroDeDespesas filtro, CancellationToken ct = default);

    /// <summary>Uma despesa da turma.</summary>
    /// <param name="id">Despesa.</param>
    Task<Result<DespesaResumo>> ObterPorId(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lança a despesa: uma linha à vista, ou N linhas mensais na parcelada.
    /// </summary>
    /// <remarks>
    /// Lançar a mesma despesa duas vezes seguidas (mesmo fornecedor, descrição e vencimento) devolve
    /// 409 <c>financeiro.despesa_duplicada</c> — o clique repetido não vira segundo compromisso.
    /// </remarks>
    /// <param name="dados">Fornecedor, descrição, valor total, parcelas e datas.</param>
    /// <param name="usuarioId">Quem lança — dono do comprovante, se vier.</param>
    /// <param name="comprovante">Comprovante, obrigatório quando a despesa já nasce paga.</param>
    Task<Result<IReadOnlyList<DespesaResumo>>> Lancar(NovaDespesa dados, Guid usuarioId, NovoArquivo? comprovante, CancellationToken ct = default);

    /// <summary>Corrige uma linha lançada — prevista ou paga.</summary>
    /// <param name="id">Despesa.</param>
    /// <param name="dados">Dados novos.</param>
    Task<Result<DespesaResumo>> Atualizar(Guid id, DadosDaDespesa dados, CancellationToken ct = default);

    /// <summary>Registra a saída do dinheiro, com o comprovante.</summary>
    /// <param name="id">Despesa.</param>
    /// <param name="dados">Dia do pagamento.</param>
    /// <param name="usuarioId">Quem paga — dono do comprovante.</param>
    /// <param name="comprovante">Comprovante em PDF ou imagem; obrigatório.</param>
    Task<Result<DespesaResumo>> Pagar(Guid id, PagarDespesa dados, Guid usuarioId, NovoArquivo? comprovante, CancellationToken ct = default);

    /// <summary>Cancela uma despesa prevista.</summary>
    /// <param name="id">Despesa.</param>
    /// <param name="autorId">Quem cancelou — vai na trilha com o retrato da despesa.</param>
    Task<Result<DespesaResumo>> Cancelar(Guid id, Guid autorId, CancellationToken ct = default);

    /// <summary>O comprovante de uma despesa, para a tesouraria abrir.</summary>
    /// <param name="id">Despesa.</param>
    Task<Result<ArquivoParaDownload>> BaixarComprovante(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Despesas da formatura selecionada.
/// </summary>
/// <remarks>Isoladas pelo filtro global: nenhum método recebe a formatura.</remarks>
public interface IDespesaRepository
{
    /// <summary>Uma página das despesas, por vencimento.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Lançamento, fornecedor, categoria, situação, período e busca.</param>
    /// <param name="hoje">Dia que separa prevista de atrasada.</param>
    Task<PaginaDe<DespesaResumo>> Listar(PaginacaoRequest paginacao, FiltroDeDespesas filtro, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Quantas e quanto, por situação, numa consulta agrupada.</summary>
    /// <param name="filtro">Filtro da tela; a situação é ignorada.</param>
    /// <param name="hoje">Dia que separa prevista de atrasada.</param>
    Task<IReadOnlyList<ContagemDeDespesas>> Contar(FiltroDeDespesas filtro, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Uma despesa, como a lista a mostra; nula se não existir aqui.</summary>
    /// <param name="id">Despesa.</param>
    /// <param name="hoje">Dia que separa prevista de atrasada.</param>
    Task<DespesaResumo?> Obter(Guid id, DateOnly hoje, CancellationToken ct = default);

    /// <summary>A despesa rastreada para alteração; nula se não existir aqui.</summary>
    /// <param name="id">Despesa.</param>
    Task<Despesa?> ObterParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>Se a turma já tem esta despesa (fornecedor, descrição e vencimento), cancelada de fora.</summary>
    /// <param name="fornecedorId">Fornecedor, se houver.</param>
    /// <param name="descricao">Descrição, já aparada.</param>
    /// <param name="vencimento">Vencimento da linha.</param>
    Task<bool> ExisteIgual(Guid? fornecedorId, string descricao, DateOnly vencimento, CancellationToken ct = default);

    /// <summary>Se o fornecedor tem despesa lançada — o que impede a exclusão dele.</summary>
    /// <param name="fornecedorId">Fornecedor.</param>
    Task<bool> ExisteDoFornecedor(Guid fornecedorId, CancellationToken ct = default);

    /// <summary>Se o item da festa tem despesa lançada — o que impede a exclusão dele (Sprint 17, decisão 13).</summary>
    /// <remarks>Cancelada conta: ela existiu, e apagar o item levaria junto a origem dela.</remarks>
    /// <param name="itemDaFestaId">Item da festa.</param>
    Task<bool> ExisteDoItemDaFesta(Guid itemDaFestaId, CancellationToken ct = default);

    /// <summary>O comprovante da despesa; nulo se ela não existir aqui ou não tiver.</summary>
    /// <param name="id">Despesa.</param>
    Task<ComprovanteDaDespesa?> ObterComprovante(Guid id, CancellationToken ct = default);

    /// <summary>Marca despesas novas para inclusão.</summary>
    /// <param name="despesas">Linhas do lançamento.</param>
    Task Adicionar(IReadOnlyList<Despesa> despesas, CancellationToken ct = default);
}
