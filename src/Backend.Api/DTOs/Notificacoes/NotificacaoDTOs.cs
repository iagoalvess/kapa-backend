using Backend.Business.Notificacoes.Models;

namespace Backend.Api.DTOs.Notificacoes;

/// <summary>Um degrau da régua, no corpo da gravação.</summary>
/// <param name="Gatilho">O que dispara. Com <paramref name="DiasDeDeslocamento"/>, é a identidade do degrau.</param>
/// <param name="DiasDeDeslocamento">Dias de distância do gatilho; negativo é antes do vencimento.</param>
/// <param name="Assunto">Assunto, com as variáveis disponíveis.</param>
/// <param name="Template">Corpo, com as mesmas variáveis.</param>
/// <param name="Ativa">Se dispara.</param>
/// <param name="AvisarTesouraria">Se a tesouraria recebe um resumo no mesmo dia.</param>
public sealed record RegraRequestDTO(
    GatilhoDaRegua Gatilho,
    int DiasDeDeslocamento,
    string? Assunto,
    string? Template,
    bool Ativa,
    bool AvisarTesouraria
);

/// <summary>A régua inteira, gravada de uma vez.</summary>
/// <param name="Regras">Degraus. Degrau que sumir do corpo é removido da régua.</param>
public sealed record ReguaRequestDTO(IReadOnlyList<RegraRequestDTO>? Regras);

/// <summary>Um degrau da régua.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Gatilho">O que dispara.</param>
/// <param name="DiasDeDeslocamento">Dias de distância do gatilho.</param>
/// <param name="Assunto">Assunto.</param>
/// <param name="Template">Corpo.</param>
/// <param name="Ativa">Se dispara.</param>
/// <param name="AvisarTesouraria">Se a tesouraria recebe um resumo.</param>
public sealed record RegraDTO(
    Guid Id,
    GatilhoDaRegua Gatilho,
    int DiasDeDeslocamento,
    string Assunto,
    string Template,
    bool Ativa,
    bool AvisarTesouraria
);

/// <summary>A régua da turma e o que o editor precisa saber para não deixar passar erro.</summary>
/// <param name="Regras">Degraus, do mais cedo ao mais tarde.</param>
/// <param name="Variaveis">As variáveis que um template aceita, sem as chaves.</param>
/// <param name="TamanhoMaximo">Teto de caracteres do corpo.</param>
/// <param name="TamanhoMaximoDoAssunto">Teto de caracteres do assunto.</param>
public sealed record ReguaDTO(IReadOnlyList<RegraDTO> Regras, IReadOnlyList<string> Variaveis, int TamanhoMaximo, int TamanhoMaximoDoAssunto);

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

/// <summary>O que o titular escolheu receber.</summary>
/// <param name="Tipo">Assunto.</param>
/// <param name="Ativa">Se recebe.</param>
/// <param name="Obrigatoria">Se não pode ser desligado — cobrança de parcela.</param>
public sealed record PreferenciaDTO(TipoDeNotificacao Tipo, bool Ativa, bool Obrigatoria);

/// <summary>A escolha do titular para um tipo.</summary>
/// <param name="Tipo">Assunto.</param>
/// <param name="Ativa">Se quer receber.</param>
public sealed record PreferenciaRequestDTO(TipoDeNotificacao Tipo, bool Ativa);

/// <summary>O corpo da gravação das preferências.</summary>
/// <param name="Preferencias">Um item por tipo. Tipo ausente fica como está.</param>
public sealed record PreferenciasRequestDTO(IReadOnlyList<PreferenciaRequestDTO>? Preferencias);
