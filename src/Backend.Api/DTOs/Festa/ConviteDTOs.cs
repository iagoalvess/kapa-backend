using Backend.Business.Agenda.Models;
using Backend.Business.Festa.Models;

namespace Backend.Api.DTOs.Festa;

/// <summary>Corpo da nomeação (ou troca) do convidado.</summary>
/// <param name="Nome">Quem vai usar o convite.</param>
/// <param name="TipoDoDocumento"><c>Cpf</c> ou <c>Rg</c>; obrigatório junto com o número.</param>
/// <param name="NumeroDoDocumento">Número, com ou sem pontuação.</param>
/// <param name="Email">E-mail do convidado, para receber o convite; opcional.</param>
/// <param name="Observacoes">Restrição alimentar, acessibilidade; opcional.</param>
public sealed record ConvidadoRequestDTO(
    string? Nome,
    TipoDeDocumento? TipoDoDocumento,
    string? NumeroDoDocumento,
    string? Email,
    string? Observacoes = null
);

/// <summary>Corpo da cortesia: o convidado e o motivo.</summary>
/// <param name="Nome">Quem vai usar o convite.</param>
/// <param name="TipoDoDocumento"><c>Cpf</c> ou <c>Rg</c>.</param>
/// <param name="NumeroDoDocumento">Número.</param>
/// <param name="Email">E-mail do convidado; opcional.</param>
/// <param name="Motivo">Por que a turma está dando o convite.</param>
/// <param name="EventoId">Evento da cortesia; ausente é a festa.</param>
/// <param name="Observacoes">Restrição alimentar, acessibilidade; opcional.</param>
public sealed record CortesiaRequestDTO(
    string? Nome,
    TipoDeDocumento? TipoDoDocumento,
    string? NumeroDoDocumento,
    string? Email,
    string? Motivo,
    Guid? EventoId = null,
    string? Observacoes = null
);

/// <summary>Corpo da liberação manual.</summary>
/// <param name="PedidoId">Pedido de convite extra ainda em aberto.</param>
/// <param name="Motivo">Por que o convite sai antes da quitação.</param>
public sealed record LiberacaoRequestDTO(Guid PedidoId, string? Motivo);

/// <summary>Corpo do check-in.</summary>
/// <param name="EventoId">Evento aberto na portaria; ausente aceita o do próprio convite.</param>
public sealed record CheckInRequestDTO(Guid? EventoId);

/// <summary>Uma entrada marcada sem rede.</summary>
/// <param name="Codigo">Código ou token do convite.</param>
/// <param name="ValidadoEm">Quando o aparelho marcou, em UTC.</param>
/// <param name="Aparelho">Identificação do aparelho.</param>
public sealed record EntradaSemRedeDTO(string? Codigo, DateTime ValidadoEm, string? Aparelho);

/// <summary>Corpo da sincronização da lista sem rede.</summary>
/// <param name="Entradas">O que o aparelho marcou.</param>
public sealed record SincronizacaoRequestDTO(IReadOnlyList<EntradaSemRedeDTO>? Entradas);

/// <summary>O evento que o convite imprime, com os horários que a tela precisa.</summary>
/// <param name="Id">Evento da agenda.</param>
/// <param name="Tipo"><c>Festa</c> ou <c>Colacao</c>.</param>
/// <param name="Titulo">Como a turma chama o evento.</param>
/// <param name="Data">O dia.</param>
/// <param name="Hora">A hora.</param>
/// <param name="Local">Onde.</param>
/// <param name="Completo">Se hora e local estão definidos (P6).</param>
/// <param name="FechamentoDaLista">Até quando o formando troca nomes: 24 h antes (P5).</param>
/// <param name="JanelaAbreEm">Quando a validação na porta abre: 6 h antes (P7).</param>
public sealed record EventoDoConviteDTO(
    Guid Id,
    TipoDeEvento Tipo,
    string Titulo,
    DateOnly Data,
    TimeOnly? Hora,
    string? Local,
    bool Completo,
    DateTime FechamentoDaLista,
    DateTime JanelaAbreEm
);

/// <summary>A página pública do convite.</summary>
/// <param name="Turma">Nome da turma.</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Evento">O que, quando e onde.</param>
/// <param name="Codigo">Código para ditar.</param>
/// <param name="Token">Código com a assinatura — vai no QR.</param>
/// <param name="NomeDoConvidado">Titular.</param>
/// <param name="Documento">Documento mascarado.</param>
public sealed record ConvitePublicoDTO(
    string Turma,
    string Instituicao,
    EventoDoConviteDTO Evento,
    string Codigo,
    string Token,
    string NomeDoConvidado,
    string? Documento
);

/// <summary>Um convite do formando.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Sequencial">Posição: 1, 2, 3.</param>
/// <param name="Codigo">Código.</param>
/// <param name="Token">Código com a assinatura; nulo enquanto "a definir" — o link só existe com convidado.</param>
/// <param name="NomeDoConvidado">Titular; nulo enquanto "a definir".</param>
/// <param name="TipoDoDocumento">CPF ou RG.</param>
/// <param name="Documento">Documento mascarado.</param>
/// <param name="EmailDoConvidado">Para onde o convite foi mandado.</param>
/// <param name="EmitidoEm">Quando nasceu.</param>
/// <param name="ValidadoEm">Quando entrou.</param>
public sealed record MeuConviteDTO(
    Guid Id,
    int Sequencial,
    string Codigo,
    string? Token,
    string? NomeDoConvidado,
    TipoDeDocumento? TipoDoDocumento,
    string? Documento,
    string? EmailDoConvidado,
    DateTime EmitidoEm,
    DateTime? ValidadoEm,
    string? Observacoes
);

/// <summary>Os convites do formando para a festa.</summary>
/// <param name="Evento">A festa; nula sem agenda.</param>
/// <param name="ListaAberta">Se ainda pode trocar nomes.</param>
/// <param name="Convites">Os convites válidos.</param>
/// <param name="AguardandoPagamento">Unidades pedidas que ainda não viraram convite.</param>
public sealed record MeusConvitesDTO(EventoDoConviteDTO? Evento, bool ListaAberta, IEnumerable<MeuConviteDTO> Convites, int AguardandoPagamento);

/// <summary>A entrada de um convite.</summary>
/// <param name="CheckInId">Entrada, para desfazer.</param>
/// <param name="ValidadoEm">Quando entrou.</param>
/// <param name="ValidadoPor">Quem validou.</param>
/// <param name="ValidadoPorUsuarioId">Id de quem validou.</param>
public sealed record EntradaNaPortariaDTO(Guid CheckInId, DateTime ValidadoEm, string ValidadoPor, Guid ValidadoPorUsuarioId);

/// <summary>Um convite na portaria.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="EventoId">Evento.</param>
/// <param name="Codigo">Código.</param>
/// <param name="NomeDoConvidado">Titular.</param>
/// <param name="Documento">Documento mascarado.</param>
/// <param name="ConvidadoDe">Formando dono; nulo na cortesia.</param>
/// <param name="Origem"><c>Comprado</c>, <c>Cota</c> ou <c>Cortesia</c>.</param>
/// <param name="Situacao"><c>Valido</c>, <c>SemTitular</c>, <c>Validado</c> ou <c>Revogado</c>.</param>
/// <param name="MotivoDaRevogacao">Por que não vale.</param>
/// <param name="Entrada">A entrada ativa.</param>
/// <param name="EntrouSemRedeDuasVezes">Se dois aparelhos sem rede deixaram entrar.</param>
public sealed record ConviteNaPortariaDTO(
    Guid Id,
    Guid EventoId,
    string Codigo,
    string? NomeDoConvidado,
    string? Documento,
    string? ConvidadoDe,
    OrigemDoConvite Origem,
    SituacaoNaPortaria Situacao,
    string? MotivoDaRevogacao,
    EntradaNaPortariaDTO? Entrada,
    bool EntrouSemRedeDuasVezes,
    string? Observacoes
);

/// <summary>Um convite aberto na portaria.</summary>
/// <param name="Convite">O convite.</param>
/// <param name="Evento">O evento dele.</param>
/// <param name="JanelaAberta">Se "Validar entrada" aparece agora.</param>
public sealed record ConsultaNaPortariaDTO(ConviteNaPortariaDTO Convite, EventoDoConviteDTO Evento, bool JanelaAberta);

/// <summary>A lista da portaria.</summary>
/// <param name="Evento">Evento.</param>
/// <param name="Total">Convites que valem ou já entraram.</param>
/// <param name="Validados">Quantos entraram.</param>
/// <param name="SemTitular">Quantos estão sem nome ou documento.</param>
/// <param name="JanelaAberta">Se a validação está aberta.</param>
/// <param name="GeradaEm">Quando a lista foi montada.</param>
/// <param name="Convites">Os convites.</param>
public sealed record ListaDaPortariaDTO(
    EventoDoConviteDTO Evento,
    int Total,
    int Validados,
    int SemTitular,
    bool JanelaAberta,
    DateTime GeradaEm,
    IEnumerable<ConviteNaPortariaDTO> Convites
);

/// <summary>Resultado da sincronização.</summary>
/// <param name="Validadas">Viraram entrada.</param>
/// <param name="Repetidas">Já tinham entrado — registradas como tentativa repetida.</param>
/// <param name="Recusadas">Inexistentes, revogadas ou de outro evento.</param>
public sealed record ResultadoDaSincronizacaoDTO(int Validadas, int Repetidas, int Recusadas);

/// <summary>A situação dos convites da festa.</summary>
/// <param name="Evento">A festa; nula sem agenda.</param>
/// <param name="EventoCompleto">Se o convite já pode sair (P6).</param>
/// <param name="Emitidos">Convites válidos.</param>
/// <param name="SemTitular">Válidos sem nome ou documento.</param>
/// <param name="PedidosQuitadosSemConvite">Pedidos pagos esperando a emissão.</param>
/// <param name="PedidosComParcelaDepoisDoFechamento">Pedidos com parcela vencendo depois do fechamento (P2.1).</param>
public sealed record ResumoDosConvitesDTO(
    EventoDoConviteDTO? Evento,
    bool EventoCompleto,
    int Emitidos,
    int SemTitular,
    int PedidosQuitadosSemConvite,
    int PedidosComParcelaDepoisDoFechamento
);

/// <summary>Quantos convites uma emissão deixou valendo, ou quantos pedidos a emissão alcançou.</summary>
/// <param name="Quantidade">Quantidade.</param>
public sealed record EmissaoDTO(int Quantidade);
