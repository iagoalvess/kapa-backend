using Backend.Business.Abstractions;

namespace Backend.Business.Pagamentos.Models;

/// <summary>
/// As falhas que o lado do formando e o da tesouraria devolvem igual.
/// </summary>
public static class ErrosDePagamento
{
    /// <summary>A parcela não existe nesta turma — ou não é de quem pede, que responde a mesma coisa.</summary>
    public static readonly Erro ParcelaNaoEncontrada = Erro.NaoEncontrado("pagamento.parcela_nao_encontrada", "Parcela não encontrada.");

    /// <summary>A parcela não está em aberto: cancelada, ou já sem nada a cobrar.</summary>
    public static readonly Erro ParcelaNaoAberta = Erro.Conflito("pagamento.parcela_nao_aberta", "Esta parcela não está em aberto.");

    /// <summary>O recebimento não existe nesta turma — ou não é de quem pede, que responde a mesma coisa.</summary>
    public static readonly Erro RecebimentoNaoEncontrado = Erro.NaoEncontrado("pagamento.recebimento_nao_encontrado", "Recebimento não encontrado.");

    /// <summary>A baixa foi desfeita: não há pagamento de que dar quitação (Sprint 22, decisão 2).</summary>
    public static readonly Erro RecebimentoEstornado = Erro.Conflito(
        "pagamento.recebimento_estornado",
        "Esta baixa foi estornada, e não há recibo de pagamento desfeito."
    );
}
