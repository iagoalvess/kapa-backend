using Backend.Business.Abstractions;
using Backend.Business.Agenda.Models;
using Backend.Business.Arquivos.Models;
using Backend.Business.Festa.Models;

namespace Backend.Business.Festa.Interfaces;

/// <summary>
/// O convite da festa do lado de quem o tem: ver, nomear o convidado, abrir e imprimir — e o que a
/// Gestão emite à mão.
/// </summary>
/// <remarks>
/// A emissão comum não passa por aqui: ela nasce da quitação do pedido, em <c>PedidoService</c>, na
/// transação da baixa (P2). O que está aqui são as exceções — a liberação manual e a cortesia —, que
/// exigem motivo e ficam na auditoria com o autor.
/// </remarks>
public interface IConviteDoEventoService
{
    /// <summary>Os convites do próprio formando para a festa ou a colação, e as unidades pagas que ainda faltam.</summary>
    /// <param name="formaturaId">Turma da sessão.</param>
    /// <param name="usuarioId">O próprio.</param>
    /// <param name="tipo">Festa (os comprados) ou colação (os da cota).</param>
    Task<Result<MeusConvites>> ListarMeus(Guid formaturaId, Guid usuarioId, TipoDeEvento tipo, CancellationToken ct = default);

    /// <summary>
    /// A página pública do convite, sem sessão (decisão 11).
    /// </summary>
    /// <remarks>
    /// Assinatura errada responde 404 sem consultar o banco (decisão 5); código inexistente e
    /// convite revogado respondem o <b>mesmo</b> 404 — a página pública não diz se o código existe.
    /// </remarks>
    /// <param name="token">Código com a assinatura.</param>
    Task<Result<ConvitePublico>> AbrirPublico(string token, CancellationToken ct = default);

    /// <summary>O convite em PDF, com o QR desenhado como vetor (decisões 9 e 10).</summary>
    /// <param name="token">Código com a assinatura.</param>
    Task<Result<ArquivoParaDownload>> GerarPdf(string token, CancellationToken ct = default);

    /// <summary>
    /// Nomeia o convidado — ou troca, que é como se transfere um convite (decisão 17).
    /// </summary>
    /// <remarks>
    /// O dono edita até o fechamento da lista, 24 h antes da festa; depois, só a Gestão, e cada
    /// alteração dela vira auditoria (P5). Trocar o titular de um convite já nomeado revoga o
    /// código antigo e devolve o convite com código novo.
    /// </remarks>
    /// <param name="conviteId">Convite.</param>
    /// <param name="formaturaId">Turma da sessão.</param>
    /// <param name="usuarioId">Quem edita.</param>
    /// <param name="dados">Nome, documento e e-mail.</param>
    Task<Result<MeuConvite>> NomearConvidado(
        Guid conviteId,
        Guid formaturaId,
        Guid usuarioId,
        DadosDoConvidado dados,
        CancellationToken ct = default
    );

    /// <summary>Nomeia ou transfere um convite comprado na loja — o comprador, pelo link da compra (Sprint 26, decisão 10).</summary>
    /// <param name="conviteId">Convite.</param>
    /// <param name="compraId">A compra do link; convite de outra compra responde 404.</param>
    /// <param name="dados">Nome, documento e e-mail.</param>
    Task<Result<MeuConvite>> NomearDaCompra(Guid conviteId, Guid compraId, DadosDoConvidado dados, CancellationToken ct = default);

    /// <summary>Revoga o código e emite outro para o mesmo convidado — o "perdi o convite" (P5).</summary>
    /// <param name="conviteId">Convite.</param>
    /// <param name="usuarioId">Quem reemite, da Gestão.</param>
    Task<Result<ConviteNaPortaria>> Reemitir(Guid conviteId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Emite os convites de um pedido ainda em aberto: "pagou 2 de 3, paga o resto na porta" (P2).
    /// </summary>
    /// <param name="usuarioId">Quem libera, da Gestão.</param>
    /// <param name="dados">Pedido e motivo.</param>
    /// <returns>Quantos convites valem para o pedido depois da liberação.</returns>
    Task<Result<int>> Liberar(Guid usuarioId, LiberacaoDeConvites dados, CancellationToken ct = default);

    /// <summary>
    /// Emite um convite da turma, sem dono e sem pedido (decisão 14).
    /// </summary>
    /// <remarks>
    /// Consome o estoque do item de convite extra quando a festa tem um à venda, sob a mesma trava
    /// da Sprint 20: cortesia que não desconta de nada é como se descobre na porta que faltou cadeira.
    /// Na colação não há estoque: a cortesia entra na conta da capacidade do painel da cota (Sprint 30, P3).
    /// </remarks>
    /// <param name="usuarioId">Quem emite, da Gestão.</param>
    /// <param name="dados">Convidado, motivo e evento.</param>
    Task<Result<ConviteNaPortaria>> EmitirCortesia(Guid usuarioId, DadosDaCortesia dados, CancellationToken ct = default);

    /// <summary>
    /// Emite os convites de todo pedido já quitado que ainda não os tem.
    /// </summary>
    /// <remarks>
    /// A emissão da quitação não derruba a baixa quando a agenda está incompleta — ela espera. Este é
    /// o botão que a Gestão aperta depois de completar a festa na agenda, e é idempotente: apertar de
    /// novo não emite nada (decisão 12).
    /// </remarks>
    /// <returns>Quantos pedidos receberam convite.</returns>
    Task<Result<int>> EmitirPendentes(CancellationToken ct = default);

    /// <summary>A situação dos convites da festa, para a Gestão e para o aviso da agenda.</summary>
    Task<Result<ResumoDosConvites>> Resumir(CancellationToken ct = default);
}

/// <summary>
/// A cota de convites da colação: cada formando ativo recebe N convites, sem pedido e sem dinheiro (Sprint 30).
/// </summary>
/// <remarks>
/// Da Gestão. Um número por evento e a Sprint 21 faz o resto (decisões 1 e 2): o convite de cota é o
/// mesmo convite, com <c>PedidoId</c> nulo.
/// </remarks>
public interface ICotaDoEventoService
{
    /// <summary>O painel da cota da colação: o número, a capacidade e a conta aberta.</summary>
    Task<Result<PainelDaCota>> Obter(CancellationToken ct = default);

    /// <summary>
    /// Grava a cota e a capacidade — passar da capacidade avisa no painel, mas salva (decisão 3).
    /// </summary>
    /// <remarks>Depois de aberta, a cota só sobe: 409 <c>festa.cota_ja_aberta</c>.</remarks>
    /// <param name="dados">Cota por formando e capacidade.</param>
    Task<Result<PainelDaCota>> Definir(DadosDaCota dados, CancellationToken ct = default);

    /// <summary>
    /// Abre — ou reabre — a cota: emite o que falta a cada formando ativo, numa instrução (P1).
    /// </summary>
    /// <remarks>
    /// Reabrir não duplica: quem já tem a cota não ganha nada, e quem entrou depois recebe a dele. Sem
    /// cota, 409 <c>festa.cota_nao_configurada</c>; sem hora e local, 409 <c>festa.evento_incompleto</c>.
    /// </remarks>
    /// <param name="usuarioId">Quem abre, para a auditoria.</param>
    Task<Result<PainelDaCota>> Abrir(Guid usuarioId, CancellationToken ct = default);
}

/// <summary>
/// A porta: a lista, a busca, a validação de entrada e a sincronização da lista sem rede.
/// </summary>
/// <remarks>
/// De qualquer membro da Gestão (P4). O convite de outra turma responde 404, nunca 403 — o filtro
/// global da formatura não o enxerga (decisão 15).
/// </remarks>
public interface IPortariaService
{
    /// <summary>A lista do evento, com a faixa de contagem.</summary>
    /// <param name="eventoId">Evento aberto na portaria; nulo é o evento único do tipo.</param>
    /// <param name="tipo">Festa ou colação, quando o evento não vem pelo id.</param>
    /// <param name="busca">Trecho do nome do convidado, de quem o convidou ou do código.</param>
    Task<Result<ListaDaPortaria>> Listar(Guid? eventoId, TipoDeEvento tipo, string? busca, CancellationToken ct = default);

    /// <summary>A lista em PDF, com o documento inteiro — a que o salão pede (P5 e P5.1).</summary>
    /// <param name="eventoId">Evento; nulo é o evento único do tipo.</param>
    /// <param name="tipo">Festa ou colação, quando o evento não vem pelo id.</param>
    Task<Result<ArquivoParaDownload>> ListaEmPdf(Guid? eventoId, TipoDeEvento tipo, CancellationToken ct = default);

    /// <summary>Um convite como a portaria o vê: situação, entrada e se a janela está aberta.</summary>
    /// <param name="codigo">Código digitado ou token lido do QR.</param>
    Task<Result<ConsultaNaPortaria>> Consultar(string codigo, CancellationToken ct = default);

    /// <summary>
    /// Valida a entrada — uma instrução só, com todas as garantias dentro (decisão 15).
    /// </summary>
    /// <remarks>
    /// A segunda leitura do mesmo convite responde 409 <c>festa.ja_validado</c> com quem validou e
    /// quando, no corpo: a portaria precisa da informação, não de um erro (decisão 6).
    /// </remarks>
    /// <param name="codigo">Código digitado ou token lido do QR.</param>
    /// <param name="eventoId">Evento aberto na portaria; nulo aceita o do próprio convite.</param>
    /// <param name="usuarioId">Quem valida.</param>
    Task<Result<EntradaNaPortaria>> ValidarEntrada(string codigo, Guid? eventoId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Desfaz uma entrada validada por engano. A linha fica, marcada como desfeita (decisão 6).</summary>
    /// <param name="checkInId">Entrada.</param>
    /// <param name="usuarioId">Quem desfaz.</param>
    Task<Result> Desfazer(Guid checkInId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Sobe as entradas marcadas sem rede — e nenhuma é descartada (decisão 16).
    /// </summary>
    /// <param name="entradas">O que o aparelho marcou.</param>
    /// <param name="usuarioId">Quem sincroniza.</param>
    Task<Result<ResultadoDaSincronizacao>> Sincronizar(IReadOnlyList<EntradaSemRede> entradas, Guid usuarioId, CancellationToken ct = default);
}

/// <summary>
/// Os convites da formatura selecionada, e as entradas da portaria.
/// </summary>
/// <remarks>
/// Isolados pelo filtro global, menos o que tem o sufixo <c>DeTodasAsFormaturas</c>: a página
/// pública do convite, que não tem sessão, e a limpeza do worker.
/// </remarks>
public interface IConviteDoEventoRepository
{
    /// <summary>
    /// Emite os convites 1 a N de um pedido — idempotente, pelo índice único (decisão 12).
    /// </summary>
    /// <remarks>
    /// <c>INSERT … SELECT … FROM generate_series … ON CONFLICT DO NOTHING</c>, direto no banco e na
    /// transação de quem chama. Rodar dez vezes produz os mesmos N convites. O sorteio de código que
    /// colide com outro também cai no <c>ON CONFLICT</c>, e a posição que ficou sem convite é
    /// sorteada de novo.
    /// <para>
    /// A posição que volta a valer depois de um estorno herda o titular do último convite revogado
    /// nela: quem pagou de novo não precisa digitar os nomes outra vez.
    /// </para>
    /// </remarks>
    /// <param name="eventoId">Evento.</param>
    /// <param name="vinculoId">Dono.</param>
    /// <param name="pedidoId">Pedido que paga pelos convites.</param>
    /// <param name="quantidade">N.</param>
    /// <param name="prefixo">Prefixo da turma, para os códigos.</param>
    /// <returns>Quantos convites do pedido valem depois da emissão.</returns>
    Task<int> EmitirDoPedido(Guid eventoId, Guid vinculoId, Guid pedidoId, int quantidade, string prefixo, CancellationToken ct = default);

    /// <summary>
    /// Emite os convites 1 a N de uma compra da loja — idempotente, pelo índice único (Sprint 26, decisão 9).
    /// </summary>
    /// <remarks>
    /// A mesma instrução de <see cref="EmitirDoPedido"/>, sem dono: webhook repetido, conciliação e
    /// confirmação tardia emitem os mesmos N convites.
    /// </remarks>
    /// <param name="eventoId">A festa.</param>
    /// <param name="compraId">A compra.</param>
    /// <param name="quantidade">N.</param>
    /// <param name="prefixo">Prefixo da turma.</param>
    /// <returns>Quantos convites da compra valem depois da emissão.</returns>
    Task<int> EmitirDaCompra(Guid eventoId, Guid compraId, int quantidade, string prefixo, CancellationToken ct = default);

    /// <summary>Os convites válidos de uma compra, por posição.</summary>
    /// <param name="compraId">A compra.</param>
    Task<IReadOnlyList<ConviteDoVinculo>> ListarDaCompra(Guid compraId, CancellationToken ct = default);

    /// <summary>
    /// Emite a cota que falta a cada vínculo ativo, em todo evento com a cota aberta — idempotente, pelo
    /// índice único (Sprint 30, P1).
    /// </summary>
    /// <remarks>
    /// Uma instrução para a turma inteira: <c>INSERT … SELECT</c> dos vínculos ativos cruzados com
    /// <c>generate_series(1, cota)</c>, <c>ON CONFLICT DO NOTHING</c>. Quem já tem a cota não ganha nada;
    /// quem entrou depois recebe a dele. Leva a formatura explícita porque a entrada na turma chama isto
    /// antes de a sessão ser dela.
    /// </remarks>
    /// <param name="formaturaId">Turma.</param>
    /// <param name="vinculoId">Só este vínculo — a entrada na turma; nulo é a turma inteira.</param>
    /// <param name="prefixo">Prefixo da turma, para os códigos.</param>
    /// <returns>Quantos convites nasceram agora.</returns>
    Task<int> EmitirDaCota(Guid formaturaId, Guid? vinculoId, string prefixo, CancellationToken ct = default);

    /// <summary>Os convites de cota válidos do vínculo, travados até o fim da transação — para revogar na saída.</summary>
    /// <param name="vinculoId">Dono.</param>
    Task<IReadOnlyList<ConviteDoEvento>> TravarDaCota(Guid vinculoId, CancellationToken ct = default);

    /// <summary>A conta do painel da cota: formandos ativos, convites de cota e cortesias do evento.</summary>
    /// <param name="eventoId">Evento.</param>
    Task<ContagemDaCota> ContarDaCota(Guid eventoId, CancellationToken ct = default);

    /// <summary>Os convites válidos de um pedido, travados até o fim da transação (decisão 15).</summary>
    /// <param name="pedidoId">Pedido.</param>
    Task<IReadOnlyList<ConviteDoEvento>> TravarDoPedido(Guid pedidoId, CancellationToken ct = default);

    /// <summary>Um convite travado até o fim da transação; nulo se não existir aqui.</summary>
    /// <param name="conviteId">Convite.</param>
    Task<ConviteDoEvento?> Travar(Guid conviteId, CancellationToken ct = default);

    /// <summary>Um convite válido pelo código, de qualquer turma — só para a página pública.</summary>
    /// <param name="codigo">Código.</param>
    Task<ConvitePublicoGravado?> ObterPublicoDeTodasAsFormaturas(string codigo, CancellationToken ct = default);

    /// <summary>Os convites válidos do vínculo para o evento, por posição.</summary>
    /// <param name="vinculoId">Dono.</param>
    /// <param name="eventoId">Evento.</param>
    Task<IReadOnlyList<ConviteDoVinculo>> ListarDoVinculo(Guid vinculoId, Guid eventoId, CancellationToken ct = default);

    /// <summary>Os convites do evento, como a portaria os lista — válidos, pendentes, validados e revogados.</summary>
    /// <param name="eventoId">Evento.</param>
    /// <param name="busca">Trecho do nome do convidado, do dono ou do código.</param>
    Task<IReadOnlyList<ConviteGravadoNaPortaria>> ListarNaPortaria(Guid eventoId, string? busca, CancellationToken ct = default);

    /// <summary>
    /// Um convite pelo código, como a portaria o lê; nulo se não existir nesta turma.
    /// </summary>
    /// <remarks>
    /// Com mais de um convite no mesmo código não há como existir — o índice é único e global. O
    /// revogado também volta: a portaria o mostra em vermelho, com o motivo.
    /// </remarks>
    /// <param name="codigo">Código.</param>
    Task<ConviteGravadoNaPortaria?> ObterNaPortaria(string codigo, CancellationToken ct = default);

    /// <summary>
    /// Grava a entrada, se o convite vale, é do evento e ainda não entrou — numa instrução só.
    /// </summary>
    /// <remarks>
    /// <c>INSERT … SELECT … WHERE revogado_em IS NULL AND evento_id = … FOR SHARE ON CONFLICT DO
    /// NOTHING</c> (decisão 15): conferir e gravar não são duas leituras, então o estorno do mesmo
    /// segundo ou entra antes (e aqui não grava) ou entra depois (e aparece como revogado após a
    /// entrada). Zero linhas quer dizer "já validado", "revogado" ou "outro evento"; quem chama lê o
    /// convite para dizer qual.
    /// </remarks>
    /// <param name="conviteId">Convite.</param>
    /// <param name="eventoId">Evento da portaria.</param>
    /// <param name="usuarioId">Quem valida.</param>
    /// <param name="validadoEm">Quando entrou, em UTC.</param>
    /// <param name="aparelho">Aparelho de origem, na sincronização sem rede.</param>
    /// <returns>Se gravou.</returns>
    Task<bool> RegistrarEntrada(Guid conviteId, Guid eventoId, Guid usuarioId, DateTime validadoEm, string? aparelho, CancellationToken ct = default);

    /// <summary>A entrada ativa do convite, com o nome de quem validou; nula se ele ainda não entrou.</summary>
    /// <param name="conviteId">Convite.</param>
    Task<EntradaNaPortaria?> ObterEntradaAtiva(Guid conviteId, CancellationToken ct = default);

    /// <summary>Uma entrada rastreada para desfazer; nula se não existir aqui.</summary>
    /// <param name="checkInId">Entrada.</param>
    Task<CheckIn?> ObterCheckInParaEdicao(Guid checkInId, CancellationToken ct = default);

    /// <summary>Quantos convites válidos o evento tem, e quantos estão sem titular.</summary>
    /// <param name="eventoId">Evento.</param>
    Task<(int Emitidos, int SemTitular)> Contar(Guid eventoId, CancellationToken ct = default);

    /// <summary>Marca um convite novo — cortesia, transferência ou reemissão — para inclusão.</summary>
    /// <param name="convite">Convite.</param>
    Task Adicionar(ConviteDoEvento convite, CancellationToken ct = default);

    /// <summary>Marca uma entrada nova para inclusão — a tentativa repetida sem rede.</summary>
    /// <param name="checkIn">Entrada.</param>
    Task Adicionar(CheckIn checkIn, CancellationToken ct = default);

    /// <summary>
    /// Apaga documento e e-mail dos convidados de eventos que terminaram antes do dia informado (P5.1).
    /// </summary>
    /// <remarks>
    /// <c>ExecuteUpdateAsync</c> atravessando as turmas: limpeza do worker, sem nada a compor — a
    /// mesma exceção de <c>EventoRepository.RemoverAnterioresA</c>. O nome fica: é o histórico da festa.
    /// </remarks>
    /// <param name="eventosAte">Eventos com data anterior a este dia.</param>
    /// <returns>Quantos convites tiveram os dados apagados.</returns>
    Task<int> DescartarDocumentosDeTodasAsFormaturas(DateOnly eventosAte, CancellationToken ct = default);
}
