using Backend.Business.Emails.Models;

namespace Backend.Business.Notificacoes.Models;

/// <summary>A identidade de um disparo no dia: o que o índice único protege.</summary>
/// <param name="RegraId">Degrau.</param>
/// <param name="ParcelaId">Parcela, ou nulo no resumo à tesouraria.</param>
public sealed record ChaveDeEnvio(Guid RegraId, Guid? ParcelaId);

/// <summary>Uma notificação enfileirada e o desfecho do e-mail dela na fila.</summary>
/// <param name="Notificacao">Registro rastreado.</param>
/// <param name="Status">Situação do e-mail na fila.</param>
/// <param name="Erro">Última falha do e-mail, se houve.</param>
public sealed record EntregaAConferir(NotificacaoEnviada Notificacao, EEmailStatus Status, string? Erro);
