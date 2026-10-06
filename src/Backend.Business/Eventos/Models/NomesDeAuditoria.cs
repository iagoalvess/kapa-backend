namespace Backend.Business.Eventos.Models;

/// <summary>
/// Os eventos que a trilha de auditoria da turma considera auditáveis.
/// </summary>
/// <remarks>
/// Uma lista, dois usos, e é por isso que ela existe: a tela de Auditoria oferece estes nomes como
/// filtro, e o job de retenção os poupa do prazo curto (decisão 3 da Sprint 14). Nome que entra aqui
/// passa a viver cinco anos; nome que sai some do vocabulário da tela.
/// <para>
/// Todo evento daqui é gravado por <c>IEventoRepository.Auditar</c>, na <b>mesma transação</b> da
/// operação que o descreve — e não pela fila de <c>IRegistradorDeEventos</c>, que descarta quando
/// enche. Auditoria que pode ser perdida sob carga não é auditoria; é estatística.
/// </para>
/// <para>
/// Os nomes são <b>estáveis</b>. Renomear um parte a série histórica em duas, e a resposta de "quem
/// baixou esta parcela em 2027" passa a depender de alguém lembrar dos dois nomes.
/// </para>
/// </remarks>
public static class NomesDeAuditoria
{
    /// <summary>Baixa manual de parcela — o ponto de fraude mais fácil do sistema.</summary>
    public const string PagamentoBaixado = "pagamento.baixado";

    /// <summary>Estorno de baixa: desfaz um recebimento já registrado.</summary>
    public const string PagamentoEstornado = "pagamento.estornado";

    /// <summary>Parcela cancelada à mão pela tesouraria, com justificativa (Sprint 42, decisão 8).</summary>
    public const string ParcelaCancelada = "pagamento.parcela_cancelada";

    /// <summary>A comissão devolveu um valor ao formando e anexou o comprovante: a saída entrou no caixa (Sprint 42, decisão 2).</summary>
    public const string ValorDevolvido = "pagamento.valor_devolvido";

    /// <summary>A comissão resolveu um pagamento do Mercado Pago que não tinha parcela para baixar (Sprint 42, decisão 9).</summary>
    public const string PagoSemParcelaResolvido = "pagamento.pago_sem_parcela_resolvido";

    /// <summary>Turma suspensa há um ano encerrada pela retenção, com as pendências que ficaram abertas (Sprint 42, decisão 7).</summary>
    public const string EncerradaPorAbandono = "formatura.encerrada_por_abandono";

    /// <summary>Primeira gravação da conta que recebe o dinheiro da turma.</summary>
    public const string ContaCadastrada = "recebimento.conta_cadastrada";

    /// <summary>Troca da chave PIX: muda para onde o dinheiro da turma vai.</summary>
    public const string ContaAlterada = "recebimento.conta_alterada";

    /// <summary>
    /// Pedido de troca do PIX ou da transferência, que espera o link do e-mail — fica na trilha mesmo sem confirmação,
    /// e é o rastro de quem tentou desviar o dinheiro com a conta do presidente.
    /// </summary>
    public const string TrocaDaContaPedida = "recebimento.troca_pedida";

    /// <summary>Aviso do mural apagado — some do registro.</summary>
    public const string AvisoExcluido = "comunicacao.aviso_excluido";

    /// <summary>Arquivo do acervo trocado por outro, mantido o mesmo registro.</summary>
    public const string DocumentoSubstituido = "comunicacao.documento_substituido";

    /// <summary>Documento do acervo apagado.</summary>
    public const string DocumentoExcluido = "comunicacao.documento_excluido";

    /// <summary>Alteração de papel na comissão: muda quem pode o quê.</summary>
    public const string PapelAlterado = "membro.papel_alterado";

    /// <summary>Pedido de promoção a Presidente, que espera o link do e-mail de quem pediu.</summary>
    public const string PresidentePedido = "membro.presidente_pedido";

    /// <summary>Remoção de membro: tira o acesso de alguém à turma.</summary>
    public const string MembroRemovido = "membro.removido";

    /// <summary>
    /// Saída de formando: cancela dívida e tira alguém dos números da turma.
    /// </summary>
    /// <remarks>
    /// O corpo leva o motivo, o que foi cancelado e quanto a pessoa já havia pago. É a linha que
    /// responde "por que o João saiu, e quanto sumiu da projeção" na assembleia de dois anos depois —
    /// e é a garantia contra desligar para "limpar" a inadimplência do painel.
    /// </remarks>
    public const string FormandoDesligado = "formatura.formando_desligado";

    /// <summary>Desfazer do desligamento. Histórico: o religar saiu em 23/09/2026, e o nome fica para as linhas já gravadas.</summary>
    public const string FormandoReligado = "formatura.formando_religado";

    /// <summary>Alteração de item do plano: muda valor ou vencimento do que a turma deve.</summary>
    public const string ItemAlterado = "cobranca.item_alterado";

    /// <summary>Remoção de item do plano: cancela as parcelas dele.</summary>
    public const string ItemRemovido = "cobranca.item_removido";

    /// <summary>Encerramento de item: cancela o que ainda não venceu.</summary>
    public const string ItemEncerrado = "cobranca.item_encerrado";

    /// <summary>Plano de cobrança em vigor: passa a valer para quem aderir.</summary>
    public const string PlanoVigorado = "cobranca.plano_vigorado";

    /// <summary>
    /// Pedido do formando cancelado: devolve estoque e pode lançar crédito no nome de alguém.
    /// </summary>
    /// <remarks>
    /// Entra na trilha porque a tesouraria cancela pedido dos outros, e o cancelamento de um pedido
    /// já pago grava uma parcela <b>negativa</b> — dinheiro que sai da conta de quem deve. É a
    /// pergunta "quem perdoou os R$ 360 da Ana" respondida sem depender da memória de ninguém.
    /// </remarks>
    public const string PedidoCancelado = "cobranca.pedido_cancelado";

    /// <summary>Versão nova do termo de adesão publicada.</summary>
    public const string TermoPublicado = "adesao.termo_publicado";

    /// <summary>Despesa cancelada: sai do caixa da turma.</summary>
    public const string DespesaCancelada = "financeiro.despesa_cancelada";

    /// <summary>Receita prevista cancelada: sai da projeção do caixa (Sprint 28).</summary>
    public const string OutraReceitaCancelada = "financeiro.outra_receita_cancelada";

    /// <summary>Conta bloqueada por tentativas de senha — sinal de ataque.</summary>
    /// <remarks>
    /// O único da lista que não pertence a uma turma: quem tenta entrar ainda não escolheu formatura,
    /// e o alvo é a conta. Fica fora da tela da turma e dentro da retenção longa, que é o que importa
    /// quando alguém pergunta, meses depois, desde quando as tentativas começaram.
    /// </remarks>
    public const string BloqueioPorTentativas = "auth.bloqueio_por_tentativas";

    /// <summary>Anonimização por pedido de eliminação — a prova de que o direito foi atendido.</summary>
    public const string TitularAnonimizado = "privacidade.titular_anonimizado";

    /// <summary>
    /// Licença ativada à mão pelo suporte, quando o pagamento entrou e o webhook se perdeu.
    /// </summary>
    /// <remarks>
    /// O corpo leva a <c>formaturaId</c> de propósito: é o único evento do painel que pertence a uma
    /// turma, e a comissão tem de ver na trilha dela que quem ativou foi o suporte, não o Presidente.
    /// </remarks>
    public const string SuporteAssinaturaAtivada = "suporte.assinatura_ativada";

    /// <summary>E-mail de confirmação reenviado pelo suporte.</summary>
    /// <remarks>Como o <see cref="BloqueioPorTentativas"/>, é da conta e não de turma nenhuma — sai sem <c>formaturaId</c>.</remarks>
    public const string SuporteConfirmacaoReenviada = "suporte.confirmacao_reenviada";

    /// <summary>Redefinição de senha disparada pelo suporte. Quem escolhe a senha continua sendo o dono da conta.</summary>
    public const string SuporteRedefinicaoDisparada = "suporte.redefinicao_disparada";

    /// <summary>Bloqueio por tentativas de senha levantado pelo suporte.</summary>
    public const string SuporteContaDesbloqueada = "suporte.conta_desbloqueada";

    /// <summary>Pagamento do plano estornado pelo suporte, e a assinatura encerrada (Sprint 37, P7).</summary>
    /// <remarks>Leva a <c>formaturaId</c>, como a ativação: a comissão vê na trilha dela quem devolveu e quanto.</remarks>
    public const string SuportePagamentoEstornado = "suporte.pagamento_estornado";

    /// <summary>Convite da festa emitido antes da quitação, pela Gestão, com motivo (Sprint 21, P2).</summary>
    public const string ConvitesLiberados = "festa.convites_liberados";

    /// <summary>Cortesia emitida: uma cadeira dada pela turma, com autor e motivo (Sprint 21, decisão 14).</summary>
    public const string CortesiaEmitida = "festa.cortesia_emitida";

    /// <summary>
    /// Titular de um convite alterado pela Gestão — depois do fechamento da lista, ou no convite de outra pessoa.
    /// </summary>
    /// <remarks>É o que mantém a lista impressa às 18h válida à meia-noite (Sprint 21, P5).</remarks>
    public const string ConvidadoAlterado = "festa.convidado_alterado";

    /// <summary>Código de um convite revogado e trocado por outro, pela Gestão (Sprint 21, P5).</summary>
    public const string ConviteReemitido = "festa.convite_reemitido";

    /// <summary>Convites de pacote presos por atraso soltos pela comissão (Sprint 47, D24).</summary>
    public const string ConvitesDesbloqueados = "festa.convites_desbloqueados";

    /// <summary>Entrada desfeita na portaria: alguém que já tinha entrado volta a poder entrar (Sprint 21, decisão 6).</summary>
    public const string EntradaDesfeita = "festa.entrada_desfeita";

    /// <summary>Convites de uma compra da loja cancelados pela Gestão, com motivo e estorno (Sprint 38, decisão 1).</summary>
    public const string CompraCancelada = "loja.compra_cancelada";

    /// <summary>A comissão marcou a compra como devolvida, com o comprovante do PIX (Sprint 38, decisão 2).</summary>
    public const string CompraDevolvida = "loja.compra_devolvida";

    /// <summary>Pedido de cancelamento do comprador recusado pela Gestão, com motivo (Sprint 38, P1).</summary>
    public const string PedidoDeCancelamentoRecusado = "loja.pedido_de_cancelamento_recusado";

    /// <summary>O formando pediu o cancelamento de um pacote ou pedido; as parcelas ficam suspensas (Sprint 48, D8/D12).</summary>
    public const string CancelamentoSolicitado = "cobranca.cancelamento_solicitado";

    /// <summary>A comissão aprovou a solicitação: o pacote ou o pedido caiu, e o pago foi para "a devolver" (D9).</summary>
    public const string CancelamentoAprovado = "cobranca.cancelamento_aprovado";

    /// <summary>A comissão recusou a solicitação, com motivo; a cobrança volta.</summary>
    public const string CancelamentoRecusado = "cobranca.cancelamento_recusado";

    /// <summary>
    /// A tesouraria lançou uma cobrança ou um crédito no vínculo de um formando (D23) — dinheiro que entra ou sai da
    /// conta de uma pessoa só, pela mão de alguém.
    /// </summary>
    public const string LancamentoAvulso = "cobranca.lancamento_avulso";

    /// <summary>O formando aceitou um aditivo: a cesta cresceu, com parcelas novas (D38).</summary>
    public const string AditivoAceito = "adesao.aditivo_aceito";

    /// <summary>Todos os nomes auditáveis.</summary>
    public static readonly IReadOnlyList<string> Todos =
    [
        PagamentoBaixado,
        PagamentoEstornado,
        ParcelaCancelada,
        ValorDevolvido,
        PagoSemParcelaResolvido,
        EncerradaPorAbandono,
        ContaCadastrada,
        ContaAlterada,
        TrocaDaContaPedida,
        AvisoExcluido,
        DocumentoSubstituido,
        DocumentoExcluido,
        PapelAlterado,
        PresidentePedido,
        MembroRemovido,
        FormandoDesligado,
        FormandoReligado,
        ItemAlterado,
        ItemRemovido,
        ItemEncerrado,
        PlanoVigorado,
        PedidoCancelado,
        ConvitesLiberados,
        CortesiaEmitida,
        ConvidadoAlterado,
        ConviteReemitido,
        EntradaDesfeita,
        ConvitesDesbloqueados,
        CompraCancelada,
        CompraDevolvida,
        PedidoDeCancelamentoRecusado,
        CancelamentoSolicitado,
        CancelamentoAprovado,
        CancelamentoRecusado,
        LancamentoAvulso,
        AditivoAceito,
        TermoPublicado,
        DespesaCancelada,
        OutraReceitaCancelada,
        BloqueioPorTentativas,
        TitularAnonimizado,
        SuporteAssinaturaAtivada,
        SuporteConfirmacaoReenviada,
        SuporteRedefinicaoDisparada,
        SuporteContaDesbloqueada,
        SuportePagamentoEstornado,
    ];
}
