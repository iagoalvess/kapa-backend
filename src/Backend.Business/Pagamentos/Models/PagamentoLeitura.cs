using Backend.Business.Cobrancas.Models;
using Backend.Business.Recebimentos.Models;

namespace Backend.Business.Pagamentos.Models;

/// <summary>O "já paguei", como o formando informa.</summary>
/// <param name="PagoEm">Dia em que pagou.</param>
/// <param name="ValorEmCentavos">Quanto pagou.</param>
/// <param name="Meio">Como pagou. Pode ser um meio que a turma não habilitou (P7 de 21/09/2026).</param>
public sealed record NovoInforme(DateOnly PagoEm, long ValorEmCentavos, MeioDeRecebimento Meio);

/// <summary>A baixa sem informe: dinheiro, TED, o formando que pagou e não avisou.</summary>
/// <param name="Forma">Como o dinheiro chegou.</param>
/// <param name="PagoEm">Dia em que entrou.</param>
/// <param name="ValorEmCentavos">Quanto entrou.</param>
public sealed record BaixaManual(FormaDePagamento Forma, DateOnly PagoEm, long ValorEmCentavos);

/// <summary>Um informe do lote e o valor que de fato entrou.</summary>
/// <param name="InformeId">Informe pendente.</param>
/// <param name="ValorRecebidoEmCentavos">O que a tesouraria viu no extrato — vem preenchido com o informado.</param>
public sealed record ConfirmacaoDeInforme(Guid InformeId, long ValorRecebidoEmCentavos);

/// <summary>O lote da conferência.</summary>
/// <param name="Itens">Informes marcados, cada um com o valor recebido.</param>
public sealed record ConfirmarInformes(IReadOnlyList<ConfirmacaoDeInforme> Itens);

/// <summary>Recusa de um informe.</summary>
/// <param name="Motivo">Por que — vai ao formando por e-mail.</param>
public sealed record RecusarInforme(string Motivo);

/// <summary>Estorno de uma baixa.</summary>
/// <param name="Justificativa">Por que — fica na auditoria.</param>
public sealed record EstornarBaixa(string Justificativa);

/// <summary>Tudo o que a baixa grava, venha de onde vier.</summary>
/// <param name="Forma">Como o dinheiro chegou.</param>
/// <param name="PagoEm">Dia em que entrou.</param>
/// <param name="ValorEmCentavos">Quanto entrou.</param>
/// <param name="ComprovanteArquivoId">Comprovante da baixa manual, se enviado.</param>
/// <param name="UsuarioId">Quem baixou.</param>
/// <param name="EnderecoIp">De onde baixou.</param>
/// <param name="AgoraUtc">Quando baixou.</param>
public sealed record DadosDaBaixa(
    FormaDePagamento Forma,
    DateOnly PagoEm,
    long ValorEmCentavos,
    Guid? ComprovanteArquivoId,
    Guid UsuarioId,
    string? EnderecoIp,
    DateTime AgoraUtc
);

/// <summary>O que a baixa precisa saber da turma e do formando para gravar o devido e avisar.</summary>
/// <param name="FormaturaId">Turma, para a auditoria.</param>
/// <param name="NomeDaTurma">Nome da turma, para o e-mail.</param>
/// <param name="Regras">Regras aceitas pelo formando, para o devido do dia.</param>
/// <param name="EmailDoFormando">Destino do "pagamento confirmado"; nulo, não avisa.</param>
public sealed record ContextoDaBaixa(Guid FormaturaId, string NomeDaTurma, RegrasDeAtraso Regras, string? EmailDoFormando);

/// <summary>O resultado do lote.</summary>
/// <param name="Confirmados">Informes que baixaram parcela.</param>
/// <param name="Ignorados">Informes que já tinham sido conferidos, ou cuja parcela já não estava aberta — o clique duplo.</param>
public sealed record ResultadoDaConferencia(int Confirmados, int Ignorados);

/// <summary>O extrato do próprio formando: o que deve, o que vem a seguir e a grade inteira.</summary>
/// <param name="EmAbertoEmCentavos">Soma do valor do dia das parcelas abertas e vencidas.</param>
/// <param name="Proxima">A primeira a pagar: aberta ou vencida, sem aviso pendente, por vencimento.</param>
/// <param name="Parcelas">Todas as parcelas, por vencimento.</param>
public sealed record ExtratoDoFormando(long EmAbertoEmCentavos, ParcelaResumo? Proxima, IReadOnlyList<ParcelaResumo> Parcelas);

/// <summary>O PIX pronto para pagar, montado na hora e não gravado.</summary>
/// <param name="CopiaECola">O BR Code, que a tela transforma em QR.</param>
/// <param name="Chave">Chave da comissão.</param>
/// <param name="NomeDoTitular">O nome que o banco vai mostrar.</param>
public sealed record PixParaPagar(string CopiaECola, string Chave, string NomeDoTitular);

/// <summary>Um meio que a turma aceita, com o que a tela do formando precisa mostrar.</summary>
/// <remarks>
/// Só o campo do próprio meio vem preenchido: o PIX traz o BR Code, a transferência traz a conta e o
/// dinheiro traz a instrução. É o que permite à tela desenhar cada um do seu jeito sem perguntar nada
/// de volta.
/// </remarks>
/// <param name="Meio">Qual é o meio.</param>
/// <param name="Pix">O PIX pronto, só em <see cref="MeioDeRecebimento.Pix"/>.</param>
/// <param name="Transferencia">Os dados bancários, só em <see cref="MeioDeRecebimento.Transferencia"/>.</param>
/// <param name="Instrucao">Com quem falar, em dinheiro.</param>
public sealed record MeioDaCobranca(MeioDeRecebimento Meio, PixParaPagar? Pix, DadosBancarios? Transferencia, string? Instrucao);

/// <summary>
/// A cobrança de uma parcela — ou de várias no mesmo pagamento: quanto, e por onde a turma aceita.
/// </summary>
/// <remarks>
/// Montada na hora e não gravada: os meios são os vigentes e o valor é o de hoje. Com um meio só, a
/// tela não tem seletor — é a mesma tela de sempre (decisão 3).
/// </remarks>
/// <param name="ValorEmCentavos">O valor do dia, somado quando são várias parcelas.</param>
/// <param name="Identificador">O identificador da parcela no PIX.</param>
/// <param name="Meios">Os meios habilitados, ao menos um.</param>
public sealed record CobrancaDaParcela(long ValorEmCentavos, string Identificador, IReadOnlyList<MeioDaCobranca> Meios);

/// <summary>Um informe na fila da tesouraria.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Parcela">A parcela, como a gestão a vê.</param>
/// <param name="PagoEm">Dia informado.</param>
/// <param name="ValorEmCentavos">Valor informado.</param>
/// <param name="DevidoEmCentavos">O valor da parcela no dia informado, pelas regras aceitas.</param>
/// <param name="TemComprovante">Se o formando anexou comprovante.</param>
/// <param name="MeioEscolhido">Como o formando diz ter pago; nulo nos avisos anteriores à Sprint 18.</param>
/// <param name="Status">Situação.</param>
/// <param name="InformadoEm">Quando o formando avisou, em UTC.</param>
/// <param name="ConferidoEm">Quando a tesouraria confirmou ou recusou, em UTC; nulo enquanto pendente.</param>
public sealed record InformeNaFila(
    Guid Id,
    ParcelaResumo Parcela,
    DateOnly PagoEm,
    long ValorEmCentavos,
    long DevidoEmCentavos,
    bool TemComprovante,
    MeioDeRecebimento? MeioEscolhido,
    StatusDoInforme Status,
    DateTime InformadoEm,
    DateTime? ConferidoEm = null
);

/// <summary>O comprovante de um informe: o arquivo e quem o enviou — o dono no módulo de arquivos.</summary>
/// <param name="ArquivoId">Arquivo.</param>
/// <param name="EnviadoPorUsuarioId">Formando que enviou.</param>
public sealed record ComprovanteDoInforme(Guid ArquivoId, Guid EnviadoPorUsuarioId);

/// <summary>Uma baixa com valor recebido diferente do devido.</summary>
/// <param name="RecebimentoId">Recebimento.</param>
/// <param name="Parcela">A parcela, como a gestão a vê.</param>
/// <param name="PagoEm">Dia em que o dinheiro entrou.</param>
/// <param name="DevidoEmCentavos">Valor do dia do pagamento.</param>
/// <param name="RecebidoEmCentavos">O que entrou.</param>
/// <param name="Forma">Como entrou.</param>
/// <param name="BaixadoPor">Nome de quem baixou.</param>
public sealed record Divergencia(
    Guid RecebimentoId,
    ParcelaResumo Parcela,
    DateOnly PagoEm,
    long DevidoEmCentavos,
    long RecebidoEmCentavos,
    FormaDePagamento Forma,
    string BaixadoPor
);

/// <summary>
/// Filtros da fila da conferência.
/// </summary>
/// <remarks>
/// <see cref="ConferidosHoje"/> recorta pelo dia local de exibição, e não pelo dia UTC: às 22h de
/// Brasília a tesouraria ainda está no mesmo dia de trabalho, e o painel do que ela fechou não pode
/// zerar sozinho.
/// <para>
/// <see cref="De"/> e <see cref="Ate"/> são do dia que o formando <b>informou ter pago</b>, não do
/// vencimento: quem confere está com o extrato de um dia aberto ao lado, e é por ele que procura.
/// </para>
/// </remarks>
/// <param name="Status">Situação dos informes.</param>
/// <param name="ConferidosHoje">Só os conferidos no dia de hoje.</param>
/// <param name="De">Pagamento informado a partir deste dia, inclusive.</param>
/// <param name="Ate">Pagamento informado até este dia, inclusive.</param>
/// <param name="Busca">Trecho do nome do formando.</param>
public sealed record FiltroDeInformes(
    StatusDoInforme Status = StatusDoInforme.Pendente,
    bool ConferidosHoje = false,
    DateOnly? De = null,
    DateOnly? Ate = null,
    string? Busca = null
);
