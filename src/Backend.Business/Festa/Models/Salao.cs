using Backend.Business.Abstractions;

namespace Backend.Business.Festa.Models;

/// <summary>
/// O salão do jantar como a comissão o desenha: o tamanho e o que há nele além das mesas.
/// </summary>
/// <remarks>
/// Um por turma (28/09/2026). Palco, pista e entrada não têm regra, dono nem histórico — são desenho —,
/// então moram numa coluna <c>jsonb</c> do salão, e não numa tabela: o mapa inteiro se lê e se grava
/// de uma vez. As mesas continuam linhas próprias, porque têm dono e <c>CHECK</c>.
/// <para>
/// A medida é o centímetro, e ela é ilustrativa: ninguém mede o salão, mas a proporção entre uma mesa
/// de 10 e a pista precisa fazer sentido, e o centímetro dá isso sem conversão de escala.
/// </para>
/// <para>
/// ponytail: sem planta de fundo importada, sem parede em polígono e sem setor com mesas dentro (P7 e P8
/// da Sprint 27) — o salão é um retângulo, e a "Área" com nome e cor faz o papel do setor.
/// </para>
/// </remarks>
public class Salao : EntidadeDaFormatura
{
    /// <summary>Largura do salão, em centímetros.</summary>
    public int Largura { get; private set; }

    /// <summary>Profundidade do salão, em centímetros.</summary>
    public int Altura { get; private set; }

    /// <summary>Palco, pista, entrada e o resto, na ordem de desenho.</summary>
    public List<ElementoDoSalao> Elementos { get; private set; } = [];

    /// <summary>O salão da turma, desenhado pela primeira vez.</summary>
    /// <param name="planta">Tamanho e elementos, já validados.</param>
    public static Salao Novo(PlantaDoSalao planta)
    {
        var salao = new Salao();
        salao.Redesenhar(planta);

        return salao;
    }

    /// <summary>Troca o desenho inteiro: o mapa é salvo de uma vez.</summary>
    /// <param name="planta">Tamanho e elementos, já validados.</param>
    public void Redesenhar(PlantaDoSalao planta)
    {
        Largura = planta.Largura;
        Altura = planta.Altura;
        Elementos = [.. planta.Elementos.Select(elemento => elemento with { Rotulo = elemento.Rotulo.Trim() })];
    }
}

/// <summary>O que um elemento do salão representa: dá o ícone e o desenho.</summary>
/// <remarks>
/// ponytail: lista fixa, porque ícone é código. O que não cabe nela vira uma <see cref="Area"/> com
/// o nome que a comissão quiser ("Cabine de fotos", "Família").
/// </remarks>
public enum TipoDeElemento
{
    /// <summary>O palco da banda ou da cerimônia.</summary>
    Palco,

    /// <summary>A pista de dança.</summary>
    Pista,

    /// <summary>O bar.</summary>
    Bar,

    /// <summary>A mesa do buffet.</summary>
    Buffet,

    /// <summary>Por onde se entra.</summary>
    Entrada,

    /// <summary>Por onde se sai — a saída de emergência também.</summary>
    Saida,

    /// <summary>Os banheiros.</summary>
    Banheiro,

    /// <summary>O DJ ou o som.</summary>
    Som,

    /// <summary>Um trecho do salão com nome e cor: "Família", "Próximo ao palco".</summary>
    Area,
}

/// <summary>As cores que uma área pode ter — as do mapa, e só elas.</summary>
public enum CorDaArea
{
    /// <summary>Laranja claro.</summary>
    Laranja,

    /// <summary>Amarelo claro.</summary>
    Amarelo,

    /// <summary>Lilás.</summary>
    Lilas,

    /// <summary>Cinza.</summary>
    Cinza,
}

/// <summary>Um retângulo do salão: palco, pista, entrada, área.</summary>
/// <param name="Tipo">O que ele é.</param>
/// <param name="Rotulo">O nome escrito no mapa: "Palco", "Família".</param>
/// <param name="X">Canto esquerdo, em centímetros.</param>
/// <param name="Y">Canto de cima, em centímetros.</param>
/// <param name="Largura">Largura, em centímetros.</param>
/// <param name="Altura">Altura, em centímetros.</param>
/// <param name="Cor">Cor da área; nula no resto.</param>
public sealed record ElementoDoSalao(TipoDeElemento Tipo, string Rotulo, int X, int Y, int Largura, int Altura, CorDaArea? Cor);

/// <summary>O salão como se lê e se grava: tamanho e elementos.</summary>
/// <param name="Largura">Largura, em centímetros.</param>
/// <param name="Altura">Profundidade, em centímetros.</param>
/// <param name="Elementos">Palco, pista e o resto.</param>
public sealed record PlantaDoSalao(int Largura, int Altura, IReadOnlyList<ElementoDoSalao> Elementos)
{
    /// <summary>O salão de quem ainda não desenhou: 24 m × 16 m, vazio — cabe umas 30 mesas de 10.</summary>
    public static readonly PlantaDoSalao Padrao = new(2400, 1600, []);
}

/// <summary>Onde fica uma mesa: o centro, ou nulo para tirá-la do mapa.</summary>
/// <param name="MesaId">Mesa.</param>
/// <param name="X">Centro, da esquerda; nulo fora do mapa.</param>
/// <param name="Y">Centro, do alto; nulo fora do mapa.</param>
/// <param name="Girada">Retangular em pé.</param>
public sealed record PosicaoDaMesa(Guid MesaId, int? X, int? Y, bool Girada);

/// <summary>O que o botão "Salvar mapa" grava de uma vez: o salão e onde fica cada mesa.</summary>
/// <param name="Planta">Tamanho e elementos.</param>
/// <param name="Posicoes">As mesas que mudaram de lugar; a que não vem fica onde está.</param>
public sealed record DesenhoDoSalao(PlantaDoSalao Planta, IReadOnlyList<PosicaoDaMesa> Posicoes);

/// <summary>Uma mesa no mapa do formando: sem o nome do dono, com a marca de "é sua".</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Identificacao">"Mesa 12".</param>
/// <param name="Lugares">Quantos lugares.</param>
/// <param name="Reservada">Fora da venda.</param>
/// <param name="Formato">Redonda ou retangular.</param>
/// <param name="X">Centro; nulo fora do mapa.</param>
/// <param name="Y">Centro; nulo fora do mapa.</param>
/// <param name="Girada">Retangular em pé.</param>
/// <param name="Minha">A mesa é de quem lê.</param>
public sealed record MesaNoSalao(
    Guid Id,
    string Identificacao,
    int Lugares,
    bool Reservada,
    FormatoDaMesa Formato,
    int? X,
    int? Y,
    bool Girada,
    bool Minha
);

/// <summary>O mapa como o formando o vê (28/09/2026): o salão, as mesas sem dono à mostra e a dele marcada.</summary>
/// <param name="Salao">Tamanho e elementos.</param>
/// <param name="Mesas">Todas as mesas da turma.</param>
public sealed record SalaoDoFormando(PlantaDoSalao Salao, IReadOnlyList<MesaNoSalao> Mesas);
