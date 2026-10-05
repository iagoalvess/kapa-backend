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

    /// <summary>Lugares do auditório ou do salão — o painel de convites avisa quando passa, mas não bloqueia (Sprint 30, decisão 3).</summary>
    public int? Capacidade { get; private set; }

    /// <summary>Se saiu do que vai acontecer — continua na lista, com o selo.</summary>
    public bool Cancelado => Situacao is SituacaoDoEvento.Cancelado;

    /// <summary>Grava a capacidade do local.</summary>
    /// <param name="capacidade">Lugares; nulo é "não sei".</param>
    public void DefinirCapacidade(int? capacidade) => Capacidade = capacidade;

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
