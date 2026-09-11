namespace Backend.Business.Abstractions;

/// <summary>
/// Fronteira transacional da aplicação.
/// </summary>
/// <remarks>
/// Repositório **não** persiste: ele monta consulta e marca mudança. Quem decide o momento do
/// commit é o service, porque só ele sabe onde termina a unidade de trabalho.
/// <para>
/// <code>
/// await _pedidoRepository.Adicionar(pedido, ct);
/// await _estoqueRepository.Baixar(itens, ct);
/// await _unitOfWork.SalvarAsync(ct);   // uma transação, tudo ou nada
/// </code>
/// </para>
/// <para>
/// Exceção conhecida: as APIs do <c>UserManager</c> do ASP.NET Identity persistem por conta
/// própria. Para compor uma escrita de Identity com outra escrita, envolva as duas em
/// <see cref="EmTransacaoAsync{T}"/>.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Persiste tudo o que foi alterado desde o último commit.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Número de registros afetados.</returns>
    Task<int> SalvarAsync(CancellationToken ct = default);

    /// <summary>
    /// Executa a operação dentro de uma transação explícita, com retry para falhas transitórias.
    /// Use quando houver mais de um <see cref="SalvarAsync"/> ou escrita fora do contexto.
    /// </summary>
    /// <typeparam name="T">Tipo devolvido pela operação.</typeparam>
    /// <param name="operacao">Trabalho a executar; recebe o token de cancelamento da tentativa.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<T> EmTransacaoAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken ct = default);
}
