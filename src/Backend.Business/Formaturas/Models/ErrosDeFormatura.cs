using Backend.Business.Abstractions;

namespace Backend.Business.Formaturas.Models;

/// <summary>
/// As falhas da formatura que outras features também devolvem.
/// </summary>
/// <remarks>
/// O código é o contrato com o cliente: declarado uma vez aqui, não tem como uma feature escrever
/// <c>formatura.nao_encontrada</c> com outra mensagem — ou com outro código — sem ninguém notar.
/// </remarks>
public static class ErrosDeFormatura
{
    /// <summary>A formatura pedida não existe.</summary>
    public static readonly Erro FormaturaNaoEncontrada = Erro.NaoEncontrado("formatura.nao_encontrada", "Formatura não encontrada.");

    /// <summary>Não há vínculo ativo com este id (ou com esta pessoa) na formatura da sessão.</summary>
    public static readonly Erro MembroNaoEncontrado = Erro.NaoEncontrado("membro.nao_encontrado", "Membro não encontrado nesta formatura.");
}
