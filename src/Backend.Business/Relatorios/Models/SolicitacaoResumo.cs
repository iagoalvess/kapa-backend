namespace Backend.Business.Relatorios.Models;

/// <summary>Uma solicitação, como a tela a acompanha.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Tipo">O que foi pedido.</param>
/// <param name="De">Primeiro dia do período.</param>
/// <param name="Ate">Último dia do período.</param>
/// <param name="Status">Em que pé está.</param>
/// <param name="Motivo">Por que falhou, quando falhou.</param>
/// <param name="ExpiraEm">Quando o arquivo deixa de estar disponível, em UTC.</param>
/// <param name="CriadoEm">Quando foi pedida, em UTC.</param>
public sealed record SolicitacaoResumo(
    Guid Id,
    TipoDeRelatorio Tipo,
    DateOnly De,
    DateOnly Ate,
    StatusDaSolicitacao Status,
    string? Motivo,
    DateTime? ExpiraEm,
    DateTime CriadoEm
)
{
    /// <summary>
    /// Se o download responde agora — é o que a tela usa para habilitar o botão.
    /// </summary>
    /// <remarks>
    /// Lê o relógio na montagem da resposta, que é o instante em que a pergunta faz sentido. Quem
    /// autoriza de verdade continua sendo <c>IRelatorioService.Baixar</c>, que refaz a conferência: um
    /// arquivo que expira entre a listagem e o clique responde 404, como deve.
    /// </remarks>
    public bool Disponivel => Status == StatusDaSolicitacao.Pronta && ExpiraEm > DateTime.UtcNow;
}
