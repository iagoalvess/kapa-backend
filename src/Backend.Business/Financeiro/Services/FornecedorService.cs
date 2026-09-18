using Backend.Business.Abstractions;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Financeiro.Services;

/// <summary>
/// O cadastro de quem a turma contrata.
/// </summary>
/// <remarks>
/// Cadastro leve (decisão 4): nome, documento, contato, categoria e observações. A categoria daqui é
/// o valor padrão do formulário de despesa — é o que faz o tesoureiro lançar "Buffet" sem escolher
/// categoria oitenta vezes.
/// </remarks>
/// <param name="fornecedorRepository">Fornecedores da turma.</param>
/// <param name="despesaRepository">Despesas, para saber se o fornecedor está em uso.</param>
/// <param name="validator">Forma do cadastro.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class FornecedorService(
    IFornecedorRepository fornecedorRepository,
    IDespesaRepository despesaRepository,
    IValidator<DadosDoFornecedor> validator,
    IUnitOfWork unitOfWork,
    ILogger<FornecedorService> logger
) : IFornecedorService
{
    private static readonly Erro NaoEncontrado = Erro.NaoEncontrado("financeiro.fornecedor_nao_encontrado", "Fornecedor não encontrado.");

    /// <inheritdoc />
    public async Task<Result<PaginaDe<FornecedorResumo>>> Listar(
        PaginacaoRequest paginacao,
        FiltroDeFornecedores filtro,
        CancellationToken ct = default
    ) => Result.Ok(await fornecedorRepository.Listar(paginacao.Normalizar(), filtro, ct));

    /// <inheritdoc />
    public async Task<Result<ContagemDeFornecedores>> Contar(CancellationToken ct = default) => Result.Ok(await fornecedorRepository.Contar(ct));

    /// <inheritdoc />
    public async Task<Result<FornecedorResumo>> ObterPorId(Guid id, CancellationToken ct = default) =>
        await fornecedorRepository.ObterResumo(id, ct) is { } fornecedor ? fornecedor : NaoEncontrado;

    /// <inheritdoc />
    public async Task<Result<FornecedorResumo>> Criar(DadosDoFornecedor dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<FornecedorResumo>(validacao.Erros);

        if (await fornecedorRepository.ExisteComNome(dados.Nome.Trim(), null, ct))
            return NomeEmUso;

        var fornecedor = Fornecedor.Novo(dados);

        await fornecedorRepository.Adicionar(fornecedor, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Fornecedor {FornecedorId} cadastrado.", fornecedor.Id);

        return await ObterPorId(fornecedor.Id, ct);
    }

    /// <inheritdoc />
    /// <remarks>A desativação entra por aqui: é o campo <c>Ativo</c> do próprio cadastro, não um endpoint à parte.</remarks>
    public async Task<Result<FornecedorResumo>> Atualizar(Guid id, DadosDoFornecedor dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<FornecedorResumo>(validacao.Erros);

        var fornecedor = await fornecedorRepository.ObterParaEdicao(id, ct);
        if (fornecedor is null)
            return NaoEncontrado;

        if (await fornecedorRepository.ExisteComNome(dados.Nome.Trim(), id, ct))
            return NomeEmUso;

        fornecedor.Aplicar(dados);
        await unitOfWork.SalvarAsync(ct);

        return await ObterPorId(id, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Com despesa lançada, 409: apagar levaria junto a origem de um gasto já pago, e a prestação de
    /// contas ficaria com lançamento órfão. O caminho é desativar.
    /// </remarks>
    public async Task<Result> Excluir(Guid id, CancellationToken ct = default)
    {
        var fornecedor = await fornecedorRepository.ObterParaEdicao(id, ct);
        if (fornecedor is null)
            return Result.Falha(NaoEncontrado);

        if (await despesaRepository.ExisteDoFornecedor(id, ct))
            return Result.Falha(
                Erro.Conflito("financeiro.fornecedor_em_uso", "Este fornecedor já tem despesa lançada. Desative o cadastro em vez de excluí-lo.")
            );

        fornecedorRepository.Remover(fornecedor);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Fornecedor {FornecedorId} excluído.", id);

        return Result.Ok();
    }

    private static Erro NomeEmUso => Erro.Conflito("financeiro.fornecedor_nome_em_uso", "A turma já tem um fornecedor com este nome.");
}
