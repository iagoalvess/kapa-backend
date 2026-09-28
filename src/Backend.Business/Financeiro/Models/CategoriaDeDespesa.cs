namespace Backend.Business.Financeiro.Models;

/// <summary>
/// No que a turma gasta.
/// </summary>
/// <remarks>
/// Lista fechada de propósito: categoria livre vira "Buffet", "buffet", "Buffett" e "Comida" na
/// mesma turma, e o quadro por categoria do caixa deixa de somar. Gravada como texto — renomear um
/// valor aqui é migration, não refatoração.
/// <para>
/// ponytail: sem categoria própria por turma. <see cref="Outros"/> com a descrição da despesa cobre
/// o que a lista não prevê; categoria configurável quando alguma comissão pedir.
/// </para>
/// </remarks>
public enum CategoriaDeDespesa
{
    /// <summary>Comida e bebida da festa.</summary>
    Buffet,

    /// <summary>Aluguel do salão, do clube, do espaço da colação.</summary>
    Espaco,

    /// <summary>Banda, DJ, som e iluminação.</summary>
    Banda,

    /// <summary>Fotografia e filmagem.</summary>
    Fotografia,

    /// <summary>Decoração e cenografia.</summary>
    Decoracao,

    /// <summary>Convites, arte e impressão.</summary>
    Convites,

    /// <summary>Beca, capelo e canudo.</summary>
    Beca,

    /// <summary>Taxas: cartório, banco, tributos, a licença do próprio Kapa.</summary>
    Taxas,

    /// <summary>O que não cabe nas demais.</summary>
    Outros,
}

/// <summary>
/// O nome da categoria por extenso, para o documento impresso e a planilha.
/// </summary>
/// <remarks>
/// O valor do enum trafega como está no contrato — a tela tem a própria tabela de rótulos. Aqui é
/// para onde não há tela: o PDF do balancete e o CSV que o contador abre, onde "Espaco" e "Decoracao"
/// sem acento passariam por erro de digitação nosso.
/// </remarks>
public static class RotuloDaCategoria
{
    /// <summary>O nome da categoria em português.</summary>
    /// <param name="categoria">Categoria.</param>
    public static string De(CategoriaDeDespesa categoria) =>
        categoria switch
        {
            CategoriaDeDespesa.Espaco => "Espaço",
            CategoriaDeDespesa.Decoracao => "Decoração",
            _ => categoria.ToString(),
        };

    /// <summary>O nome da categoria de receita em português.</summary>
    /// <param name="categoria">Categoria.</param>
    public static string De(CategoriaDeOutraReceita categoria) =>
        categoria switch
        {
            CategoriaDeOutraReceita.Patrocinio => "Patrocínio",
            CategoriaDeOutraReceita.Evento => "Evento de arrecadação",
            CategoriaDeOutraReceita.Doacao => "Doação",
            CategoriaDeOutraReceita.VendaDeConvite => "Venda de convite",
            _ => categoria.ToString(),
        };
}
