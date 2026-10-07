using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Recebimentos;
using Backend.Business.Cobrancas.Models;
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

/// <summary>As duas parcelas do Início, sem o extrato.</summary>
/// <param name="Proxima">A primeira a pagar — a mesma <c>proxima</c> do extrato. Nula: a pessoa está em dia.</param>
/// <param name="Seguinte">A que vem depois dela, pela mesma regra; nula quando não há.</param>
public sealed record ProximasParcelasDTO(ParcelaDTO? Proxima, ParcelaDTO? Seguinte);

/// <summary>O PIX pronto para pagar.</summary>
/// <param name="CopiaECola">O BR Code — a tela desenha o QR a partir dele, no navegador.</param>
/// <param name="Chave">Chave da comissão.</param>
/// <param name="NomeDoTitular">O nome que o banco vai mostrar.</param>
/// <param name="DocumentoDoTitular">CPF mascarado ou CNPJ, quando é esse o tipo da chave; nulo nos demais.</param>
/// <param name="ConferidaEm">Quando a comissão conferiu a titularidade no banco, em UTC. Nulo: a conferir.</param>
/// <param name="BancoDoTitular">O banco que o app do pagador deve mostrar; nulo quando a comissão não informou.</param>
public sealed record PixParaPagarDTO(
    string CopiaECola,
    string Chave,
    string NomeDoTitular,
    string? DocumentoDoTitular,
    DateTime? ConferidaEm,
    string? BancoDoTitular
);

/// <summary>Um meio que a comissão habilitou, com o que a tela precisa mostrar.</summary>
/// <remarks>Só o campo do próprio meio vem preenchido; os outros vêm nulos.</remarks>
/// <param name="Meio"><c>Pix</c>, <c>Transferencia</c> ou <c>Dinheiro</c>.</param>
/// <param name="Pix">O PIX da chave da comissão, só em <c>Pix</c>.</param>
/// <param name="Transferencia">Os dados bancários, só em <c>Transferencia</c>.</param>
/// <param name="Instrucao">O que fazer, em <c>Dinheiro</c>.</param>
public sealed record MeioDaCobrancaDTO(MeioDeRecebimento Meio, PixParaPagarDTO? Pix, DadosBancariosDTO? Transferencia, string? Instrucao);

/// <summary>Um meio do Mercado Pago da turma — baixa sozinho, sem aviso do formando.</summary>
/// <param name="Meio"><c>Pix</c> ou <c>Cartao</c>.</param>
/// <param name="Pix">O PIX pronto, só em <c>Pix</c>.</param>
/// <param name="Cartao">O formulário do cartão e o valor que ele cobra, só em <c>Cartao</c> (Sprint 39).</param>
public sealed record PagamentoPeloMercadoPagoDTO(MeioDePagamento Meio, PixDinamicoParaPagarDTO? Pix, CartaoParaPagarDTO? Cartao);

/// <summary>O cartão pronto para pagar (Sprint 39): o formulário do Mercado Pago tokeniza no navegador.</summary>
/// <param name="ChavePublica">A <c>public_key</c> da conta da turma, para o SDK do Mercado Pago.</param>
/// <param name="ValorEmCentavos">O que o cartão cobra — o valor do PIX mais o acréscimo.</param>
/// <param name="AcrescimoEmCentavos">A taxa repassada a quem paga; zero quando a turma absorve.</param>
/// <param name="MaximoDeParcelas">Em quantas vezes, no máximo; os juros do parcelamento são de quem paga.</param>
public sealed record CartaoParaPagarDTO(string ChavePublica, long ValorEmCentavos, long AcrescimoEmCentavos, int MaximoDeParcelas);

/// <summary>O pagamento de parcelas no cartão (Sprint 39).</summary>
/// <param name="ParcelaIds">As parcelas, de 1 a 24.</param>
/// <param name="Token">O token do cartão que o formulário do Mercado Pago gerou.</param>
/// <param name="Bandeira">O <c>payment_method_id</c> que o formulário identificou.</param>
/// <param name="Parcelas">Em quantas vezes, de 1 a 12.</param>
/// <param name="ValorEmCentavos">O valor que a tela mostrou.</param>
public sealed record PagamentoNoCartaoRequestDTO(
    IReadOnlyList<Guid>? ParcelaIds,
    string? Token,
    string? Bandeira,
    int? Parcelas,
    long? ValorEmCentavos
);

/// <summary>Em que pé ficou o pagamento no cartão.</summary>
/// <param name="Situacao"><c>Pago</c> — as parcelas já estão pagas — ou <c>EmAnalise</c>.</param>
public sealed record PagamentoNoCartaoDTO(SituacaoDoCartao Situacao);

/// <summary>O PIX do Mercado Pago da turma: o copia-e-cola e até quando vale.</summary>
/// <param name="CopiaECola">O BR Code; o QR é desenhado no navegador.</param>
/// <param name="ExpiraEm">Até quando aceita pagamento, em UTC.</param>
public sealed record PixDinamicoParaPagarDTO(string CopiaECola, DateTime ExpiraEm);

/// <summary>A cobrança da parcela, montada na hora: quanto, e por onde a turma aceita receber.</summary>
/// <remarks>Com um meio só nas duas listas, a tela não desenha seletor — é o caminho de sempre.</remarks>
/// <param name="ValorEmCentavos">O valor de hoje, somado quando são várias parcelas.</param>
/// <param name="PeloMercadoPago">Os meios do Mercado Pago da turma, primeiro; vazio sem conexão ou se ele falhou.</param>
/// <param name="Meios">Os meios que a comissão habilitou.</param>
public sealed record CobrancaDaParcelaDTO(
    long ValorEmCentavos,
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

/// <summary>Corpo do cancelamento avulso de uma parcela.</summary>
/// <param name="Justificativa">Por que — fica na auditoria.</param>
public sealed record CancelarParcelaRequestDTO(string? Justificativa);

/// <summary>Corpo do fechamento do pago sem parcela.</summary>
/// <param name="Observacao">O que a comissão fez: devolveu no painel do Mercado Pago, lançou como outra receita…</param>
public sealed record FecharValorADevolverRequestDTO(string? Observacao);

/// <summary>Um item da lista "a devolver" da tesouraria (Sprint 42).</summary>
/// <param name="Id">Identificador.</param>
/// <param name="UsuarioId">Formando.</param>
/// <param name="Nome">Nome civil do cadastro, ou o da conta.</param>
/// <param name="Origem"><c>CreditoDePedido</c>, <c>ParcelaCancelada</c> ou <c>PagoSemParcela</c>.</param>
/// <param name="Status"><c>ADevolver</c>, <c>Devolvido</c> ou <c>Fechado</c>.</param>
/// <param name="ValorEmCentavos">Quanto falta devolver, em centavos.</param>
/// <param name="Tipo">Tipo do item da parcela ou do pedido.</param>
/// <param name="Descricao">Descrição do item, se houver.</param>
/// <param name="NumeroDaParcela">A parcela de origem; nulo no crédito de pedido.</param>
/// <param name="Vencimento">Vencimento dela.</param>
/// <param name="CriadoEm">Quando entrou na lista.</param>
/// <param name="ResolvidoEm">Quando saiu da lista.</param>
/// <param name="Observacao">O que foi feito com o pago sem parcela, ou por que fechou sozinho.</param>
/// <param name="TemComprovante">Se a devolução tem comprovante.</param>
public sealed record ValorADevolverDTO(
    Guid Id,
    Guid UsuarioId,
    string Nome,
    OrigemDoValorADevolver Origem,
    StatusDoValorADevolver Status,
    long ValorEmCentavos,
    TipoDeCobranca Tipo,
    string? Descricao,
    int? NumeroDaParcela,
    DateOnly? Vencimento,
    DateTime CriadoEm,
    DateTime? ResolvidoEm,
    string? Observacao,
    bool TemComprovante
);

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
