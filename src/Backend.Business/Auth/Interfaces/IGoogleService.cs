using Backend.Business.Abstractions;
using Backend.Business.Auth.Models;
using Backend.Business.Legal.Models;

namespace Backend.Business.Auth.Interfaces;

/// <summary>Entrada, cadastro e vínculo Google com comprovação da conta existente.</summary>
public interface IGoogleService
{
    /// <summary>Inicia uma tentativa curta neste navegador.</summary>
    Task<Result<InicioGoogle>> Iniciar(string navegador, string? convite, CancellationToken ct = default);

    /// <summary>Valida o token e decide o próximo passo.</summary>
    Task<Result<ResultadoGoogle>> Entrar(string tentativa, string credencial, string navegador, string? ip, CancellationToken ct = default);

    /// <summary>Conclui cadastro com aceites e sem senha.</summary>
    Task<Result<ParDeTokens>> Cadastrar(CadastroGoogle dados, string navegador, OrigemDoAceite origem, CancellationToken ct = default);

    /// <summary>Envia comprovação específica sem revelar métodos de acesso.</summary>
    Task<Result> SolicitarVinculo(string contexto, string navegador, CancellationToken ct = default);

    /// <summary>Confere o comprovante por e-mail e aplica o vínculo uma única vez.</summary>
    Task<Result<ParDeTokens>> Vincular(string contexto, string comprovante, string navegador, string? ip, CancellationToken ct = default);
}

/// <summary>Validação criptográfica isolada do transporte e das regras de conta.</summary>
public interface IValidadorGoogle
{
    /// <summary>Valida assinatura, emissor, público, prazo e nonce.</summary>
    Task<Result<IdentidadeGoogle>> Validar(string credencial, string nonce, CancellationToken ct = default);
}

/// <summary>Persistência das tentativas, sem sessão ou ID token no banco.</summary>
public interface ITentativaGoogleRepository
{
    /// <summary>Adiciona, sem persistir.</summary>
    Task Adicionar(TentativaGoogle tentativa, CancellationToken ct = default);

    /// <summary>Obtém sob FOR UPDATE na transação de quem chama.</summary>
    Task<TentativaGoogle?> Travar(string hash, CancellationToken ct = default);

    /// <summary>Serializa cadastro/vínculo por e-mail e identidade, inclusive sob concorrência.</summary>
    Task TravarIdentidade(string chave, CancellationToken ct = default);

    /// <summary>Limpeza de contextos expirados, executada somente pelo worker.</summary>
    Task<int> RemoverExpiradas(DateTime limiteUtc, CancellationToken ct = default);
}
