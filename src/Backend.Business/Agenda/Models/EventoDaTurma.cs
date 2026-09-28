using Backend.Business.Abstractions;

namespace Backend.Business.Agenda.Models;

/// <summary>
/// Uma data da turma: a colação, a festa, a reunião de quinta, o prazo de escolher o buffet.
/// </summary>
/// <remarks>
/// É a dona das datas da formatura (decisão 1). Até a Sprint 19, colação e festa eram duas colunas
/// da <c>Formatura</c> lidas por quatro telas como se fossem calendário — uma agenda de dois itens
/// escrita como cadastro, e por isso sem lugar para o terceiro. Agora são dois eventos como
/// qualquer outro, e o que a <c>FormaturaDetalhe</c> devolve é projeção destas linhas.
/// <para>
/// <b>Data local, não instante.</b> <see cref="Data"/> é <see cref="DateOnly"/> e <see cref="Hora"/>
/// é <see cref="TimeOnly"/>: um evento é "19h no ateliê", e continua sendo 19h de onde quer que a
/// pessoa abra o app. Guardar UTC aqui criaria a reunião das 19h aparecendo às 22h (decisão 3).
/// </para>
/// </remarks>
public class EventoDaTurma : EntidadeDaFormatura
{
    /// <summary>O que é ("Prova da beca", "Reunião da comissão").</summary>
    public string Titulo { get; private set; } = string.Empty;

    /// <summary>Que tipo de data é. Colação e festa são únicas por turma.</summary>
    public TipoDeEvento Tipo { get; private set; }

    /// <summary>Se está confirmada, a confirmar ou cancelada.</summary>
    public SituacaoDoEvento Situacao { get; private set; } = SituacaoDoEvento.AConfirmar;

    /// <summary>O dia, no fuso da turma.</summary>
    public DateOnly Data { get; private set; }

    /// <summary>A hora, quando importa. Nulo é evento de dia inteiro (P5).</summary>
    public TimeOnly? Hora { get; private set; }

    /// <summary>Onde é. Nulo: a comissão ainda não disse.</summary>
    public string? Local { get; private set; }

    /// <summary>O que mais a turma precisa saber. Texto simples, não Markdown.</summary>
    public string? Descricao { get; private set; }

    /// <summary>Quantos convites cada formando ativo recebe (Sprint 30). Nulo: o evento não tem cota.</summary>
    public int? CotaPorFormando { get; private set; }

    /// <summary>Lugares do auditório — a conta da cota avisa quando passa, mas não bloqueia (decisão 3).</summary>
    public int? Capacidade { get; private set; }

    /// <summary>
    /// Quando a comissão abriu a cota pela primeira vez, em UTC. Nulo: nenhum convite de cota saiu.
    /// </summary>
    /// <remarks>
    /// É o que faz o formando que entra depois receber os dele na entrada (P1), e o que impede a cota
    /// de diminuir: convite já emitido não se desfaz por mudança de número.
    /// </remarks>
    public DateTime? CotaAbertaEm { get; private set; }

    /// <summary>Se saiu do que vai acontecer — continua na lista, com o selo.</summary>
    public bool Cancelado => Situacao is SituacaoDoEvento.Cancelado;

    /// <summary>
    /// Grava a cota e a capacidade.
    /// </summary>
    /// <remarks>
    /// Depois de aberta, a cota só sobe: descer deixaria convites emitidos além dela, e revogar convite
    /// nomeado por mudança de número é o tipo de surpresa que a porta descobre.
    /// </remarks>
    /// <param name="cota">Convites por formando; nulo tira a cota de um evento que ainda não a abriu.</param>
    /// <param name="capacidade">Lugares; nulo é "não sei".</param>
    public Result DefinirCota(int? cota, int? capacidade)
    {
        if (CotaAbertaEm is not null && (cota ?? 0) < CotaPorFormando)
            return Result.Falha(
                Erro.Conflito(
                    "festa.cota_ja_aberta",
                    $"Os convites já foram emitidos com {CotaPorFormando} por formando. Depois de aberta, a cota só aumenta."
                )
            );

        CotaPorFormando = cota;
        Capacidade = capacidade;

        return Result.Ok();
    }

    /// <summary>Marca a abertura da cota. Reabrir não muda a data da primeira vez.</summary>
    /// <param name="agora">Instante, em UTC.</param>
    public void AbrirCota(DateTime agora) => CotaAbertaEm ??= agora;

    /// <summary>Um evento novo.</summary>
    /// <param name="dados">Dados já validados.</param>
    public static EventoDaTurma Novo(DadosDoEvento dados)
    {
        var evento = new EventoDaTurma();

        evento.Aplicar(dados);

        return evento;
    }

    /// <summary>
    /// Grava o que a comissão digitou.
    /// </summary>
    /// <remarks>
    /// Sem transição a proteger: o evento cancelado <b>aceita</b> correção, porque é assim que se
    /// desmarca e remarca uma data — o contrário obrigaria a reativar antes de mover, que é um
    /// passo a mais para dizer a mesma coisa.
    /// </remarks>
    /// <param name="dados">Dados já validados.</param>
    public void Aplicar(DadosDoEvento dados)
    {
        Titulo = dados.Titulo.Trim();
        Tipo = dados.Tipo;
        Situacao = dados.Situacao;
        Data = dados.Data;
        Hora = dados.Hora;
        Local = Vazio(dados.Local?.Trim());
        Descricao = Vazio(dados.Descricao?.Trim());
    }

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto;
}
