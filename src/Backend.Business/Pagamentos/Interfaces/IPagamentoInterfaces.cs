using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Pagamentos.Interfaces;

/// <summary>
/// O caminho do dinheiro da turma: o formando vê o extrato, paga pelo PIX e avisa; a tesouraria confere
/// em lote, recusa ou baixa à mão; o Presidente estorna.
/// </summary>
/// <remarks>
/// "Dono da parcela" é regra daqui, não da política: a política garante que é membro da turma, e o
/// service que a parcela é dele. Parcela de outro responde 404, não 403 — 403 confirmaria que existe.
/// </remarks>
public interface IPagamentoService
{
    /// <summary>O extrato do próprio formando.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    Task<Result<ExtratoDoFormando>> ObterExtrato(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Quantas parcelas próprias venceram sem o formando ter avisado o pagamento.</summary>
    /// <remarks>
    /// O selo do menu, que está em toda tela: por isso é um número, e não o extrato inteiro filtrado
    /// por quem chama.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    Task<Result<int>> ContarVencidasSemAviso(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Uma parcela, com o valor do dia. O dono, ou a gestão.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    /// <param name="parcelaId">Parcela.</param>
    Task<Result<ParcelaResumo>> ObterParcela(Guid formaturaId, Guid usuarioId, Guid parcelaId, CancellationToken ct = default);

    /// <summary>O PIX da parcela com a chave vigente e o valor do dia. O dono, ou a tesouraria.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    /// <param name="parcelaId">Parcela.</param>
    Task<Result<PixDaParcela>> GerarPix(Guid formaturaId, Guid usuarioId, Guid parcelaId, CancellationToken ct = default);

    /// <summary>
    /// O PIX de várias parcelas de uma vez: um BR Code com a soma do que elas cobram hoje. Só o dono.
    /// </summary>
    /// <remarks>
    /// O passo que faltava no "paguei vários meses de uma vez": até 17/09/2026 o formando só podia
    /// avisar o lote, e tinha que somar as parcelas e montar o PIX por fora. As parcelas passam pelas
    /// mesmas conferências de <see cref="Informar"/>, e a distribuição continua sendo dela — este
    /// método não grava nada.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    /// <param name="parcelaIds">Parcelas que o pagamento vai cobrir, ao menos uma.</param>
    Task<Result<PixDaParcela>> GerarPixDeVarias(Guid formaturaId, Guid usuarioId, IReadOnlyList<Guid> parcelaIds, CancellationToken ct = default);

    /// <summary>
    /// O "já paguei": grava os informes pendentes e não muda parcela nenhuma. Só o dono.
    /// </summary>
    /// <remarks>
    /// Várias parcelas porque um PIX só costuma cobrir vários meses — é o formando atrasado se
    /// acertando, e obrigá-lo a rachar o valor em três avisos é atrito no pior momento possível
    /// (revisão de 17/09/2026). O valor informado é distribuído da parcela mais antiga para a mais
    /// nova, cada uma até o que ela cobra; o que sobrar fica no último aviso, e a tesouraria o vê em
    /// Divergências. Um comprovante só, compartilhado — é um pagamento só.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem informa.</param>
    /// <param name="parcelaIds">Parcelas cobertas pelo pagamento, ao menos uma.</param>
    /// <param name="dados">Dia e valor total do pagamento.</param>
    /// <param name="comprovante">Comprovante, se enviado.</param>
    Task<Result<IReadOnlyList<ParcelaResumo>>> Informar(
        Guid formaturaId,
        Guid usuarioId,
        IReadOnlyList<Guid> parcelaIds,
        NovoInforme dados,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    );

    /// <summary>A fila da tesouraria: os pendentes do mais antigo ao mais novo; os conferidos, dos mais recentes.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Situação, período do pagamento informado e busca.</param>
    Task<Result<PaginaDe<InformeNaFila>>> ListarInformes(PaginacaoRequest paginacao, FiltroDeInformes filtro, CancellationToken ct = default);

    /// <summary>O comprovante de um informe, para a tesouraria abrir.</summary>
    /// <param name="informeId">Informe.</param>
    Task<Result<ArquivoParaDownload>> BaixarComprovante(Guid informeId, CancellationToken ct = default);

    /// <summary>Confirma o lote numa transação: cada informe baixa a parcela com o valor recebido.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem confirma.</param>
    /// <param name="enderecoIp">De onde confirma.</param>
    /// <param name="lote">Informes e valores recebidos.</param>
    Task<Result<ResultadoDaConferencia>> Confirmar(
        Guid formaturaId,
        Guid usuarioId,
        string? enderecoIp,
        ConfirmarInformes lote,
        CancellationToken ct = default
    );

    /// <summary>Recusa um informe, com motivo, e avisa o formando. A parcela continua aberta.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem recusa.</param>
    /// <param name="informeId">Informe.</param>
    /// <param name="dados">Motivo.</param>
    Task<Result> Recusar(Guid formaturaId, Guid usuarioId, Guid informeId, RecusarInforme dados, CancellationToken ct = default);

    /// <summary>Baixa uma parcela sem informe — dinheiro, TED, quem pagou e não avisou.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem baixa.</param>
    /// <param name="enderecoIp">De onde baixa.</param>
    /// <param name="parcelaId">Parcela.</param>
    /// <param name="dados">Forma, dia e valor.</param>
    /// <param name="comprovante">Comprovante, se enviado.</param>
    Task<Result<ParcelaResumo>> BaixarManualmente(
        Guid formaturaId,
        Guid usuarioId,
        string? enderecoIp,
        Guid parcelaId,
        BaixaManual dados,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    );

    /// <summary>Desfaz a baixa, com justificativa. A parcela volta a ser devida; o recebimento fica, estornado.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Presidente que estorna.</param>
    /// <param name="enderecoIp">De onde estorna.</param>
    /// <param name="parcelaId">Parcela.</param>
    /// <param name="dados">Justificativa.</param>
    Task<Result<ParcelaResumo>> Estornar(
        Guid formaturaId,
        Guid usuarioId,
        string? enderecoIp,
        Guid parcelaId,
        EstornarBaixa dados,
        CancellationToken ct = default
    );

    /// <summary>As baixas com valor recebido diferente do devido, das mais recentes.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="busca">Trecho do nome do formando.</param>
    Task<Result<PaginaDe<Divergencia>>> ListarDivergencias(PaginacaoRequest paginacao, string? busca = null, CancellationToken ct = default);
}

/// <summary>
/// Informes de pagamento da formatura selecionada.
/// </summary>
/// <remarks>Isolados pelo filtro global: nenhum método recebe a formatura.</remarks>
public interface IInformeRepository
{
    /// <summary>A parcela de cada informe — para travar as parcelas antes de ler os informes.</summary>
    /// <param name="informeIds">Informes.</param>
    Task<IReadOnlyList<Guid>> ListarParcelas(IReadOnlyCollection<Guid> informeIds, CancellationToken ct = default);

    /// <summary>Os informes, rastreados para alteração.</summary>
    /// <param name="informeIds">Informes.</param>
    Task<IReadOnlyList<InformeDePagamento>> ListarParaEdicao(IReadOnlyCollection<Guid> informeIds, CancellationToken ct = default);

    /// <summary>Uma página da fila, com a parcela de cada informe, sem o devido.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Situação, período do pagamento informado e busca.</param>
    /// <param name="hoje">Dia que separa aberta de vencida.</param>
    /// <param name="conferidosDesdeUtc">Só os conferidos a partir deste instante; nulo traz todos.</param>
    Task<PaginaDe<InformeNaFila>> Listar(
        PaginacaoRequest paginacao,
        FiltroDeInformes filtro,
        DateOnly hoje,
        DateTime? conferidosDesdeUtc = null,
        CancellationToken ct = default
    );

    /// <summary>O comprovante do informe; nulo se o informe não existir aqui ou não tiver.</summary>
    /// <param name="informeId">Informe.</param>
    Task<ComprovanteDoInforme?> ObterComprovante(Guid informeId, CancellationToken ct = default);

    /// <summary>Marca um informe novo para inclusão.</summary>
    /// <param name="informe">Informe.</param>
    Task Adicionar(InformeDePagamento informe, CancellationToken ct = default);
}

/// <summary>
/// Recebimentos — as entradas no caixa — da formatura selecionada.
/// </summary>
/// <remarks>Isolados pelo filtro global. Não há método para remover: o estorno marca, não apaga.</remarks>
public interface IRecebimentoRepository
{
    /// <summary>A <b>última</b> baixa que vale para a parcela, rastreada; nula se ela não tem baixa ativa.</summary>
    /// <remarks>
    /// A mais recente porque desde 17/09/2026 uma parcela pode ter várias: o pagamento parcial não a
    /// fecha, e estornar é desfazer a última entrada, não todas.
    /// </remarks>
    /// <param name="parcelaId">Parcela.</param>
    Task<Recebimento?> ObterAtivoParaEdicao(Guid parcelaId, CancellationToken ct = default);

    /// <summary>Uma página das baixas ativas com recebido diferente do devido, das mais recentes.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="hoje">Dia que separa aberta de vencida.</param>
    /// <param name="busca">Trecho do nome do formando.</param>
    Task<PaginaDe<Divergencia>> ListarDivergencias(PaginacaoRequest paginacao, DateOnly hoje, string? busca = null, CancellationToken ct = default);

    /// <summary>Marca um recebimento novo para inclusão.</summary>
    /// <param name="recebimento">Recebimento.</param>
    Task Adicionar(Recebimento recebimento, CancellationToken ct = default);
}
