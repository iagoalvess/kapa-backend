namespace Backend.Business.Cobrancas.Models;

/// <summary>Por qual porta um item opcional se vende (Sprint 26, decisão 1).</summary>
/// <remarks>Gravado como texto, como todo enum do produto que vai para o banco.</remarks>
public enum ModoDeVenda
{
    /// <summary>Na vitrine do formando, como pedido com parcelas (Sprint 20).</summary>
    AoFormando,

    /// <summary>Na loja pública da turma, por link, para quem quiser (Sprint 26). As portas são exclusivas (P8).</summary>
    Publica,
}
