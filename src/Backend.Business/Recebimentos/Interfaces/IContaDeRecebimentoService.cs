using Backend.Business.Abstractions;
using Backend.Business.Recebimentos.Models;

namespace Backend.Business.Recebimentos.Interfaces;

/// <summary>
/// A chave PIX da comissão: cadastrar, trocar, testar com R$ 1,00 e confirmar o titular.
/// </summary>
public interface IContaDeRecebimentoService
{
    /// <summary>A conta da turma, se já cadastrada.</summary>
    Task<Result<ContaDeRecebimentoDaTurma>> Obter(CancellationToken ct = default);

    /// <summary>Cadastra ou troca a chave. A conta sai sempre não conferida.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem grava.</param>
    /// <param name="dados">Chave, titular e cidade.</param>
    Task<Result<ContaDeRecebimentoDetalhe>> Gravar(Guid formaturaId, Guid usuarioId, DadosDaConta dados, CancellationToken ct = default);

    /// <summary>O copia-e-cola de R$ 1,00 para a chave gravada.</summary>
    Task<Result<PixDeTeste>> GerarPixDeTeste(CancellationToken ct = default);

    /// <summary>Registra que o banco mostrou o titular cadastrado no PIX de teste.</summary>
    /// <param name="usuarioId">Quem confirma.</param>
    Task<Result<ContaDeRecebimentoDetalhe>> Conferir(Guid usuarioId, CancellationToken ct = default);
}
