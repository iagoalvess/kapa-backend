namespace Backend.Business.Notificacoes.Models;

/// <summary>Um degrau da régua da turma, com o texto do catálogo.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Gatilho">O que dispara.</param>
/// <param name="DiasDeDeslocamento">Dias de distância do gatilho.</param>
/// <param name="Ativa">Se dispara.</param>
public sealed record RegraResumo(Guid Id, GatilhoDaRegua Gatilho, int DiasDeDeslocamento, bool Ativa)
{
    /// <summary>O degrau no catálogo. Só chega aqui degrau que existe nele — <c>ReguaDaTurma</c> filtra os antigos.</summary>
    public DegrauDaRegua Degrau => ReguaDoKapa.De(Gatilho, DiasDeDeslocamento)!;

    /// <summary>Assunto da mensagem.</summary>
    public string Assunto => Degrau.Texto.Assunto;

    /// <summary>Se a tesouraria recebe um resumo no mesmo dia.</summary>
    public bool AvisarTesouraria => Degrau.AvisaTesouraria;
}

/// <summary>Filtros do histórico de envios.</summary>
/// <param name="Status">Só nesta situação.</param>
/// <param name="De">A partir deste dia de referência, inclusive.</param>
/// <param name="Ate">Até este dia de referência, inclusive.</param>
/// <param name="Busca">Trecho do destinatário ou do nome de quem recebeu.</param>
public sealed record FiltroDeNotificacoes(StatusDaNotificacao? Status = null, DateOnly? De = null, DateOnly? Ate = null, string? Busca = null);

/// <summary>Uma linha do histórico: quem recebeu o quê, quando e com qual resultado.</summary>
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
public sealed record NotificacaoNoHistorico(
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

/// <summary>Uma parcela que um degrau da régua alcançou hoje.</summary>
/// <remarks>Já sai do banco sem as pagas, sem as canceladas e sem as que têm informe pendente.</remarks>
/// <param name="ParcelaId">Parcela.</param>
/// <param name="VinculoId">Vínculo de quem deve.</param>
/// <param name="Nome">Nome civil, se informado no cadastro; senão, o da conta.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Vencimento">Dia do vencimento.</param>
/// <param name="ValorOriginalEmCentavos">Valor antes de multa e juros.</param>
/// <param name="Descricao">Descrição do item de origem, se houver.</param>
/// <param name="JaPagoEmCentavos">O que já entrou por ela em pagamentos parciais: a mensagem cobra só o resto.</param>
public sealed record ParcelaParaCobranca(
    Guid ParcelaId,
    Guid VinculoId,
    string Nome,
    string Email,
    DateOnly Vencimento,
    long ValorOriginalEmCentavos,
    string? Descricao,
    long JaPagoEmCentavos = 0
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
}

/// <summary>Uma formatura que a régua deve percorrer.</summary>
/// <param name="Id">Formatura.</param>
/// <param name="Nome">Nome da turma, que entra na variável <c>{formatura}</c>.</param>
public sealed record FormaturaParaRegua(Guid Id, string Nome);
