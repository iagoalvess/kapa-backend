using Backend.Business.Abstractions;

namespace Backend.Business.Festa.Models;

/// <summary>
/// Em qual proposta um formando votou, para um item.
/// </summary>
/// <remarks>
/// Decisão 17: <b>um voto por formando por item</b>, e trocar de ideia atualiza a linha em vez de
/// criar outra. Quem garante isso é o índice único em <c>(vinculo_id, item_da_festa_id)</c> — não o
/// service: dois cliques simultâneos em propostas diferentes não podem virar dois votos.
/// <para>
/// <see cref="ItemDaFestaId"/> está aqui de propósito, embora a proposta já o conheça: é a coluna
/// que o índice único precisa, e índice não atravessa tabela. Só o service escreve os dois, sempre
/// a partir da proposta escolhida, e <see cref="Trocar"/> não deixa mudar de item.
/// </para>
/// <para>
/// Não existe "descurtir". Dislike dobraria o estado e criaria um placar negativo contra um
/// fornecedor que a comissão pode ter de contratar mesmo assim — tirar o voto é
/// <c>DELETE</c> da linha, e é o suficiente.
/// </para>
/// </remarks>
public class VotoNaProposta : EntidadeDaFormatura
{
    /// <summary>Quem votou.</summary>
    public Guid VinculoId { get; private set; }

    /// <summary>O item em disputa — a coluna do índice único, junto com o vínculo.</summary>
    public Guid ItemDaFestaId { get; private set; }

    /// <summary>A proposta escolhida.</summary>
    public Guid PropostaId { get; private set; }

    /// <summary>Um voto novo.</summary>
    /// <param name="vinculoId">Quem vota.</param>
    /// <param name="proposta">A escolhida.</param>
    public static VotoNaProposta Novo(Guid vinculoId, PropostaDoItem proposta) =>
        new()
        {
            VinculoId = vinculoId,
            ItemDaFestaId = proposta.ItemDaFestaId,
            PropostaId = proposta.Id,
        };

    /// <summary>Mudou de ideia: o mesmo voto passa a apontar para outra proposta do mesmo item.</summary>
    /// <remarks>
    /// A proposta precisa ser do item que este voto já disputa. Trocar de item aqui furaria o índice
    /// único pela porta dos fundos: a pessoa passaria a ter dois votos no item de destino.
    /// </remarks>
    /// <param name="proposta">A escolhida agora.</param>
    public Result Trocar(PropostaDoItem proposta)
    {
        if (proposta.ItemDaFestaId != ItemDaFestaId)
            return Result.Falha(Erro.Conflito("festa.proposta_de_outro_item", "Essa proposta é de outro item."));

        PropostaId = proposta.Id;

        return Result.Ok();
    }
}
