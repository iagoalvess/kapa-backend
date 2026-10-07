using Backend.Business.Assinaturas.Models;
using Backend.Business.Pagamentos.Models;

namespace Backend.Api.DTOs.Assinaturas;

/// <summary>Plano contratável.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Codigo">Código enviado no checkout.</param>
/// <param name="Nome">Nome do plano.</param>
/// <param name="Descricao">Para que turma o plano serve, em uma linha.</param>
/// <param name="PrecoEmCentavos">Preço de um ciclo, em centavos — <c>34990</c> é R$ 349,90.</param>
/// <param name="PrecoCheioEmCentavos">Preço sem desconto, em centavos — o valor riscado. Nulo quando não há desconto.</param>
/// <param name="Ciclo"><c>Mensal</c> ou <c>Anual</c>.</param>
/// <param name="LimiteDeFormandos">Quantos formandos cabem.</param>
/// <param name="Modulos">Módulos incluídos, na ordem de exibição.</param>
/// <param name="Recomendado">Destacado na tela.</param>
public sealed record PlanoDTO(
    Guid Id,
    string Codigo,
    string Nome,
    string Descricao,
    long PrecoEmCentavos,
    long? PrecoCheioEmCentavos,
    CicloDeCobranca Ciclo,
    int LimiteDeFormandos,
    IReadOnlyList<string> Modulos,
    bool Recomendado
);

/// <summary>O plano que vale para a turma agora (Sprint 45).</summary>
/// <param name="Codigo">Código do plano, como no catálogo; <c>gratuito</c> sem plano pago em vigor.</param>
/// <param name="Nome">Nome exibido.</param>
/// <param name="Modulos">Módulos que o plano libera: o que a tela usa para trancar área.</param>
/// <param name="Pago">Se é plano contratado em vigor. Falso no gratuito — e a turma vencida volta a ele.</param>
public sealed record PlanoDaTurmaDTO(string Codigo, string Nome, IReadOnlyList<string> Modulos, bool Pago);

/// <summary>A assinatura mais recente da formatura.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Status"><c>Pendente</c>, <c>Ativa</c>, <c>Vencida</c> ou <c>Cancelada</c>.</param>
/// <param name="Plano">Plano contratado.</param>
/// <param name="VigenteAte">Fim da vigência paga, em UTC.</param>
/// <param name="ProximaCobrancaEm">Próxima cobrança automática, em UTC; nulo se não houver.</param>
/// <param name="CanceladaEm">Quando a renovação foi cancelada, em UTC.</param>
/// <param name="Meio"><c>Cartao</c> (recorrente) ou <c>Pix</c> (avulso, um por ciclo).</param>
/// <param name="ProximoPlano">Plano que passa a valer na próxima renovação; nulo sem descida agendada.</param>
/// <param name="CartaoAguardandoAutorizacao">Se a troca para o cartão espera a autorização na página do provedor.</param>
/// <param name="Cupom">Cupom usado na primeira cobrança; nulo sem cupom.</param>
public sealed record AssinaturaDTO(
    Guid Id,
    StatusDaAssinatura Status,
    PlanoDTO Plano,
    DateTime? VigenteAte,
    DateTime? ProximaCobrancaEm,
    DateTime? CanceladaEm,
    MeioDePagamento Meio,
    PlanoDTO? ProximoPlano,
    bool CartaoAguardandoAutorizacao,
    CupomAplicavelDTO? Cupom
);

/// <summary>Um pagamento do plano, no histórico.</summary>
/// <param name="Id">Cobrança.</param>
/// <param name="PlanoNome">Plano pago.</param>
/// <param name="Motivo"><c>Ciclo</c> ou <c>Diferenca</c> (subida de plano).</param>
/// <param name="Meio"><c>Cartao</c> ou <c>Pix</c>.</param>
/// <param name="ValorEmCentavos">Valor, em centavos.</param>
/// <param name="Situacao"><c>Aberta</c>, <c>Paga</c>, <c>Cancelada</c> ou <c>Estornada</c>.</param>
/// <param name="Url">Página de pagamento, enquanto aberta; nula depois.</param>
/// <param name="CriadaEm">Quando nasceu, em UTC.</param>
/// <param name="PagaEm">Quando foi paga, em UTC; nula enquanto não.</param>
/// <param name="ValorEstornadoEmCentavos">Quanto voltou, se estornada.</param>
/// <param name="EstornadaEm">Quando foi estornada, em UTC.</param>
public sealed record CobrancaDoPlanoDTO(
    Guid Id,
    string PlanoNome,
    MotivoDaCobranca Motivo,
    MeioDePagamento Meio,
    long ValorEmCentavos,
    SituacaoDaCobrancaDoPlano Situacao,
    string? Url,
    DateTime CriadaEm,
    DateTime? PagaEm,
    long? ValorEstornadoEmCentavos,
    DateTime? EstornadaEm
);

/// <summary>Corpo da troca de plano.</summary>
/// <param name="PlanoCodigo">Plano novo, do mesmo ciclo.</param>
public sealed record TrocarPlanoRequestDTO(string? PlanoCodigo);

/// <summary>Corpo da troca de meio.</summary>
/// <param name="Meio"><c>Cartao</c> ou <c>Pix</c>.</param>
public sealed record TrocarMeioRequestDTO(MeioDePagamento Meio);

/// <summary>O que a troca de plano ou de meio deu.</summary>
/// <remarks>A assinatura depois da troca se lê em <c>GET /formaturas/atual/assinatura</c>.</remarks>
/// <param name="Url">Página do provedor para pagar a diferença ou autorizar o cartão; nula quando nada precisa ser pago agora.</param>
public sealed record TrocaDTO(string? Url);

/// <summary>Corpo do checkout.</summary>
/// <param name="PlanoCodigo">Plano escolhido.</param>
/// <param name="Meio"><c>Cartao</c> (recorrente) ou <c>Pix</c> (avulso). Nulo é cartão.</param>
/// <param name="CupomCodigo">Cupom da primeira cobrança; nulo é sem cupom. Só o código: o preço é do servidor.</param>
public sealed record IniciarCheckoutRequestDTO(string PlanoCodigo, MeioDePagamento? Meio = null, string? CupomCodigo = null);

/// <summary>Sessão de pagamento criada.</summary>
/// <param name="Url">Página do provedor, para onde o navegador deve ir.</param>
public sealed record CheckoutDTO(string Url);

/// <summary>Recibo do webhook.</summary>
/// <param name="EventoId">Id do evento recebido.</param>
/// <param name="Duplicado">Se já tinha chegado antes e não foi reprocessado.</param>
public sealed record ReciboDeWebhookDTO(string EventoId, bool Duplicado);

/// <summary>Cupom que vale para a turma, como o checkout o mostra.</summary>
/// <param name="Codigo">Código normalizado.</param>
/// <param name="Percentual">Desconto na primeira cobrança, em % (1 a 50).</param>
public sealed record CupomAplicavelDTO(string Codigo, int Percentual);

/// <summary>Cupom no painel do Kapa.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Codigo">Código.</param>
/// <param name="Percentual">Desconto na primeira cobrança, em %.</param>
/// <param name="ValidoAte">Último instante em que vale, em UTC.</param>
/// <param name="LimiteDeUsos">Quantas turmas podem usar.</param>
/// <param name="Usos">Quantas já usaram.</param>
/// <param name="Ativo">Se não foi desativado.</param>
/// <param name="CriadoEm">Quando foi criado, em UTC.</param>
public sealed record CupomDTO(Guid Id, string Codigo, int Percentual, DateTime ValidoAte, int LimiteDeUsos, int Usos, bool Ativo, DateTime CriadoEm);

/// <summary>Corpo do cupom novo.</summary>
/// <param name="Codigo">6 a 20 letras, números ou hífen; é guardado em maiúsculo.</param>
/// <param name="Percentual">Desconto na primeira cobrança, de 1 a 50.</param>
/// <param name="ValidoAte">Último dia em que vale (<c>aaaa-mm-dd</c>, no horário de Brasília).</param>
/// <param name="LimiteDeUsos">Quantas turmas podem usar, de 1 a 1000.</param>
public sealed record NovoCupomRequestDTO(string Codigo, int Percentual, DateOnly ValidoAte, int LimiteDeUsos);
