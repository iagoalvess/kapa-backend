namespace Backend.Business.Formaturas.Models;

/// <summary>
/// O caminho da comissão até a turma estar rodando — o bloco "Primeiros passos" do Início.
/// </summary>
/// <remarks>
/// Derivado do que já existe, sem campo de "viu o guia": cada passo é uma pergunta de sim ou não ao banco. A comissão
/// montada é opcional e fica fora de <see cref="Concluidos"/> — convidar alguém antes de preparar o resto não termina
/// a configuração.
/// </remarks>
/// <param name="ComissaoMontada">Há mais de uma pessoa ativa na gestão (Presidente, Tesouraria ou Comissão).</param>
/// <param name="PlanoDeCobrancaEmVigor">Há plano de cobrança vigente.</param>
/// <param name="TermoPublicado">O termo de adesão tem ao menos uma versão publicada.</param>
/// <param name="RecebimentosConfigurados">
/// A turma cobra pelo Mercado Pago, ou aceita transferência, dinheiro ou um PIX de titular conferido.
/// </param>
/// <param name="PlanoContratado">A turma já contratou um plano alguma vez — o mesmo <c>ja_contratou</c> da formatura.</param>
/// <param name="FormandosNaTurma">Há ao menos um formando ativo.</param>
public sealed record PrimeirosPassos(
    bool ComissaoMontada,
    bool PlanoDeCobrancaEmVigor,
    bool TermoPublicado,
    bool RecebimentosConfigurados,
    bool PlanoContratado,
    bool FormandosNaTurma
)
{
    /// <summary>Se todos os passos obrigatórios estão feitos — e o bloco some do Início.</summary>
    public bool Concluidos => PlanoDeCobrancaEmVigor && TermoPublicado && RecebimentosConfigurados && PlanoContratado && FormandosNaTurma;
}
