using Backend.Business.Abstractions;

namespace Backend.Business.Festa.Models;

/// <summary>
/// Uma mesa do jantar: nome e quantos lugares ela tem, onde fica no salão — e, se foi vendida, de quem ela é.
/// </summary>
/// <remarks>
/// A mesa não sabe quem senta nela (decisão de 23/09/2026, revendo as decisões 1 e 2): a comissão
/// cadastra "Mesa 12, 10 lugares", o formando compra o opcional <c>Mesa</c> e leva a mesa inteira, sem
/// dizer de quem são os lugares. Por isso não há assento, nem mesa impressa no convite.
/// <para>
/// A mesa reservada ("Mesa dos pais") é só uma marca (P3): nunca tem dono e não conta entre as
/// vendáveis. O <c>CHECK</c> do banco garante as duas coisas juntas.
/// </para>
/// <para>
/// O lugar no mapa (28/09/2026) é o centro da mesa em centímetros do <see cref="Salao"/>, e nulo
/// enquanto ela está "fora do mapa". O tamanho não é guardado: sai dos lugares e do formato.
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

    /// <summary>Redonda ou retangular: é o desenho no mapa.</summary>
    public FormatoDaMesa Formato { get; private set; }

    /// <summary>Centro da mesa no salão, da esquerda, em centímetros; nulo fora do mapa.</summary>
    public int? X { get; private set; }

    /// <summary>Centro da mesa no salão, do alto, em centímetros; nulo fora do mapa.</summary>
    public int? Y { get; private set; }

    /// <summary>Mesa retangular em pé no mapa (90°). A redonda ignora.</summary>
    public bool Girada { get; private set; }

    /// <summary>O formando que comprou a mesa (decisão 6); nulo enquanto ninguém a tem.</summary>
    public Guid? VinculoId { get; private set; }

    /// <summary>Uma mesa nova, sem dono e fora do mapa.</summary>
    /// <param name="dados">Identificação, lugares, observação, reserva e formato, já validados.</param>
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
        Formato = dados.Formato;
    }

    /// <summary>Põe a mesa no mapa, ou a tira (<paramref name="x"/> e <paramref name="y"/> nulos).</summary>
    /// <param name="x">Centro, da esquerda.</param>
    /// <param name="y">Centro, do alto.</param>
    /// <param name="girada">Em pé.</param>
    public void Posicionar(int? x, int? y, bool girada)
    {
        X = x;
        Y = y;
        Girada = girada;
    }

    /// <summary>Troca o dono — nulo solta a mesa.</summary>
    /// <param name="vinculoId">Formando que comprou.</param>
    public void DefinirDono(Guid? vinculoId) => VinculoId = vinculoId;
}

/// <summary>O desenho da mesa no mapa.</summary>
public enum FormatoDaMesa
{
    /// <summary>Redonda, com as cadeiras em volta — a de buffet.</summary>
    Redonda,

    /// <summary>Retangular, com as cadeiras nos dois lados compridos.</summary>
    Retangular,
}

/// <summary>O cadastro de uma mesa, como a comissão o digita.</summary>
/// <param name="Identificacao">"Mesa 12".</param>
/// <param name="Lugares">Quantos lugares.</param>
/// <param name="Observacao">Observação livre.</param>
/// <param name="Reservada">Fora da venda (P3).</param>
/// <param name="Formato">Redonda ou retangular.</param>
public sealed record DadosDaMesa(
    string Identificacao,
    int Lugares,
    string? Observacao,
    bool Reservada,
    FormatoDaMesa Formato = FormatoDaMesa.Redonda
);

/// <summary>Uma mesa na lista da Gestão, com o nome do dono e o lugar no mapa.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Identificacao">"Mesa 12".</param>
/// <param name="Lugares">Quantos lugares.</param>
/// <param name="Observacao">Observação.</param>
/// <param name="Reservada">Fora da venda.</param>
/// <param name="VinculoId">Dono; nulo sem dono.</param>
/// <param name="Dono">Nome do dono.</param>
/// <param name="Formato">Redonda ou retangular.</param>
/// <param name="X">Centro no salão; nulo fora do mapa.</param>
/// <param name="Y">Centro no salão; nulo fora do mapa.</param>
/// <param name="Girada">Retangular em pé.</param>
public sealed record MesaResumo(
    Guid Id,
    string Identificacao,
    int Lugares,
    string? Observacao,
    bool Reservada,
    Guid? VinculoId,
    string? Dono,
    FormatoDaMesa Formato,
    int? X,
    int? Y,
    bool Girada
);

/// <summary>
/// Um formando com pedido confirmado do opcional <c>Mesa</c>: quantas comprou e quantas já tem no mapa.
/// </summary>
/// <param name="VinculoId">Formando.</param>
/// <param name="Nome">Nome civil, ou o da conta.</param>
/// <param name="Compradas">Quantidade confirmada nos pedidos de mesa.</param>
/// <param name="Atribuidas">Mesas do mapa que já são dele.</param>
public sealed record CompradorDeMesa(Guid VinculoId, string Nome, int Compradas, int Atribuidas);

/// <summary>
/// O mapa inteiro, numa resposta: a faixa do topo, as mesas, quem comprou e o salão.
/// </summary>
/// <param name="Mesas">Quantas mesas.</param>
/// <param name="Lugares">Soma dos lugares.</param>
/// <param name="Reservadas">Mesas fora da venda.</param>
/// <param name="ComDono">Mesas vendidas já atribuídas.</param>
/// <param name="MesasPorAtribuir">Mesas compradas que ainda não têm mesa no mapa — o trabalho que falta à comissão.</param>
/// <param name="Lista">As mesas, por identificação.</param>
/// <param name="Compradores">Quem comprou mesa, por nome.</param>
/// <param name="Salao">Tamanho do salão e o que há nele além das mesas.</param>
public sealed record MapaDeMesas(
    int Mesas,
    int Lugares,
    int Reservadas,
    int ComDono,
    int MesasPorAtribuir,
    IReadOnlyList<MesaResumo> Lista,
    IReadOnlyList<CompradorDeMesa> Compradores,
    PlantaDoSalao Salao
);
