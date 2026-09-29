using Backend.Business.Abstractions;
using Backend.Business.Legal.Models;
using Backend.Business.Marketing.Models;
using Backend.Business.Usuarios.Models;

namespace Backend.Business.Marketing.Interfaces;

/// <summary>
/// A preferência "Receber novidades do Kapa": ligar, desligar e sair pelo link do e-mail.
/// </summary>
public interface IComunicacaoDoKapaService
{
    /// <summary>
    /// Liga ou desliga a preferência e grava a linha do histórico.
    /// </summary>
    /// <remarks>
    /// Pedir o que já vale não grava nada: o histórico é de mudanças, e um clique duplo não vira duas linhas.
    /// Chamado dentro de uma transação, entra nela — é assim que o cadastro cria conta e aceite juntos.
    /// </remarks>
    /// <param name="usuarioId">Titular.</param>
    /// <param name="receber">O valor novo.</param>
    /// <param name="origem">De onde veio — um de <see cref="OrigemDoConsentimentoDeMarketing"/>.</param>
    /// <param name="de">IP e navegador.</param>
    Task<Result> DefinirPreferencia(Guid usuarioId, bool receber, string origem, OrigemDoAceite de, CancellationToken ct = default);

    /// <summary>
    /// Sai pelo token do e-mail, sem login.
    /// </summary>
    /// <remarks>
    /// Responde sucesso para token válido, vencido ou adulterado: distinguir os casos transformaria o endpoint
    /// anônimo num verificador de contas. A diferença vai só para o log.
    /// </remarks>
    /// <param name="token">O token do link.</param>
    /// <param name="de">IP e cliente de e-mail.</param>
    Task<Result> Descadastrar(string? token, OrigemDoAceite de, CancellationToken ct = default);
}

/// <summary>
/// As jornadas de ciclo de vida: acha quem venceu, enfileira o e-mail e registra o envio. Chamado pelo worker.
/// </summary>
public interface IJornadasDeMarketingService
{
    /// <summary>Uma rodada.</summary>
    /// <remarks>Não faz nada com o envio desligado (P9) ou fora da janela de 9h às 20h em dia útil.</remarks>
    /// <param name="agoraUtc">Momento da rodada.</param>
    /// <returns>Quantos e-mails entraram na fila.</returns>
    Task<int> Executar(DateTime agoraUtc, CancellationToken ct = default);
}

/// <summary>
/// Preferência, histórico e envios de marketing.
/// </summary>
/// <remarks>
/// O sufixo <c>DeTodasAsFormaturas</c> marca o método que atravessa turmas com <c>IgnoreQueryFilters()</c>:
/// o recorte do marketing é a pessoa, e o worker não tem turma na sessão.
/// </remarks>
public interface IComunicacaoDoKapaRepository
{
    /// <summary>A conta, rastreada para escrita.</summary>
    /// <param name="usuarioId">Titular.</param>
    Task<Usuario?> ObterUsuario(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Marca uma linha do histórico para gravação.</summary>
    /// <param name="consentimento">Aceite ou oposição.</param>
    Task Adicionar(ConsentimentoDeMarketing consentimento, CancellationToken ct = default);

    /// <summary>Marca o registro de um envio para gravação.</summary>
    /// <param name="envio">O envio.</param>
    Task Adicionar(EnvioDeMarketing envio, CancellationToken ct = default);

    /// <summary>
    /// Quem da comissão de uma turma do gratuito pode receber marketing agora, com o que a turma fez.
    /// </summary>
    /// <remarks>
    /// Só vínculo ativo de Gestão — nunca formando —, conta ativa, e-mail confirmado, preferência ligada,
    /// nenhum marketing nos últimos 14 dias; turma <c>Ativa</c>, sem assinatura paga e criada entre 3 e 30 dias
    /// atrás. Qual jornada venceu é decidido por <see cref="CandidatoDeMarketing.JornadaVencida"/>.
    /// </remarks>
    /// <param name="agoraUtc">Momento da rodada.</param>
    Task<IReadOnlyList<CandidatoDeMarketing>> ListarCandidatosDeTodasAsFormaturas(DateTime agoraUtc, CancellationToken ct = default);
}
