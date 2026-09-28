using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Formandos.Models;
using Backend.Business.Privacidade.Models;

namespace Backend.Business.Privacidade.Interfaces;

/// <summary>
/// O portal do titular: ver o que a Kapa guarda, exportar, revogar e pedir eliminação.
/// </summary>
/// <remarks>
/// Tudo aqui é sobre a <b>pessoa autenticada</b>, e não sobre a turma selecionada — não existe
/// método que receba <c>formaturaId</c>, e é de propósito (decisão 2 da Sprint 14). Ninguém aqui
/// consulta dado de outro titular: quem quer ver a turma tem as telas da Gestão.
/// </remarks>
public interface IPrivacidadeService
{
    /// <summary>Tudo o que a Kapa guarda sobre o titular, por seção.</summary>
    /// <param name="usuarioId">Titular.</param>
    Task<Result<MeusDados>> MeusDados(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Abre uma solicitação de exportação ou de eliminação.
    /// </summary>
    /// <remarks>
    /// Pedido igual já pendente devolve o mesmo — clique duplo não vira dois pacotes nem duas
    /// eliminações agendadas.
    /// </remarks>
    /// <param name="tipo">Exportação ou exclusão.</param>
    /// <param name="usuarioId">Titular.</param>
    /// <param name="senha">
    /// Senha da conta. Exigida <b>só</b> na exclusão: ela é irreversível, e uma sessão esquecida
    /// aberta num computador compartilhado não pode bastar para apagar a vida de alguém na turma.
    /// </param>
    Task<Result<SolicitacaoResumo>> Solicitar(TipoDeSolicitacao tipo, Guid usuarioId, string? senha, CancellationToken ct = default);

    /// <summary>As solicitações do próprio titular, da mais recente.</summary>
    /// <param name="usuarioId">Titular.</param>
    Task<Result<IReadOnlyList<SolicitacaoResumo>>> ListarSolicitacoes(Guid usuarioId, CancellationToken ct = default);

    /// <summary>O titular confirma a eliminação e ela deixa de esperar os quinze dias.</summary>
    /// <param name="id">Solicitação.</param>
    /// <param name="usuarioId">Titular.</param>
    Task<Result<SolicitacaoResumo>> Confirmar(Guid id, Guid usuarioId, CancellationToken ct = default);

    /// <summary>O titular desiste de uma solicitação ainda pendente.</summary>
    /// <param name="id">Solicitação.</param>
    /// <param name="usuarioId">Titular.</param>
    Task<Result<SolicitacaoResumo>> Cancelar(Guid id, Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// O pacote de uma exportação concluída.
    /// </summary>
    /// <remarks>
    /// Solicitação de outro titular, ainda na fila ou já expirada respondem a mesma coisa: 404 —
    /// distinguir os casos transformaria o endpoint num verificador de quem pediu exportação.
    /// </remarks>
    /// <param name="id">Solicitação.</param>
    /// <param name="usuarioId">Titular.</param>
    Task<Result<ArquivoParaDownload>> Baixar(Guid id, Guid usuarioId, CancellationToken ct = default);
}

/// <summary>
/// Leitura dos dados do titular atravessando todas as turmas, e a fila de solicitações.
/// </summary>
/// <remarks>
/// Os métodos com sufixo <c>DeTodasAsFormaturas</c> usam <c>IgnoreQueryFilters()</c>: é a saída de
/// emergência do isolamento, e ela está autorizada aqui porque o recorte é o titular, e um titular
/// atravessa turmas. Cada um deles filtra por <c>usuarioId</c> — nunca devolvem linha de terceiro.
/// </remarks>
public interface IPrivacidadeRepository
{
    /// <summary>Tudo o que a Kapa guarda sobre o titular, montado a partir de todas as turmas dele.</summary>
    /// <param name="usuarioId">Titular.</param>
    Task<MeusDados?> ObterMeusDadosDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default);

    /// <summary>As solicitações do titular, da mais recente.</summary>
    /// <param name="usuarioId">Titular.</param>
    Task<IReadOnlyList<SolicitacaoDePrivacidade>> ListarDoTitular(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Uma solicitação do titular informado, rastreada para escrita.</summary>
    /// <param name="id">Solicitação.</param>
    /// <param name="usuarioId">Titular.</param>
    Task<SolicitacaoDePrivacidade?> ObterDoTitular(Guid id, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Uma solicitação pendente do mesmo tipo, se já houver.</summary>
    /// <param name="tipo">Exportação ou exclusão.</param>
    /// <param name="usuarioId">Titular.</param>
    Task<SolicitacaoDePrivacidade?> ObterPendente(TipoDeSolicitacao tipo, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Solicitações pendentes cujo prazo já venceu, para o worker.</summary>
    /// <param name="agora">Instante de referência, em UTC.</param>
    /// <param name="quantidade">Teto do lote.</param>
    Task<IReadOnlyList<PrivacidadePendente>> ListarVencidas(DateTime agora, int quantidade, CancellationToken ct = default);

    /// <summary>Exportações concluídas cujo arquivo passou do prazo.</summary>
    /// <param name="agora">Instante de referência, em UTC.</param>
    /// <param name="quantidade">Teto do lote.</param>
    Task<IReadOnlyList<SolicitacaoDePrivacidade>> ListarArquivosVencidos(DateTime agora, int quantidade, CancellationToken ct = default);

    /// <summary>Uma solicitação qualquer, rastreada, para o worker processar.</summary>
    /// <param name="id">Solicitação.</param>
    Task<SolicitacaoDePrivacidade?> Obter(Guid id, CancellationToken ct = default);

    /// <summary>Marca a solicitação para gravação.</summary>
    /// <param name="solicitacao">Pedido novo.</param>
    Task Adicionar(SolicitacaoDePrivacidade solicitacao, CancellationToken ct = default);

    /// <summary>Nome e e-mail do titular, para os avisos.</summary>
    /// <param name="usuarioId">Titular.</param>
    Task<TitularParaAviso?> ObterTitular(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Os cadastros do titular em todas as turmas, rastreados para escrita.
    /// </summary>
    /// <remarks>Usado só pela anonimização — é o único lugar que escreve em perfil de várias turmas.</remarks>
    /// <param name="usuarioId">Titular.</param>
    Task<IReadOnlyList<PerfilDoFormando>> ListarPerfisParaAnonimizarDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Os presidentes das turmas do titular, com o que ele deve em cada uma.
    /// </summary>
    /// <remarks>
    /// É o que o aviso de eliminação precisa dizer à comissão: a dívida não some com o pedido, e
    /// quem vai atrás dela é quem tem a chave PIX.
    /// </remarks>
    /// <param name="usuarioId">Titular.</param>
    Task<IReadOnlyList<PresidenteParaAviso>> ListarPresidentesParaAvisoDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default);
}

/// <summary>
/// O lado do worker: gera o pacote da exportação e executa a eliminação vencida.
/// </summary>
/// <remarks>
/// A regra mora aqui, e não no job — o job abre o escopo, respeita o relógio e escreve o log.
/// Mesma divisão de <c>IGeracaoDeRelatoriosService</c>, e pelo mesmo motivo: isto é exercitável
/// por teste sem subir worker nenhum.
/// </remarks>
public interface IProcessamentoDePrivacidadeService
{
    /// <summary>Solicitações pendentes cujo prazo já venceu.</summary>
    /// <param name="quantidade">Teto do lote.</param>
    Task<IReadOnlyList<PrivacidadePendente>> ListarPendentes(int quantidade, CancellationToken ct = default);

    /// <summary>Processa uma solicitação: gera o pacote, ou anonimiza.</summary>
    /// <param name="solicitacaoId">Solicitação.</param>
    /// <returns>Verdadeiro se foi atendida nesta passada.</returns>
    Task<bool> Processar(Guid solicitacaoId, CancellationToken ct = default);

    /// <summary>Apaga os pacotes de exportação que passaram do prazo.</summary>
    /// <returns>Quantos foram apagados.</returns>
    Task<int> ExpirarVencidas(CancellationToken ct = default);
}

/// <summary>
/// Torna o titular irreconhecível, preservando o registro financeiro da turma.
/// </summary>
/// <remarks>
/// A eliminação da LGPD, aqui, é <b>anonimização</b> — e a tela diz isso antes de confirmar. Quem
/// pagou R$ 8.400 à turma não pode ter o lançamento apagado: a comissão presta contas com ele, e a
/// guarda fiscal o exige. O que some é o que identifica.
/// </remarks>
public interface IAnonimizacaoDeTitular
{
    /// <summary>
    /// Anonimiza conta e cadastros do titular em todas as turmas.
    /// </summary>
    /// <remarks>
    /// Idempotente: anonimizar de novo não muda nada e não falha — o worker pode repetir uma
    /// tentativa que caiu no meio.
    /// </remarks>
    /// <param name="usuarioId">Titular.</param>
    /// <returns>O marcador que passou a identificar a pessoa nos lançamentos.</returns>
    Task<Result<string>> Anonimizar(Guid usuarioId, CancellationToken ct = default);
}
