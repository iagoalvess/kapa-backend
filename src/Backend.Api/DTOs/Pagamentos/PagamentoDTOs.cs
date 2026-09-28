using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Recebimentos;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;

namespace Backend.Api.DTOs.Pagamentos;

/// <summary>O extrato do próprio formando.</summary>
/// <param name="EmAbertoEmCentavos">Soma do valor de hoje das parcelas abertas e vencidas.</param>
/// <param name="Proxima">A primeira a pagar — aberta ou vencida, sem aviso pendente. Ausente, não há.</param>
/// <param name="Parcelas">Todas as parcelas, por vencimento.</param>
public sealed record ExtratoDTO(long EmAbertoEmCentavos, ParcelaDTO? Proxima, IReadOnlyList<ParcelaDTO> Parcelas);

/// <summary>O que o extrato tem de pendente, sem o extrato — o selo do menu.</summary>
/// <param name="VencidasSemAviso">Parcelas vencidas em que o formando ainda não avisou o pagamento.</param>
public sealed record PendenciasDoExtratoDTO(int VencidasSemAviso);

/// <summary>O PIX pronto para pagar.</summary>
/// <param name="CopiaECola">O BR Code — a tela desenha o QR a partir dele, no navegador.</param>
/// <param name="Chave">Chave da comissão.</param>
/// <param name="NomeDoTitular">O nome que o banco vai mostrar.</param>
/// <param name="DocumentoDoTitular">CPF mascarado ou CNPJ, quando é esse o tipo da chave; nulo nos demais.</param>
/// <param name="ConferidaEm">Quando a comissão conferiu a titularidade no banco, em UTC. Nulo: a conferir.</param>
public sealed record PixParaPagarDTO(string CopiaECola, string Chave, string NomeDoTitular, string? DocumentoDoTitular, DateTime? ConferidaEm);

/// <summary>Um meio que a comissão habilitou, com o que a tela precisa mostrar.</summary>
/// <remarks>Só o campo do próprio meio vem preenchido; os outros vêm nulos.</remarks>
/// <param name="Meio"><c>Pix</c>, <c>Transferencia</c> ou <c>Dinheiro</c>.</param>
/// <param name="Pix">O PIX da chave da comissão, só em <c>Pix</c>.</param>
/// <param name="Transferencia">Os dados bancários, só em <c>Transferencia</c>.</param>
/// <param name="Instrucao">O que fazer, em <c>Dinheiro</c>.</param>
public sealed record MeioDaCobrancaDTO(MeioDeRecebimento Meio, PixParaPagarDTO? Pix, DadosBancariosDTO? Transferencia, string? Instrucao);

/// <summary>Um meio do Mercado Pago da turma — baixa sozinho, sem aviso do formando.</summary>
/// <param name="Meio"><c>Pix</c>, <c>PixAutomatico</c> ou <c>Cartao</c>.</param>
/// <param name="Pix">O PIX pronto, só em <c>Pix</c>.</param>
public sealed record PagamentoPeloMercadoPagoDTO(MeioDePagamento Meio, PixDinamicoParaPagarDTO? Pix);

/// <summary>O PIX do Mercado Pago da turma: o copia-e-cola e até quando vale.</summary>
/// <param name="CopiaECola">O BR Code; o QR é desenhado no navegador.</param>
/// <param name="ExpiraEm">Até quando aceita pagamento, em UTC.</param>
public sealed record PixDinamicoParaPagarDTO(string CopiaECola, DateTime ExpiraEm);

/// <summary>A cobrança da parcela, montada na hora: quanto, e por onde a turma aceita receber.</summary>
/// <remarks>Com um meio só nas duas listas, a tela não desenha seletor — é o caminho de sempre.</remarks>
/// <param name="ValorEmCentavos">O valor de hoje, somado quando são várias parcelas.</param>
/// <param name="Identificador">O identificador da parcela no PIX.</param>
/// <param name="PeloMercadoPago">Os meios do Mercado Pago da turma, primeiro; vazio sem conexão ou se ele falhou.</param>
/// <param name="Meios">Os meios que a comissão habilitou.</param>
public sealed record CobrancaDaParcelaDTO(
    long ValorEmCentavos,
    string Identificador,
    IReadOnlyList<PagamentoPeloMercadoPagoDTO> PeloMercadoPago,
    IReadOnlyList<MeioDaCobrancaDTO> Meios
);

/// <summary>Um informe na fila da tesouraria.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Parcela">A parcela.</param>
/// <param name="PagoEm">Dia informado.</param>
/// <param name="ValorEmCentavos">Valor informado.</param>
/// <param name="DevidoEmCentavos">O valor da parcela no dia informado.</param>
/// <param name="TemComprovante">Se há comprovante — abre em <c>/informes/{id}/comprovante</c>.</param>
/// <param name="MeioEscolhido">Como o formando diz ter pago; nulo nos avisos anteriores à Sprint 18.</param>
/// <param name="Status"><c>Pendente</c>, <c>Confirmado</c> ou <c>Recusado</c>.</param>
/// <param name="InformadoEm">Quando o formando avisou, em UTC.</param>
/// <param name="ConferidoEm">Quando a tesouraria confirmou ou recusou, em UTC; ausente enquanto pendente.</param>
public sealed record InformeDTO(
    Guid Id,
    ParcelaDTO Parcela,
    DateOnly PagoEm,
    long ValorEmCentavos,
    long DevidoEmCentavos,
    bool TemComprovante,
    MeioDeRecebimento? MeioEscolhido,
    StatusDoInforme Status,
    DateTime InformadoEm,
    DateTime? ConferidoEm
);

/// <summary>Um informe do lote.</summary>
/// <param name="InformeId">Informe pendente.</param>
/// <param name="ValorRecebidoEmCentavos">O que entrou na conta, em centavos.</param>
public sealed record ConfirmacaoDeInformeDTO(Guid InformeId, long ValorRecebidoEmCentavos);

/// <summary>Corpo da confirmação em lote.</summary>
/// <param name="Itens">Informes marcados, cada um com o valor recebido.</param>
public sealed record ConfirmarInformesRequestDTO(IReadOnlyList<ConfirmacaoDeInformeDTO>? Itens);

/// <summary>O resultado do lote.</summary>
/// <param name="Confirmados">Informes que baixaram parcela.</param>
/// <param name="Ignorados">Já conferidos antes — o clique duplo.</param>
public sealed record ResultadoDaConferenciaDTO(int Confirmados, int Ignorados);

/// <summary>Corpo da recusa.</summary>
/// <param name="Motivo">Por que — vai ao formando por e-mail.</param>
public sealed record RecusarInformeRequestDTO(string? Motivo);

/// <summary>Corpo do estorno.</summary>
/// <param name="Justificativa">Por que — fica na auditoria.</param>
public sealed record EstornarBaixaRequestDTO(string? Justificativa);

/// <summary>Uma baixa com valor recebido diferente do devido.</summary>
/// <param name="RecebimentoId">Recebimento.</param>
/// <param name="Parcela">A parcela.</param>
/// <param name="PagoEm">Dia em que o dinheiro entrou.</param>
/// <param name="DevidoEmCentavos">Valor do dia do pagamento.</param>
/// <param name="RecebidoEmCentavos">O que entrou.</param>
/// <param name="Forma"><c>Pix</c>, <c>Dinheiro</c>, <c>Transferencia</c> ou <c>Outro</c>.</param>
/// <param name="BaixadoPor">Nome de quem baixou.</param>
public sealed record DivergenciaDTO(
    Guid RecebimentoId,
    ParcelaDTO Parcela,
    DateOnly PagoEm,
    long DevidoEmCentavos,
    long RecebidoEmCentavos,
    FormaDePagamento Forma,
    string BaixadoPor
);
