namespace Backend.Business.Emails.Models;

/// <summary>
/// O mascote que abre o e-mail. O nome é o do arquivo em <c>Emails/Recursos</c>, em minúsculas.
/// </summary>
/// <remarks>
/// Enum e não string: mascote digitado errado viraria imagem quebrada no topo de oitenta mensagens,
/// e isso só aparece na caixa de quem recebeu.
/// </remarks>
public enum Mascote
{
    /// <summary>Comemorando. O padrão — serve a qualquer aviso que não seja ruim.</summary>
    Feliz,

    /// <summary>Acenando. Boas-vindas, convite, primeira mensagem.</summary>
    Acenando,

    /// <summary>De alerta, com a engrenagem quebrada. Algo deu errado e pede ação: estorno, assinatura vencida.</summary>
    /// <remarks>O desenho lê como falha: aviso de segurança usa <see cref="Cadeado"/>, e troca a conferir, <see cref="Lupa"/>.</remarks>
    Alerta,

    /// <summary>Com o cadeado. Segurança: senha, link de acesso, pedido que só o titular pode fazer.</summary>
    Cadeado,

    /// <summary>De binóculo. Procurando alguém — o lembrete de quem ainda não apareceu.</summary>
    Binoculo,

    /// <summary>Com o canudo. Formatura, adesão fechada, fim de ciclo.</summary>
    Canudo,

    /// <summary>Com o celular. Código de verificação.</summary>
    Celular,

    /// <summary>Com a lista. Pendência a cumprir.</summary>
    Checklist,

    /// <summary>Com o cofrinho. Dinheiro: parcela paga, recibo, extrato.</summary>
    Cofrinho,

    /// <summary>Com cara de erro. Pagamento recusado, falha, desligamento.</summary>
    Erro,

    /// <summary>No foguete. Turma ativada, licença confirmada.</summary>
    Foguete,

    /// <summary>Lendo um documento. Termo, relatório, exportação de dados.</summary>
    Documento,

    /// <summary>Lendo. Aviso do mural, comunicado da comissão.</summary>
    Lendo,

    /// <summary>Com a lupa. Conferência, auditoria, algo a verificar.</summary>
    Lupa,
}
