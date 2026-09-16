using Backend.Api.DTOs.Cobrancas;
using Backend.Business.Pagamentos.Models;

namespace Backend.Api.DTOs.Pagamentos;

/// <summary>O extrato do próprio formando.</summary>
/// <param name="EmAbertoEmCentavos">Soma do valor de hoje das parcelas abertas e vencidas.</param>
/// <param name="Proxima">A primeira a pagar — aberta ou vencida, sem aviso pendente. Ausente, não há.</param>
/// <param name="Parcelas">Todas as parcelas, por vencimento.</param>
public sealed record ExtratoDTO(long EmAbertoEmCentavos, ParcelaDTO? Proxima, IReadOnlyList<ParcelaDTO> Parcelas);

/// <summary>O PIX da parcela, montado na hora.</summary>
/// <param name="CopiaECola">O BR Code — a tela desenha o QR a partir dele, no navegador.</param>
/// <param name="ValorEmCentavos">O valor de hoje.</param>
/// <param name="Chave">Chave da comissão.</param>
/// <param name="NomeDoTitular">O nome que o banco vai mostrar.</param>
/// <param name="Identificador">O identificador da parcela no PIX.</param>
public sealed record PixDaParcelaDTO(string CopiaECola, long ValorEmCentavos, string Chave, string NomeDoTitular, string Identificador);

/// <summary>Um informe na fila da tesouraria.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Parcela">A parcela.</param>
/// <param name="PagoEm">Dia informado.</param>
/// <param name="ValorEmCentavos">Valor informado.</param>
/// <param name="DevidoEmCentavos">O valor da parcela no dia informado.</param>
/// <param name="TemComprovante">Se há comprovante — abre em <c>/informes/{id}/comprovante</c>.</param>
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
