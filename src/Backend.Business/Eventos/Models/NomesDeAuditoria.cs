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

    /// <summary>Primeira gravação da conta que recebe o dinheiro da turma.</summary>
    public const string ContaCadastrada = "recebimento.conta_cadastrada";

    /// <summary>Troca da chave PIX: muda para onde o dinheiro da turma vai.</summary>
    public const string ContaAlterada = "recebimento.conta_alterada";

    /// <summary>Aviso do mural apagado — some do registro.</summary>
    public const string AvisoExcluido = "comunicacao.aviso_excluido";

    /// <summary>Arquivo do acervo trocado por outro, mantido o mesmo registro.</summary>
    public const string DocumentoSubstituido = "comunicacao.documento_substituido";

    /// <summary>Documento do acervo apagado.</summary>
    public const string DocumentoExcluido = "comunicacao.documento_excluido";

    /// <summary>Alteração de papel na comissão: muda quem pode o quê.</summary>
    public const string PapelAlterado = "membro.papel_alterado";

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

    /// <summary>Cota de convites da colação aberta ou reaberta pela Gestão (Sprint 30).</summary>
    public const string CotaAberta = "festa.cota_aberta";

    /// <summary>Entrada desfeita na portaria: alguém que já tinha entrado volta a poder entrar (Sprint 21, decisão 6).</summary>
    public const string EntradaDesfeita = "festa.entrada_desfeita";

    /// <summary>Convites de uma compra da loja cancelados pela Gestão, com motivo e estorno (Sprint 38, decisão 1).</summary>
    public const string CompraCancelada = "loja.compra_cancelada";

    /// <summary>Todas as vendas da loja canceladas de uma vez, pelo Presidente — a festa cancelada (Sprint 38, P6).</summary>
    public const string VendasDaFestaCanceladas = "loja.vendas_da_festa_canceladas";

    /// <summary>A comissão marcou a compra como devolvida, com o comprovante do PIX (Sprint 38, decisão 2).</summary>
    public const string CompraDevolvida = "loja.compra_devolvida";

    /// <summary>Pedido de cancelamento do comprador recusado pela Gestão, com motivo (Sprint 38, P1).</summary>
    public const string PedidoDeCancelamentoRecusado = "loja.pedido_de_cancelamento_recusado";

    /// <summary>Todos os nomes auditáveis.</summary>
    public static readonly IReadOnlyList<string> Todos =
    [
        PagamentoBaixado,
        PagamentoEstornado,
        ContaCadastrada,
        ContaAlterada,
        AvisoExcluido,
        DocumentoSubstituido,
        DocumentoExcluido,
        PapelAlterado,
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
        CotaAberta,
        CompraCancelada,
        VendasDaFestaCanceladas,
        CompraDevolvida,
        PedidoDeCancelamentoRecusado,
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
