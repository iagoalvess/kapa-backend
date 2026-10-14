using Backend.Business.Abstractions;
using Backend.Business.Enquetes.Models;
using Backend.Business.Formandos.Models;
using Backend.Business.Privacidade.Models;

namespace Backend.Business.Privacidade.Interfaces;

/// <summary>Consultas internas para atender pedidos recebidos pelo suporte.</summary>
public interface IPrivacidadeRepository
{
    /// <summary>Dados cadastrais, financeiro, aceites e comunicações de todas as turmas do titular.</summary>
    Task<MeusDados?> ObterMeusDadosDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Respostas rastreadas para retirar o vínculo sem alterar o resultado da enquete.</summary>
    Task<IReadOnlyList<RespostaDaEnquete>> ListarRespostasParaAnonimizarDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Cadastros rastreados para limpeza após análise do pedido pelo suporte.</summary>
    Task<IReadOnlyList<PerfilDoFormando>> ListarPerfisParaAnonimizarDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default);
}

/// <summary>Limpa conta e cadastros, encerra o acesso e preserva os registros contratuais e financeiros.</summary>
/// <remarks>Não representa anonimização integral: documentos retidos ainda permitem identificar o titular.</remarks>
public interface IAnonimizacaoDeTitular
{
    /// <summary>Executa a limpeza cadastral aprovada pelo suporte. Repetir a operação é seguro.</summary>
    Task<Result<string>> Anonimizar(Guid usuarioId, CancellationToken ct = default);
}
