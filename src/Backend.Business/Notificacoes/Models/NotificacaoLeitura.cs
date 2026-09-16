namespace Backend.Business.Notificacoes.Models;

/// <summary>Um degrau da régua, como a tesouraria o edita.</summary>
/// <param name="Gatilho">O que dispara. Com <paramref name="DiasDeDeslocamento"/>, é a identidade do degrau.</param>
/// <param name="DiasDeDeslocamento">Dias de distância do gatilho; negativo é antes do vencimento.</param>
/// <param name="Canal">Por onde sai.</param>
/// <param name="Assunto">Assunto, com as variáveis de <c>TemplateDeNotificacao</c>.</param>
/// <param name="Template">Corpo, com as mesmas variáveis.</param>
/// <param name="Ativa">Se dispara.</param>
/// <param name="AvisarTesouraria">Se a tesouraria recebe cópia.</param>
public sealed record DadosDaRegra(
    GatilhoDaRegua Gatilho,
    int DiasDeDeslocamento,
    CanalDeNotificacao Canal,
    string Assunto,
    string Template,
    bool Ativa,
    bool AvisarTesouraria
);

/// <summary>A régua inteira, como a tela a grava: os degraus de uma vez.</summary>
/// <param name="Regras">Degraus. Cada par <c>(gatilho, dias)</c> aparece uma vez.</param>
public sealed record DadosDaRegua(IReadOnlyList<DadosDaRegra> Regras);

/// <summary>Um degrau da régua, como a linha do tempo o desenha.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Gatilho">O que dispara.</param>
/// <param name="DiasDeDeslocamento">Dias de distância do gatilho.</param>
/// <param name="Canal">Por onde sai.</param>
/// <param name="Assunto">Assunto.</param>
/// <param name="Template">Corpo.</param>
/// <param name="Ativa">Se dispara.</param>
/// <param name="AvisarTesouraria">Se a tesouraria recebe cópia.</param>
public sealed record RegraResumo(
    Guid Id,
    GatilhoDaRegua Gatilho,
    int DiasDeDeslocamento,
    CanalDeNotificacao Canal,
    string Assunto,
    string Template,
    bool Ativa,
    bool AvisarTesouraria
);

/// <summary>Filtros do histórico de envios.</summary>
/// <param name="Canal">Só deste canal.</param>
/// <param name="Status">Só nesta situação.</param>
/// <param name="De">A partir deste dia de referência, inclusive.</param>
/// <param name="Ate">Até este dia de referência, inclusive.</param>
/// <param name="Busca">Trecho do destinatário ou do nome de quem recebeu.</param>
public sealed record FiltroDeNotificacoes(
    CanalDeNotificacao? Canal = null,
    StatusDaNotificacao? Status = null,
    DateOnly? De = null,
    DateOnly? Ate = null,
    string? Busca = null
);

/// <summary>Uma linha do histórico: quem recebeu o quê, quando, por qual canal e com qual resultado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Destinatario">Endereço que recebeu.</param>
/// <param name="Nome">Nome de quem recebeu; ausente no resumo à tesouraria.</param>
/// <param name="Assunto">Assunto enviado.</param>
/// <param name="Canal">Por onde saiu.</param>
/// <param name="Status">Resultado.</param>
/// <param name="Erro">Por que falhou, quando falhou.</param>
/// <param name="DataDeReferencia">Dia em que a régua rodou.</param>
/// <param name="EnviadaEm">Momento do disparo, em UTC.</param>
/// <param name="Gatilho">Degrau de origem.</param>
/// <param name="DiasDeDeslocamento">Deslocamento do degrau de origem.</param>
public sealed record NotificacaoNoHistorico(
    Guid Id,
    string Destinatario,
    string? Nome,
    string Assunto,
    CanalDeNotificacao Canal,
    StatusDaNotificacao Status,
    string? Erro,
    DateOnly DataDeReferencia,
    DateTime EnviadaEm,
    GatilhoDaRegua Gatilho,
    int DiasDeDeslocamento
);

/// <summary>O que o titular escolheu receber, um item por tipo opcional.</summary>
/// <param name="Tipo">Assunto.</param>
/// <param name="Ativa">Se recebe.</param>
/// <param name="Obrigatoria">Se não pode ser desligado — cobrança de parcela.</param>
public sealed record PreferenciaResumo(TipoDeNotificacao Tipo, bool Ativa, bool Obrigatoria);

/// <summary>O que o titular quer mudar.</summary>
/// <param name="Preferencias">Um item por tipo. Tipo ausente fica como está.</param>
public sealed record DadosDasPreferencias(IReadOnlyList<PreferenciaEscolhida> Preferencias);

/// <summary>A escolha do titular para um tipo.</summary>
/// <param name="Tipo">Assunto.</param>
/// <param name="Ativa">Se quer receber.</param>
public sealed record PreferenciaEscolhida(TipoDeNotificacao Tipo, bool Ativa);

/// <summary>Uma parcela que um degrau da régua alcançou hoje.</summary>
/// <remarks>Já sai do banco sem as pagas, sem as canceladas e sem as que têm informe pendente.</remarks>
/// <param name="ParcelaId">Parcela.</param>
/// <param name="VinculoId">Vínculo de quem deve.</param>
/// <param name="Nome">Nome civil, se informado no cadastro; senão, o da conta.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Vencimento">Dia do vencimento.</param>
/// <param name="ValorOriginalEmCentavos">Valor antes de multa e juros.</param>
/// <param name="Descricao">Descrição do item de origem, se houver.</param>
public sealed record ParcelaParaCobranca(
    Guid ParcelaId,
    Guid VinculoId,
    string Nome,
    string Email,
    DateOnly Vencimento,
    long ValorOriginalEmCentavos,
    string? Descricao
);

/// <summary>O que uma rodada da régua fez numa formatura.</summary>
/// <param name="Mensagens">Mensagens enfileiradas.</param>
/// <param name="Parcelas">Parcelas cobradas.</param>
/// <param name="Conferidas">Entregas atualizadas a partir da fila de e-mail.</param>
public sealed record ResumoDaRodada(int Mensagens, int Parcelas, int Conferidas)
{
    /// <summary>Nada a fazer.</summary>
    public static readonly ResumoDaRodada Nenhuma = new(0, 0, 0);

    /// <summary>Se a rodada não produziu nada digno de log.</summary>
    public bool Vazia => this is { Mensagens: 0, Parcelas: 0, Conferidas: 0 };

    /// <summary>Soma duas rodadas — a do dia e as dos dias represados pelo fim de semana.</summary>
    /// <param name="outra">Rodada a somar.</param>
    public ResumoDaRodada Mais(ResumoDaRodada outra) => new(Mensagens + outra.Mensagens, Parcelas + outra.Parcelas, Conferidas + outra.Conferidas);
}

/// <summary>Uma formatura que a régua deve percorrer.</summary>
/// <param name="Id">Formatura.</param>
/// <param name="Nome">Nome da turma, que entra na variável <c>{formatura}</c>.</param>
public sealed record FormaturaParaRegua(Guid Id, string Nome);

/// <summary>O e-mail de uma notificação, para conferir a entrega depois.</summary>
/// <param name="NotificacaoId">Notificação.</param>
/// <param name="EmailNaFilaId">E-mail correspondente na fila.</param>
public sealed record EntregaPendente(Guid NotificacaoId, Guid EmailNaFilaId);
