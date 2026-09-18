using Backend.Business.Abstractions;
using Backend.Business.Financeiro.Models;

namespace Backend.Business.Financeiro.Interfaces;

/// <summary>
/// O cadastro de quem a turma contrata.
/// </summary>
/// <remarks>
/// Fornecedor com despesa lançada não é excluído (409 <c>financeiro.fornecedor_em_uso</c>): é
/// desativado. Apagar levaria junto a origem de um gasto já pago.
/// </remarks>
public interface IFornecedorService
{
    /// <summary>Uma página do cadastro, por nome.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Ativação, categoria e busca.</param>
    Task<Result<PaginaDe<FornecedorResumo>>> Listar(PaginacaoRequest paginacao, FiltroDeFornecedores filtro, CancellationToken ct = default);

    /// <summary>Quantos fornecedores a turma tem ativos e inativos — os números das pílulas.</summary>
    Task<Result<ContagemDeFornecedores>> Contar(CancellationToken ct = default);

    /// <summary>Um fornecedor da turma.</summary>
    /// <param name="id">Fornecedor.</param>
    Task<Result<FornecedorResumo>> ObterPorId(Guid id, CancellationToken ct = default);

    /// <summary>Cadastra um fornecedor.</summary>
    /// <param name="dados">Nome, documento, categoria e contato.</param>
    Task<Result<FornecedorResumo>> Criar(DadosDoFornecedor dados, CancellationToken ct = default);

    /// <summary>Altera o cadastro — inclusive a ativação.</summary>
    /// <param name="id">Fornecedor.</param>
    /// <param name="dados">Dados novos.</param>
    Task<Result<FornecedorResumo>> Atualizar(Guid id, DadosDoFornecedor dados, CancellationToken ct = default);

    /// <summary>Exclui um fornecedor sem despesa lançada; com despesa, recusa e manda desativar.</summary>
    /// <param name="id">Fornecedor.</param>
    Task<Result> Excluir(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Fornecedores da formatura selecionada.
/// </summary>
/// <remarks>Isolados pelo filtro global: nenhum método recebe a formatura.</remarks>
public interface IFornecedorRepository
{
    /// <summary>Uma página do cadastro, com o gasto de cada um.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Ativação, categoria e busca.</param>
    Task<PaginaDe<FornecedorResumo>> Listar(PaginacaoRequest paginacao, FiltroDeFornecedores filtro, CancellationToken ct = default);

    /// <summary>Quantos fornecedores a turma tem ativos e inativos, numa consulta agrupada.</summary>
    Task<ContagemDeFornecedores> Contar(CancellationToken ct = default);

    /// <summary>Um fornecedor, com o gasto dele; nulo se não existir aqui.</summary>
    /// <param name="id">Fornecedor.</param>
    Task<FornecedorResumo?> ObterResumo(Guid id, CancellationToken ct = default);

    /// <summary>O fornecedor rastreado para alteração; nulo se não existir aqui.</summary>
    /// <param name="id">Fornecedor.</param>
    Task<Fornecedor?> ObterParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>Se a turma já tem um fornecedor com este nome, sem contar o informado.</summary>
    /// <param name="nome">Nome pretendido.</param>
    /// <param name="exceto">Fornecedor que está sendo alterado.</param>
    Task<bool> ExisteComNome(string nome, Guid? exceto, CancellationToken ct = default);

    /// <summary>A categoria padrão do fornecedor; nula se ele não for da turma.</summary>
    /// <param name="id">Fornecedor.</param>
    Task<CategoriaDeDespesa?> ObterCategoria(Guid id, CancellationToken ct = default);

    /// <summary>Marca um fornecedor novo para inclusão.</summary>
    /// <param name="fornecedor">Fornecedor.</param>
    Task Adicionar(Fornecedor fornecedor, CancellationToken ct = default);

    /// <summary>Marca o fornecedor para exclusão.</summary>
    /// <param name="fornecedor">Fornecedor carregado para edição.</param>
    void Remover(Fornecedor fornecedor);
}
