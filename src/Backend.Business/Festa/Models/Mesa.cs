using Backend.Business.Abstractions;

namespace Backend.Business.Festa.Models;

/// <summary>
/// Uma mesa do jantar: nome e quantos lugares ela tem — e, se foi vendida, de quem ela é.
/// </summary>
/// <remarks>
/// A mesa não sabe quem senta nela (decisão de 23/09/2026, revendo as decisões 1 e 2): a comissão
/// cadastra "Mesa 12, 10 lugares", o formando compra o opcional <c>Mesa</c> e leva a mesa inteira, sem
/// dizer de quem são os lugares. Por isso não há assento, nem mesa impressa no convite.
/// <para>
/// A mesa reservada ("Mesa dos pais") é só uma marca (P3): nunca tem dono e não conta entre as
/// vendáveis. O <c>CHECK</c> do banco garante as duas coisas juntas.
/// </para>
/// </remarks>
public class Mesa : EntidadeDaFormatura
{
    /// <summary>Como a turma chama a mesa: "Mesa 12", "Mesa dos pais".</summary>
    public string Identificacao { get; private set; } = string.Empty;

    /// <summary>Quantos lugares ela tem.</summary>
    public int Lugares { get; private set; }

    /// <summary>Observação livre da comissão — "perto da pista".</summary>
    public string? Observacao { get; private set; }

    /// <summary>Reservada pela comissão: fora da venda, sem dono (P3).</summary>
    public bool Reservada { get; private set; }

    /// <summary>O formando que comprou a mesa (decisão 6); nulo enquanto ninguém a tem.</summary>
    public Guid? VinculoId { get; private set; }

    /// <summary>Uma mesa nova, sem dono.</summary>
    /// <param name="dados">Identificação, lugares, observação e reserva, já validados.</param>
    public static Mesa Nova(DadosDaMesa dados)
    {
        var mesa = new Mesa();
        mesa.Aplicar(dados);

        return mesa;
    }

    /// <summary>Grava o cadastro. Marcar como reservada uma mesa com dono é recusado antes, pelo service.</summary>
    /// <param name="dados">Dados novos, já validados.</param>
    public void Aplicar(DadosDaMesa dados)
    {
        Identificacao = dados.Identificacao.Trim();
        Lugares = dados.Lugares;
        Observacao = string.IsNullOrWhiteSpace(dados.Observacao) ? null : dados.Observacao.Trim();
        Reservada = dados.Reservada;
    }

    /// <summary>Troca o dono — nulo solta a mesa.</summary>
    /// <param name="vinculoId">Formando que comprou.</param>
    public void DefinirDono(Guid? vinculoId) => VinculoId = vinculoId;
}

/// <summary>O cadastro de uma mesa, como a comissão o digita.</summary>
/// <param name="Identificacao">"Mesa 12".</param>
/// <param name="Lugares">Quantos lugares.</param>
/// <param name="Observacao">Observação livre.</param>
/// <param name="Reservada">Fora da venda (P3).</param>
public sealed record DadosDaMesa(string Identificacao, int Lugares, string? Observacao, bool Reservada);

/// <summary>Uma mesa na lista da Gestão, com o nome do dono.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Identificacao">"Mesa 12".</param>
/// <param name="Lugares">Quantos lugares.</param>
/// <param name="Observacao">Observação.</param>
/// <param name="Reservada">Fora da venda.</param>
/// <param name="VinculoId">Dono; nulo sem dono.</param>
/// <param name="Dono">Nome do dono.</param>
public sealed record MesaResumo(Guid Id, string Identificacao, int Lugares, string? Observacao, bool Reservada, Guid? VinculoId, string? Dono);

/// <summary>
/// Um formando com pedido confirmado do opcional <c>Mesa</c>: quantas comprou e quantas já tem no mapa.
/// </summary>
/// <param name="VinculoId">Formando.</param>
/// <param name="Nome">Nome civil, ou o da conta.</param>
/// <param name="Compradas">Quantidade confirmada nos pedidos de mesa.</param>
/// <param name="Atribuidas">Mesas do mapa que já são dele.</param>
public sealed record CompradorDeMesa(Guid VinculoId, string Nome, int Compradas, int Atribuidas);

/// <summary>
/// O mapa inteiro, numa resposta: a faixa do topo, as mesas e quem comprou.
/// </summary>
/// <param name="Mesas">Quantas mesas.</param>
/// <param name="Lugares">Soma dos lugares.</param>
/// <param name="Reservadas">Mesas fora da venda.</param>
/// <param name="ComDono">Mesas vendidas já atribuídas.</param>
/// <param name="MesasPorAtribuir">Mesas compradas que ainda não têm mesa no mapa — o trabalho que falta à comissão.</param>
/// <param name="Lista">As mesas, por identificação.</param>
/// <param name="Compradores">Quem comprou mesa, por nome.</param>
public sealed record MapaDeMesas(
    int Mesas,
    int Lugares,
    int Reservadas,
    int ComDono,
    int MesasPorAtribuir,
    IReadOnlyList<MesaResumo> Lista,
    IReadOnlyList<CompradorDeMesa> Compradores
);
