using Backend.Business.Abstractions;

namespace Backend.Business.Festa.Models;

/// <summary>
/// Um convite para um evento da agenda: uma pessoa, um código, uma entrada.
/// </summary>
/// <remarks>
/// Do <b>evento</b>, e não da festa (decisão 13): a colação precisa do mesmo controle na porta, e o
/// que muda entre as duas é só de onde vem o direito. As três origens se leem pelas colunas, sem
/// campo de tipo (decisão 14):
/// <list type="bullet">
/// <item>comprado — <see cref="PedidoId"/> e <see cref="VinculoId"/> preenchidos, nasce na quitação;</item>
/// <item>do pacote — só <see cref="VinculoId"/>: concedido pela cesta do formando (Sprint 47, D14);</item>
/// <item>vendido na loja — só <see cref="CompraId"/> (Sprint 26), nasce na confirmação do pagamento;</item>
/// <item>cortesia — os três nulos: é convidado da turma, emitido pela Gestão.</item>
/// </list>
/// <para>
/// <b>Uma linha por unidade</b>, nunca um convite com quantidade (P1): quem leva o da avó leva dois
/// convites, cada um com nome e QR próprios. Trocar o titular de um convite já nomeado não edita a
/// linha: ela é revogada e nasce outra, com código novo, no mesmo <see cref="Sequencial"/> (decisão
/// 17) — quem recebeu o convite antes fica com um QR que a portaria mostra como revogado.
/// </para>
/// <para>
/// Documento e e-mail do convidado são dado pessoal de terceiro: cifrados no banco, mascarados em
/// toda tela que não seja a lista exportada pela Gestão, e apagados 30 dias depois do evento (P5.1).
/// O nome fica — é o histórico da festa.
/// </para>
/// </remarks>
public class ConviteDoEvento : EntidadeDaFormatura
{
    /// <summary>O evento da agenda — a festa, ou a colação.</summary>
    public Guid EventoId { get; private set; }

    /// <summary>Formando dono do convite. Nulo na cortesia.</summary>
    public Guid? VinculoId { get; private set; }

    /// <summary>Pedido que pagou por ele. Nulo no do pacote e na cortesia.</summary>
    public Guid? PedidoId { get; private set; }

    /// <summary>A compra da loja pública que pagou por ele (Sprint 26). Nula nas outras origens.</summary>
    public Guid? CompraId { get; private set; }

    /// <summary>
    /// A posição do convite entre os do mesmo dono no mesmo evento: 1, 2, 3.
    /// </summary>
    /// <remarks>
    /// É a chave natural da emissão em lote (decisão 12): emitir de novo os convites 1 a N de um
    /// pedido esbarra no índice único e não cria nada.
    /// </remarks>
    public int Sequencial { get; private set; }

    /// <summary>O código que se dita na porta: <c>MED27-7QK4</c> (decisão 4).</summary>
    public string Codigo { get; private set; } = string.Empty;

    /// <summary>Quem vai usar o convite. Nulo enquanto "a definir" (P1).</summary>
    public string? NomeDoConvidado { get; private set; }

    /// <summary>CPF ou RG, junto com o número (P5.1).</summary>
    public TipoDeDocumento? TipoDoDocumento { get; private set; }

    /// <summary>O número do documento, só com os caracteres que importam. Cifrado no banco.</summary>
    public string? NumeroDoDocumento { get; private set; }

    /// <summary>E-mail do convidado, para mandar o convite a ele. Cifrado no banco.</summary>
    public string? EmailDoConvidado { get; private set; }

    /// <summary>
    /// O que a comissão precisa saber do convidado. Cifrada no banco e
    /// apagada com o documento (P5.1): pode ser dado de saúde de terceiro.
    /// </summary>
    public string? Observacoes { get; private set; }

    /// <summary>Quando nasceu, em UTC.</summary>
    public DateTime EmitidoEm { get; private set; } = DateTime.UtcNow;

    /// <summary>Quando deixou de valer, em UTC. Nulo no convite válido.</summary>
    public DateTime? RevogadoEm { get; private set; }

    /// <summary>Por que deixou de valer — a portaria mostra em vermelho (decisão 8).</summary>
    public string? MotivoDaRevogacao { get; private set; }

    /// <summary>Se ainda vale.</summary>
    public bool Valido => RevogadoEm is null;

    /// <summary>
    /// Quando a comissão soltou o convite do pacote preso por atraso (Sprint 47, D24). Nulo: a trava vale.
    /// </summary>
    /// <remarks>
    /// A trava não é gravada: é lida na portaria, das parcelas do dono. O que se grava é só a exceção da comissão —
    /// regularizar o pagamento solta o convite sem ninguém precisar lembrar.
    /// </remarks>
    public DateTime? LiberadoEm { get; private set; }

    /// <summary>De onde veio o direito, lido das colunas (decisão 14).</summary>
    public OrigemDoConvite Origem =>
        PedidoId is not null ? OrigemDoConvite.Comprado
        : CompraId is not null ? OrigemDoConvite.Loja
        : VinculoId is not null ? OrigemDoConvite.Pacote
        : OrigemDoConvite.Cortesia;

    /// <summary>Se já tem titular: nome e documento.</summary>
    /// <remarks>No fechamento da lista, convite sem isso é pendência da Gestão, não convite anônimo válido (P5).</remarks>
    public bool Nomeado => NomeDoConvidado is not null && NumeroDoDocumento is not null;

    /// <summary>Uma cortesia da turma: nome obrigatório, sem dono e sem pedido (decisão 14).</summary>
    /// <param name="eventoId">Evento.</param>
    /// <param name="codigo">Código já sorteado.</param>
    /// <param name="convidado">Titular.</param>
    public static ConviteDoEvento Cortesia(Guid eventoId, string codigo, DadosDoConvidado convidado)
    {
        var convite = new ConviteDoEvento { EventoId = eventoId, Codigo = codigo };
        convite.Aplicar(convidado);

        return convite;
    }

    /// <summary>
    /// O convite que substitui este, com código novo e o titular informado.
    /// </summary>
    /// <remarks>
    /// Mesmo evento, dono, pedido e posição: para o dono, é o mesmo convite com outro nome. Quem
    /// revoga este é o chamador, antes — o índice único só aceita um válido por posição.
    /// </remarks>
    /// <param name="codigo">Código novo.</param>
    /// <param name="convidado">Titular; nulo mantém o atual (reemissão).</param>
    public ConviteDoEvento Substituto(string codigo, DadosDoConvidado? convidado)
    {
        var novo = new ConviteDoEvento
        {
            EventoId = EventoId,
            VinculoId = VinculoId,
            PedidoId = PedidoId,
            CompraId = CompraId,
            Sequencial = Sequencial,
            Codigo = codigo,
            NomeDoConvidado = NomeDoConvidado,
            TipoDoDocumento = TipoDoDocumento,
            NumeroDoDocumento = NumeroDoDocumento,
            EmailDoConvidado = EmailDoConvidado,
            Observacoes = Observacoes,
        };

        if (convidado is not null)
            novo.Aplicar(convidado);

        return novo;
    }

    /// <summary>
    /// Se gravar este titular é uma <b>transferência</b>: o convite já tinha dono, e ele muda.
    /// </summary>
    /// <remarks>
    /// A primeira nomeação de um convite "a definir" não é: ninguém recebeu nada ainda (decisão 17).
    /// Completar o documento de quem já estava nomeado também não — é o mesmo convidado.
    /// </remarks>
    /// <param name="convidado">Titular pretendido.</param>
    public bool TrocaDeTitular(DadosDoConvidado convidado) =>
        NomeDoConvidado is not null
        && (
            !string.Equals(NomeDoConvidado, convidado.Nome.Trim(), StringComparison.OrdinalIgnoreCase)
            || (NumeroDoDocumento is not null && (NumeroDoDocumento != convidado.NumeroDoDocumento || TipoDoDocumento != convidado.TipoDoDocumento))
        );

    /// <summary>Grava o titular, sem trocar o código.</summary>
    /// <param name="convidado">Nome, documento e e-mail já validados e normalizados.</param>
    public void Aplicar(DadosDoConvidado convidado)
    {
        NomeDoConvidado = convidado.Nome.Trim();
        TipoDoDocumento = convidado.NumeroDoDocumento is null ? null : convidado.TipoDoDocumento;
        NumeroDoDocumento = convidado.NumeroDoDocumento;
        EmailDoConvidado = string.IsNullOrWhiteSpace(convidado.Email) ? null : convidado.Email.Trim();
        Observacoes = string.IsNullOrWhiteSpace(convidado.Observacoes) ? null : convidado.Observacoes.Trim();
    }

    /// <summary>Solta o convite preso por atraso. Liberar de novo não muda a hora.</summary>
    /// <param name="agora">Instante, em UTC.</param>
    public void Liberar(DateTime agora) => LiberadoEm ??= agora;

    /// <summary>Tira a validade do convite. Revogar de novo não muda o motivo nem a hora.</summary>
    /// <param name="motivo">O que a portaria vai ler.</param>
    /// <param name="agora">Instante, em UTC.</param>
    /// <returns>Se revogou agora — falso quando já estava revogado.</returns>
    public bool Revogar(string motivo, DateTime agora)
    {
        if (RevogadoEm is not null)
            return false;

        RevogadoEm = agora;
        MotivoDaRevogacao = motivo;

        return true;
    }
}

/// <summary>O documento que o convidado apresenta na porta.</summary>
/// <remarks>Gravado como texto, como todo enum do produto que vai para o banco.</remarks>
public enum TipoDeDocumento
{
    /// <summary>CPF, conferido pelo dígito verificador.</summary>
    Cpf,

    /// <summary>RG — o número varia por estado, então só a forma é conferida.</summary>
    Rg,
}

/// <summary>
/// Uma entrada validada na portaria — ou a tentativa repetida de uma.
/// </summary>
/// <remarks>
/// Check-in é fato, não status (decisão 6): uma linha por validação, e desfazer não apaga, marca
/// <see cref="DesfeitoEm"/>. O índice único parcial sobre <c>convite_id WHERE desfeito_em IS NULL</c>
/// garante uma entrada ativa por convite e preserva o histórico inteiro (decisão 12).
/// <para>
/// A segunda entrada de um convite que dois aparelhos sem rede validaram não é descartada (decisão
/// 16): ela sobe como linha já desfeita, com <see cref="Motivo"/> <c>duplicada_offline</c>, e aparece
/// para a Gestão.
/// </para>
/// </remarks>
public class CheckIn : EntidadeDaFormatura
{
    /// <summary>Motivo da linha que registra a entrada repetida sem rede.</summary>
    public const string DuplicadaOffline = "duplicada_offline";

    /// <summary>O convite validado.</summary>
    public Guid ConviteId { get; private set; }

    /// <summary>Quem validou.</summary>
    public Guid ValidadoPorUsuarioId { get; private set; }

    /// <summary>Quando a pessoa entrou, em UTC — no modo sem rede, a hora do aparelho, não a da sincronização.</summary>
    public DateTime ValidadoEm { get; private set; }

    /// <summary>Qual aparelho marcou, quando veio da lista sem rede.</summary>
    public string? Aparelho { get; private set; }

    /// <summary>Quando deixou de valer, em UTC.</summary>
    public DateTime? DesfeitoEm { get; private set; }

    /// <summary>Quem desfez.</summary>
    public Guid? DesfeitoPorUsuarioId { get; private set; }

    /// <summary>Por que a linha nasceu ou ficou desfeita — <see cref="DuplicadaOffline"/>, ou nulo.</summary>
    public string? Motivo { get; private set; }

    /// <summary>
    /// A segunda entrada de um convite que já tinha entrado, vinda da lista sem rede (decisão 16).
    /// </summary>
    /// <remarks>
    /// Nasce desfeita — o índice único só aceita uma entrada ativa, e a primeira é a que vale —, mas
    /// nasce: é o registro de que o convite entrou duas vezes, visível para a Gestão.
    /// </remarks>
    /// <param name="conviteId">Convite.</param>
    /// <param name="usuarioId">Quem sincronizou.</param>
    /// <param name="validadoEm">Hora em que o aparelho marcou.</param>
    /// <param name="aparelho">Aparelho de origem.</param>
    /// <param name="agora">Hora da sincronização.</param>
    public static CheckIn Repetida(Guid conviteId, Guid usuarioId, DateTime validadoEm, string? aparelho, DateTime agora) =>
        new()
        {
            ConviteId = conviteId,
            ValidadoPorUsuarioId = usuarioId,
            ValidadoEm = validadoEm,
            Aparelho = aparelho,
            DesfeitoEm = agora,
            DesfeitoPorUsuarioId = usuarioId,
            Motivo = DuplicadaOffline,
        };

    /// <summary>Desfaz a entrada. Desfazer de novo não muda nada.</summary>
    /// <param name="usuarioId">Quem desfaz.</param>
    /// <param name="agora">Instante, em UTC.</param>
    public bool Desfazer(Guid usuarioId, DateTime agora)
    {
        if (DesfeitoEm is not null)
            return false;

        DesfeitoEm = agora;
        DesfeitoPorUsuarioId = usuarioId;

        return true;
    }
}
