using Backend.Business.Agenda.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Texto;

namespace Backend.Business.Festa.Models;

/// <summary>O titular de um convite, como quem comprou (ou a Gestão) o informa.</summary>
/// <param name="Nome">Quem vai usar.</param>
/// <param name="TipoDoDocumento">CPF ou RG; nulo enquanto o documento não foi informado.</param>
/// <param name="NumeroDoDocumento">O número — normalizado pelo service antes de gravar.</param>
/// <param name="Email">Para onde mandar o convite; opcional (decisão 17).</param>
public sealed record DadosDoConvidado(string Nome, TipoDeDocumento? TipoDoDocumento, string? NumeroDoDocumento, string? Email);

/// <summary>Uma cortesia, como a Gestão a emite (decisão 14).</summary>
/// <param name="Convidado">Titular — obrigatório: ninguém dá uma cadeira sem saber para quem.</param>
/// <param name="Motivo">Por que — vai para a auditoria, com o autor.</param>
/// <param name="EventoId">Evento da cortesia; nulo é a festa. O paraninfo vai à colação (Sprint 30, P3).</param>
public sealed record DadosDaCortesia(DadosDoConvidado Convidado, string Motivo, Guid? EventoId = null);

/// <summary>A liberação manual de um pedido que ainda não foi quitado (P2).</summary>
/// <param name="PedidoId">Pedido de convite extra.</param>
/// <param name="Motivo">"Pagou 2 de 3, paga o resto na porta" — vai para a auditoria.</param>
public sealed record LiberacaoDeConvites(Guid PedidoId, string Motivo);

/// <summary>Uma entrada marcada na lista sem rede, subindo depois (decisão 16).</summary>
/// <param name="Codigo">Código do convite.</param>
/// <param name="ValidadoEm">Hora em que o aparelho marcou, em UTC.</param>
/// <param name="Aparelho">Identificação do aparelho, para a Gestão saber de onde veio.</param>
public sealed record EntradaSemRede(string Codigo, DateTime ValidadoEm, string? Aparelho);

/// <summary>
/// O evento como o convite o imprime: nome, dia, hora e lugar — e as contas de horário que saem deles.
/// </summary>
/// <remarks>
/// Lido do <c>EventoDaTurma</c> a cada abertura (P6): mudar o local na agenda muda a página do
/// convite na hora, sem reescrever convite nenhum. Data e hora são do calendário da turma; os
/// instantes em UTC que a portaria compara com o relógio saem de <see cref="DataUtils"/>.
/// </remarks>
/// <param name="Id">Evento da agenda.</param>
/// <param name="Tipo">Festa ou colação.</param>
/// <param name="Titulo">Como a turma chama o evento.</param>
/// <param name="Data">O dia.</param>
/// <param name="Hora">A hora; sem ela o convite não sai.</param>
/// <param name="Local">Onde; sem ele o convite não sai.</param>
public sealed record EventoDoConvite(Guid Id, TipoDeEvento Tipo, string Titulo, DateOnly Data, TimeOnly? Hora, string? Local)
{
    /// <summary>Quanto antes do horário a porta abre para validação: montagem e ensaio (P7).</summary>
    public static readonly TimeSpan AntesDaJanela = TimeSpan.FromHours(6);

    /// <summary>Quanto depois do horário ela fecha: festa até de manhã, colação que atrasa (P7).</summary>
    public static readonly TimeSpan DepoisDaJanela = TimeSpan.FromHours(12);

    /// <summary>Quanto antes do evento a lista de convidados fecha (P5).</summary>
    public static readonly TimeSpan AntesDoFechamento = TimeSpan.FromHours(24);

    /// <summary>Se dá para imprimir um convite: hora e local definidos (P6).</summary>
    public bool Completo => Hora is not null && !string.IsNullOrWhiteSpace(Local);

    /// <summary>O instante em que o evento começa, em UTC. Sem hora, a meia-noite do dia.</summary>
    public DateTime InicioEmUtc => DataUtils.ParaUtc(Data.ToDateTime(Hora ?? TimeOnly.MinValue));

    /// <summary>Até quando o formando troca o nome do convidado: 24 h antes (P5).</summary>
    public DateTime FechamentoEmUtc => InicioEmUtc - AntesDoFechamento;

    /// <summary>Quando o botão "Validar entrada" aparece.</summary>
    public DateTime JanelaAbreEmUtc => InicioEmUtc - AntesDaJanela;

    /// <summary>Quando ele some.</summary>
    public DateTime JanelaFechaEmUtc => InicioEmUtc + DepoisDaJanela;

    /// <summary>Se a lista ainda está aberta ao formando.</summary>
    /// <param name="agora">Instante de referência, em UTC.</param>
    public bool ListaAberta(DateTime agora) => agora < FechamentoEmUtc;

    /// <summary>Se a portaria valida entrada agora (P7).</summary>
    /// <param name="agora">Instante de referência, em UTC.</param>
    public bool JanelaAberta(DateTime agora) => agora >= JanelaAbreEmUtc && agora <= JanelaFechaEmUtc;
}

/// <summary>
/// O convite como o convidado o vê: sem login, e por isso magro (decisão 11).
/// </summary>
/// <remarks>
/// Sem nome do formando que comprou, sem valor, sem nada do cadastro de ninguém. O documento vai
/// mascarado — o mesário confere o final contra o documento na mão (P5.1).
/// </remarks>
/// <param name="Turma">Nome da turma.</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Evento">O que, quando e onde.</param>
/// <param name="Codigo">O código para ditar.</param>
/// <param name="Token">Código com a assinatura — é o que vai na URL e no QR.</param>
/// <param name="NomeDoConvidado">Titular. Convite "a definir" não tem página: é vaga paga, não ingresso.</param>
/// <param name="Documento">Documento mascarado (<c>RG ••••456-7</c>), se informado.</param>
public sealed record ConvitePublico(
    string Turma,
    string Instituicao,
    EventoDoConvite Evento,
    string Codigo,
    string Token,
    string NomeDoConvidado,
    string? Documento
);

/// <summary>Um convite do formando, na tela "Meus convites".</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Sequencial">Posição entre os dele: 1, 2, 3.</param>
/// <param name="Codigo">Código.</param>
/// <param name="Token">
/// Código com a assinatura, para o link e o QR. Nulo enquanto "a definir": sem convidado não há o que
/// mandar, e o link que circulasse antes valeria para quem fosse nomeado depois.
/// </param>
/// <param name="NomeDoConvidado">Titular; nulo enquanto "a definir".</param>
/// <param name="TipoDoDocumento">CPF ou RG.</param>
/// <param name="Documento">Documento mascarado.</param>
/// <param name="EmailDoConvidado">Para onde o convite foi mandado.</param>
/// <param name="EmitidoEm">Quando nasceu.</param>
/// <param name="ValidadoEm">Quando entrou, se já entrou.</param>
public sealed record MeuConvite(
    Guid Id,
    int Sequencial,
    string Codigo,
    string? Token,
    string? NomeDoConvidado,
    TipoDeDocumento? TipoDoDocumento,
    string? Documento,
    string? EmailDoConvidado,
    DateTime EmitidoEm,
    DateTime? ValidadoEm
);

/// <summary>Os convites do formando para um evento, e o que ele ainda pode fazer com eles.</summary>
/// <param name="Evento">O evento; nulo quando a agenda ainda não tem a festa.</param>
/// <param name="FechamentoDaLista">Até quando ele troca nomes (P5); nulo sem evento.</param>
/// <param name="ListaAberta">Se ainda pode editar.</param>
/// <param name="Convites">Os válidos, por posição.</param>
/// <param name="AguardandoPagamento">Unidades pedidas que ainda não viraram convite — a parcela em aberto (P2).</param>
public sealed record MeusConvites(
    EventoDoConvite? Evento,
    DateTime? FechamentoDaLista,
    bool ListaAberta,
    IReadOnlyList<MeuConvite> Convites,
    int AguardandoPagamento
);

/// <summary>De onde veio o direito ao convite (decisão 14).</summary>
public enum OrigemDoConvite
{
    /// <summary>Pago num pedido de convite extra.</summary>
    Comprado,

    /// <summary>Concedido por um pacote da cesta do formando (Sprint 47, D14).</summary>
    Pacote,

    /// <summary>Da turma, emitido pela Gestão.</summary>
    Cortesia,

    /// <summary>Vendido na loja pública (Sprint 26).</summary>
    Loja,
}

/// <summary>Como a portaria enxerga um convite agora.</summary>
public enum SituacaoNaPortaria
{
    /// <summary>Pode entrar.</summary>
    Valido,

    /// <summary>Sem nome ou sem documento — pendência da Gestão, não convite anônimo (P5).</summary>
    SemTitular,

    /// <summary>Já entrou.</summary>
    Validado,

    /// <summary>Não vale mais — estorno, cancelamento, transferência ou reemissão.</summary>
    Revogado,

    /// <summary>Do pacote de quem tem parcela em atraso além da carência: não entra até regularizar ou a comissão liberar (D24).</summary>
    Preso,
}

/// <summary>A entrada de um convite: quando, e por quem.</summary>
/// <param name="CheckInId">A linha do check-in, para desfazer.</param>
/// <param name="ValidadoEm">Quando entrou.</param>
/// <param name="ValidadoPor">Nome de quem validou — o "por Ana" da decisão 6.</param>
/// <param name="ValidadoPorUsuarioId">Quem validou — é como a tela sabe que foi "você, agora há pouco".</param>
public sealed record EntradaNaPortaria(Guid CheckInId, DateTime ValidadoEm, string ValidadoPor, Guid ValidadoPorUsuarioId);

/// <summary>Um convite na lista da portaria.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="EventoId">Evento a que pertence.</param>
/// <param name="Codigo">Código.</param>
/// <param name="NomeDoConvidado">Titular.</param>
/// <param name="Documento">Documento mascarado.</param>
/// <param name="ConvidadoDe">Nome do formando dono; nulo na cortesia.</param>
/// <param name="Origem">Comprado, do pacote, da loja ou cortesia.</param>
/// <param name="Situacao">O que a portaria faz com ele.</param>
/// <param name="MotivoDaRevogacao">Por que não vale, quando não vale.</param>
/// <param name="Entrada">A entrada ativa, se houver.</param>
/// <param name="EntrouSemRedeDuasVezes">Se dois aparelhos sem rede deixaram este convite entrar (decisão 16).</param>
public sealed record ConviteNaPortaria(
    Guid Id,
    Guid EventoId,
    string Codigo,
    string? NomeDoConvidado,
    string? Documento,
    string? ConvidadoDe,
    OrigemDoConvite Origem,
    SituacaoNaPortaria Situacao,
    string? MotivoDaRevogacao,
    EntradaNaPortaria? Entrada,
    bool EntrouSemRedeDuasVezes
);

/// <summary>Um convite como a portaria o abre, com a janela da validação.</summary>
/// <param name="Convite">O convite.</param>
/// <param name="Evento">O evento dele.</param>
/// <param name="JanelaAberta">Se o botão "Validar entrada" aparece agora (P7).</param>
public sealed record ConsultaNaPortaria(ConviteNaPortaria Convite, EventoDoConvite Evento, bool JanelaAberta);

/// <summary>A lista da portaria: a faixa de contagem e os convites.</summary>
/// <param name="Evento">Evento aberto na portaria.</param>
/// <param name="Total">Convites que valem ou já entraram.</param>
/// <param name="Validados">Quantos já entraram.</param>
/// <param name="SemTitular">Quantos estão sem nome ou documento.</param>
/// <param name="JanelaAberta">Se a validação está aberta agora.</param>
/// <param name="GeradaEm">Quando a lista foi montada — no modo sem rede, é a idade do que está na tela (decisão 16).</param>
/// <param name="Convites">Os convites, por nome.</param>
public sealed record ListaDaPortaria(
    EventoDoConvite Evento,
    int Total,
    int Validados,
    int SemTitular,
    bool JanelaAberta,
    DateTime GeradaEm,
    IReadOnlyList<ConviteNaPortaria> Convites
);

/// <summary>O que a sincronização da lista sem rede fez com cada entrada.</summary>
/// <param name="Validadas">Viraram check-in.</param>
/// <param name="Repetidas">O convite já tinha entrado: gravadas como tentativa repetida, visíveis à Gestão.</param>
/// <param name="Recusadas">Código que não existe aqui, revogado ou de outro evento.</param>
public sealed record ResultadoDaSincronizacao(int Validadas, int Repetidas, int Recusadas);

/// <summary>A situação dos convites de um evento, para a Gestão e para a agenda.</summary>
/// <param name="Evento">A festa da agenda; nulo se ainda não há.</param>
/// <param name="EventoCompleto">Se hora e local estão definidos — sem isso nenhum convite sai (P6).</param>
/// <param name="Emitidos">Convites válidos.</param>
/// <param name="SemTitular">Válidos sem nome ou documento.</param>
/// <param name="PedidosQuitadosSemConvite">Pedidos pagos que ainda não viraram convite — a agenda incompleta segurou.</param>
/// <param name="PedidosComParcelaDepoisDoFechamento">
/// Pedidos com parcela em aberto vencendo depois do fechamento da lista (P2.1) — o aviso da agenda
/// quando a festa é antecipada.
/// </param>
public sealed record ResumoDosConvites(
    EventoDoConvite? Evento,
    bool EventoCompleto,
    int Emitidos,
    int SemTitular,
    int PedidosQuitadosSemConvite,
    int PedidosComParcelaDepoisDoFechamento
);

/// <summary>O que o convite mostra do documento: o tipo e só o final.</summary>
public static class DocumentoDoConvidado
{
    /// <summary>
    /// Mascarado: <c>CPF ••••••••901-23</c>, <c>RG ••••456-7</c> — os quatro últimos, e nada que
    /// identifique a pessoa para quem não tem o documento na mão.
    /// </summary>
    /// <param name="tipo">CPF ou RG.</param>
    /// <param name="numero">Número gravado.</param>
    public static string? Mascarar(TipoDeDocumento? tipo, string? numero)
    {
        if (tipo is null || string.IsNullOrEmpty(numero))
            return null;

        var final = numero.Length <= 4 ? numero : numero[^4..];

        return $"{Sigla(tipo.Value)} ••••{final}";
    }

    /// <summary>Inteiro e formatado — só na lista exportada pela Gestão (P5.1).</summary>
    /// <param name="tipo">CPF ou RG.</param>
    /// <param name="numero">Número gravado.</param>
    public static string? Inteiro(TipoDeDocumento? tipo, string? numero) =>
        tipo is null || string.IsNullOrEmpty(numero)
            ? null
            : $"{Sigla(tipo.Value)} {(tipo is TipoDeDocumento.Cpf ? FormatosBrasileiros.FormatarCpf(numero) : numero)}";

    /// <summary>
    /// O número como se grava: CPF só com dígitos; RG só com letras e dígitos, em maiúsculas.
    /// </summary>
    /// <param name="tipo">CPF ou RG.</param>
    /// <param name="numero">Como a pessoa digitou.</param>
    public static string? Normalizar(TipoDeDocumento? tipo, string? numero) =>
        string.IsNullOrWhiteSpace(numero) ? null
        : tipo is TipoDeDocumento.Cpf ? FormatosBrasileiros.SomenteDigitos(numero)
        : new string([.. numero.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

    private static string Sigla(TipoDeDocumento tipo) => tipo is TipoDeDocumento.Cpf ? "CPF" : "RG";
}

/// <summary>Um convite válido com o que a página pública imprime da turma e do evento.</summary>
/// <param name="Codigo">Código.</param>
/// <param name="Turma">Nome da turma.</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Evento">Evento, lido da agenda a cada abertura.</param>
/// <param name="NomeDoConvidado">Titular.</param>
/// <param name="TipoDoDocumento">CPF ou RG.</param>
/// <param name="NumeroDoDocumento">Número, decifrado — quem mascara é o service.</param>
public sealed record ConvitePublicoGravado(
    string Codigo,
    string Turma,
    string Instituicao,
    EventoDoConvite Evento,
    string? NomeDoConvidado,
    TipoDeDocumento? TipoDoDocumento,
    string? NumeroDoDocumento
);

/// <summary>Um convite do vínculo, com o documento ainda inteiro — quem mascara é o service.</summary>
/// <param name="Convite">O convite.</param>
/// <param name="ValidadoEm">Quando entrou, se já entrou.</param>
public sealed record ConviteDoVinculo(ConviteDoEvento Convite, DateTime? ValidadoEm);

/// <summary>Um convite na portaria, com o documento ainda inteiro — a lista exportada o imprime assim.</summary>
/// <param name="Convite">O convite.</param>
/// <param name="ConvidadoDe">Nome do dono; nulo na cortesia.</param>
/// <param name="Entrada">A entrada ativa, se houver.</param>
/// <param name="EntrouSemRedeDuasVezes">Se há tentativa repetida sem rede registrada.</param>
/// <param name="Preso">Convite de pacote cujo dono tem parcela vencida além da carência, sem liberação da comissão (D24).</param>
public sealed record ConviteGravadoNaPortaria(
    ConviteDoEvento Convite,
    string? ConvidadoDe,
    EntradaNaPortaria? Entrada,
    bool EntrouSemRedeDuasVezes,
    bool Preso = false
);
