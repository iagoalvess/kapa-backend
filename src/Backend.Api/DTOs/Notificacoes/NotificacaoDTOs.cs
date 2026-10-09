using Backend.Business.Notificacoes.Models;

namespace Backend.Api.DTOs.Notificacoes;

/// <summary>Liga ou desliga um degrau.</summary>
/// <param name="Ativa">Se dispara.</param>
public sealed record RegraRequestDTO(bool Ativa);

/// <summary>Um degrau da régua. O texto é do Kapa e igual para toda turma.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Gatilho">O que dispara.</param>
/// <param name="DiasDeDeslocamento">Dias de distância do gatilho.</param>
/// <param name="Assunto">Assunto do e-mail.</param>
/// <param name="Ativa">Se dispara.</param>
/// <param name="AvisarTesouraria">Se a tesouraria recebe um resumo.</param>
public sealed record RegraDTO(Guid Id, GatilhoDaRegua Gatilho, int DiasDeDeslocamento, string Assunto, bool Ativa, bool AvisarTesouraria);

/// <summary>A régua da turma.</summary>
/// <param name="Regras">Degraus.</param>
public sealed record ReguaDTO(IReadOnlyList<RegraDTO> Regras);

/// <summary>Uma linha do histórico de envios.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Destinatario">Endereço que recebeu.</param>
/// <param name="Nome">Nome de quem recebeu; ausente no resumo à tesouraria.</param>
/// <param name="Assunto">Assunto enviado.</param>
/// <param name="Status">Resultado.</param>
/// <param name="Erro">Por que falhou, quando falhou.</param>
/// <param name="DataDeReferencia">Dia em que a régua rodou.</param>
/// <param name="EnviadaEm">Momento do disparo, em UTC.</param>
/// <param name="Gatilho">Degrau de origem.</param>
/// <param name="DiasDeDeslocamento">Deslocamento do degrau de origem.</param>
public sealed record NotificacaoDTO(
    Guid Id,
    string Destinatario,
    string? Nome,
    string Assunto,
    StatusDaNotificacao Status,
    string? Erro,
    DateOnly DataDeReferencia,
    DateTime EnviadaEm,
    GatilhoDaRegua Gatilho,
    int DiasDeDeslocamento
);

/// <summary>Filtros do histórico, na query string.</summary>
/// <param name="Status">Só nesta situação.</param>
/// <param name="De">A partir deste dia de referência, inclusive.</param>
/// <param name="Ate">Até este dia de referência, inclusive.</param>
/// <param name="Busca">Trecho do destinatário ou do nome de quem recebeu.</param>
public sealed record FiltroDeNotificacoesDTO(StatusDaNotificacao? Status = null, DateOnly? De = null, DateOnly? Ate = null, string? Busca = null)
{
    /// <summary>Converte para o filtro da camada de negócio.</summary>
    public FiltroDeNotificacoes ParaModelo() => new(Status, De, Ate, Busca);
}
