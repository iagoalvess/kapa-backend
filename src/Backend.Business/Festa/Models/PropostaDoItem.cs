using Backend.Business.Abstractions;

namespace Backend.Business.Festa.Models;

/// <summary>
/// Uma candidata a ser contratada para um item: "Banda X, R$ 8.000, quatro horas de show".
/// </summary>
/// <remarks>
/// Decisão 16: o item "a contratar" é o mapeamento da comissão, e até aqui as candidatas só cabiam
/// em prosa, dentro do <see cref="ItemDaFesta.OQueInclui"/>. Prosa não se compara e não
/// vira despesa — três bandas num parágrafo são três bandas que ninguém consegue escolher em tela.
/// <para>
/// A proposta <b>não</b> é fornecedor cadastrado nem cotação formal: não tem contato, não tem
/// documento e não tem prazo de validade. É o que a comissão levantou no grupo, escrito onde a turma
/// possa ler. Quem for contratado vira <c>Fornecedor</c> no lançamento da despesa, e é de lá
/// que o nome no cartão continua saindo (decisão 15).
/// </para>
/// <para>
/// Ela não some quando o item é contratado: fica como o registro de por que a turma escolheu aquela.
/// É o histórico da decisão, e é barato — a proposta perdedora é uma linha.
/// </para>
/// </remarks>
public class PropostaDoItem : EntidadeDaFormatura
{
    /// <summary>O item que esta proposta disputa.</summary>
    public Guid ItemDaFestaId { get; private set; }

    /// <summary>Quem está propondo ("Banda X", "Buffet Lumière").</summary>
    public string Titulo { get; private set; } = string.Empty;

    /// <summary>Quanto ela custa, em centavos. Zero: a comissão ainda não tem o preço.</summary>
    public long ValorEmCentavos { get; private set; }

    /// <summary>O que esta proposta entrega, em Markdown curto. Nulo: só o nome e o preço.</summary>
    public string? OQueInclui { get; private set; }

    /// <summary>Uma proposta nova para um item.</summary>
    /// <param name="itemDaFestaId">Item que ela disputa.</param>
    /// <param name="dados">Dados já validados.</param>
    public static PropostaDoItem Nova(Guid itemDaFestaId, DadosDaProposta dados)
    {
        var proposta = new PropostaDoItem { ItemDaFestaId = itemDaFestaId };

        proposta.Aplicar(dados);

        return proposta;
    }

    /// <summary>Corrige o que a comissão digitou.</summary>
    /// <param name="dados">Dados já validados.</param>
    public void Aplicar(DadosDaProposta dados)
    {
        Titulo = dados.Titulo.Trim();
        ValorEmCentavos = dados.ValorEmCentavos;
        OQueInclui = string.IsNullOrWhiteSpace(dados.OQueInclui) ? null : dados.OQueInclui.Trim();
    }
}
