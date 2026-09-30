using Backend.Business.Abstractions;
using Backend.Business.Financeiro.Models;

namespace Backend.Business.Festa.Models;

/// <summary>
/// O que a turma está comprando: o buffet, o espaço, a fotografia — o combinado, não o vencimento.
/// </summary>
/// <remarks>
/// Decisão 1: o item existe <b>antes</b> de haver o que pagar ("Buffet — a contratar"), e uma
/// <see cref="Despesa"/> não descreve nada ("Buffet 2/5" numa lista de vencimentos). Um item tem N
/// lançamentos de despesa; uma despesa aponta para nenhum ou um item.
/// <para>
/// Não existe coluna de situação (decisão 2): "a contratar", "contratado" e "pago" são lidos das
/// despesas vinculadas a cada consulta, pelo mesmo motivo que o caixa não tem coluna de saldo. O
/// único estado gravado é o cancelamento, que é um fato — a turma desistiu — e não a consequência
/// de outra tabela.
/// </para>
/// </remarks>
public class ItemDaFesta : EntidadeDaFormatura
{
    /// <summary>Como a turma chama o item ("Buffet", "Sessão de fotos").</summary>
    public string Titulo { get; private set; } = string.Empty;

    /// <summary>Em que gaveta ele entra — a mesma lista das despesas, para os dois lados somarem igual.</summary>
    public CategoriaDeDespesa Categoria { get; private set; }

    /// <summary>O que vai ter, em Markdown curto (decisão 4). Nulo: a comissão ainda não descreveu.</summary>
    public string? OQueInclui { get; private set; }

    /// <summary>
    /// O contrato deste item no acervo da Sprint 11. Nulo: a comissão não ligou nenhum documento.
    /// </summary>
    /// <remarks>
    /// Decisão 7: o contrato não é reenviado aqui — ele já mora no acervo, com a política de acesso
    /// dele. Só documento de visibilidade <c>Turma</c> pode ser ligado, porque o cartão é lido pela
    /// turma inteira: apontar para uma ata da comissão vazaria o título dela.
    /// </remarks>
    public Guid? DocumentoId { get; private set; }

    /// <summary>Quem paga: a turma inteira, ou só quem quiser (decisão 14).</summary>
    public TipoDeRateio Rateio { get; private set; } = TipoDeRateio.Turma;

    /// <summary>
    /// O valor que a comissão digitou, em centavos: o total do contrato, ou o preço de cada
    /// formando quando o rateio é <see cref="TipoDeRateio.PorFormando"/>. Zero: sem orçamento ainda.
    /// </summary>
    /// <remarks>Vale até existir despesa; daí em diante o custo é a soma delas (decisão 3).</remarks>
    public long ValorPrevistoEmCentavos { get; private set; }

    /// <summary>Quantos formandos a comissão espera que comprem. Sempre 1 no item rateado pela turma.</summary>
    public int QuantidadeEstimada { get; private set; } = 1;

    /// <summary>Posição na tela, crescente. A comissão decide o que vem primeiro.</summary>
    public int Ordem { get; private set; }

    /// <summary>Quando a turma desistiu, em UTC. Nulo: o item está de pé.</summary>
    public DateTime? CanceladoEm { get; private set; }

    /// <summary>Se saiu do custo da festa — continua na lista, com o selo (decisão 13).</summary>
    public bool Cancelado => CanceladoEm is not null;

    /// <summary>Um item novo, no fim da lista.</summary>
    /// <param name="dados">Dados já validados.</param>
    /// <param name="ordem">Posição na tela.</param>
    public static ItemDaFesta Novo(DadosDoItemDaFesta dados, int ordem)
    {
        var item = new ItemDaFesta { Ordem = ordem };

        item.Aplicar(dados);

        return item;
    }

    /// <summary>Um dos seis itens com que a turma começa: só o nome e a gaveta (decisão 12).</summary>
    /// <param name="titulo">Nome do item.</param>
    /// <param name="categoria">Gaveta.</param>
    /// <param name="ordem">Posição na tela.</param>
    public static ItemDaFesta Sugerido(string titulo, CategoriaDeDespesa categoria, int ordem) =>
        new()
        {
            Titulo = titulo,
            Categoria = categoria,
            Ordem = ordem,
        };

    /// <summary>Grava o que a comissão digitou.</summary>
    /// <remarks>
    /// Item cancelado não se edita: a turma desistiu dele, e corrigir o preço de algo que não vai
    /// acontecer é o caminho para um número que ninguém entende. Reabra antes.
    /// </remarks>
    /// <param name="dados">Dados já validados.</param>
    public Result Aplicar(DadosDoItemDaFesta dados)
    {
        if (Cancelado)
            return Result.Falha(Erro.Conflito("festa.item_cancelado", "Este item foi cancelado. Reative-o antes de alterá-lo."));

        Titulo = dados.Titulo.Trim();
        Categoria = dados.Categoria;
        OQueInclui = Vazio(dados.OQueInclui?.Trim());
        DocumentoId = dados.DocumentoId;
        Rateio = dados.Rateio;
        ValorPrevistoEmCentavos = dados.ValorPrevistoEmCentavos;
        QuantidadeEstimada = dados.Rateio == TipoDeRateio.Turma ? 1 : dados.QuantidadeEstimada;

        return Result.Ok();
    }

    /// <summary>
    /// A turma desistiu: sai do custo da festa e da barra, continua na lista com o selo.
    /// </summary>
    /// <remarks>
    /// Não mexe nas despesas dele (decisão 13): o sinal da banda saiu do caixa e continua no
    /// balancete. Quem cancela despesa é a tela de Despesas, uma a uma.
    /// </remarks>
    public Result Cancelar()
    {
        if (Cancelado)
            return Result.Falha(Erro.Conflito("festa.item_cancelado", "Este item já está cancelado."));

        CanceladoEm = DateTime.UtcNow;

        return Result.Ok();
    }

    /// <summary>Desfaz o cancelamento — o item volta a contar no custo da festa.</summary>
    /// <remarks>
    /// Existe porque cancelar é um clique e a alternativa seria recadastrar o item e repontar todas
    /// as despesas dele na mão, o que é pior do que o engano.
    /// </remarks>
    public Result Reativar()
    {
        if (!Cancelado)
            return Result.Falha(Erro.Conflito("festa.item_nao_cancelado", "Este item não está cancelado."));

        CanceladoEm = null;

        return Result.Ok();
    }

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto;
}
