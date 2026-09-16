using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Models;
using Backend.Business.Legal.Models;

namespace Backend.Business.Adesoes.Interfaces;

/// <summary>
/// A adesão do formando ao termo — o único caminho que gera parcela — e o acompanhamento da comissão.
/// </summary>
public interface IAdesaoService
{
    /// <summary>
    /// Registra o aceite do termo vigente e gera as parcelas, na mesma transação.
    /// </summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem adere.</param>
    /// <param name="dados">Hash do conteúdo exibido.</param>
    /// <param name="origem">IP e User-Agent do aceite.</param>
    Task<Result<AdesaoDetalhe>> Aderir(Guid formaturaId, Guid usuarioId, AderirAoTermo dados, OrigemDoAceite origem, CancellationToken ct = default);

    /// <summary>
    /// Envia ao e-mail da conta o código de seis dígitos que o aceite vai pedir.
    /// </summary>
    /// <remarks>
    /// Sem termo ou sem plano não há o que aceitar, e o código não é enviado — o formando receberia
    /// um código para uma tela que ainda recusa o aceite.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem vai aderir.</param>
    Task<Result<CodigoEnviado>> SolicitarCodigo(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>A própria adesão mais recente e o que falta no cadastro para aderir.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">O próprio.</param>
    Task<Result<MinhaAdesao>> ObterMinha(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Quem aderiu e quem falta, uma página por vez.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Situação e busca.</param>
    Task<Result<PaginaDe<SituacaoDeAdesao>>> Listar(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeAdesoes filtro,
        CancellationToken ct = default
    );

    /// <summary>Quantos aderiram, de quantos, e a versão vigente.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    Task<Result<ResumoDeAdesoes>> Resumir(Guid formaturaId, CancellationToken ct = default);

    /// <summary>
    /// O termo assinado em PDF, remontado do texto da versão, do plano aceito e dos dados do aceite.
    /// </summary>
    /// <remarks>
    /// O próprio formando e a gestão baixam; qualquer outro recebe "não encontrada", como arquivo de
    /// terceiro. A gestão recebe o CPF mascarado, como no resto do sistema.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="adesaoId">Adesão.</param>
    /// <param name="solicitanteId">Quem pede.</param>
    Task<Result<PdfDaAdesao>> ObterPdf(Guid formaturaId, Guid adesaoId, Guid solicitanteId, CancellationToken ct = default);

    /// <summary>Enfileira um lembrete para um membro que ainda não aderiu.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Membro lembrado.</param>
    Task<Result> Lembrar(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);
}
