using Backend.Business.Abstractions;

namespace Backend.Business.Recebimentos.Models;

/// <summary>
/// Para onde vai o dinheiro da turma: a chave PIX da comissão. Uma por formatura.
/// </summary>
/// <remarks>
/// O Kapa não recebe nem repassa: a partir desta chave ele monta o texto do PIX, e o formando paga
/// direto na conta da comissão. O QR não é gravado em lugar nenhum — cada tela monta o seu da chave
/// vigente, então trocar a chave não deixa QR antigo circulando.
/// <para>
/// Nome e cidade ficam como a pessoa digitou; o <c>BrCode</c> tira o acento e corta no limite do
/// padrão só na hora de montar. "Conferida" é <see cref="ConferidaEm"/> preenchido — sem enum para
/// dois estados.
/// </para>
/// </remarks>
public class ContaDeRecebimento : EntidadeDaFormatura
{
    /// <summary>Tipo da chave.</summary>
    public TipoDeChavePix TipoDeChave { get; private set; }

    /// <summary>A chave, no formato do diretório do PIX (<see cref="ChavePix.Normalizar"/>).</summary>
    public string Chave { get; private set; } = string.Empty;

    /// <summary>Nome do titular da conta, como a comissão digitou.</summary>
    public string NomeDoTitular { get; private set; } = string.Empty;

    /// <summary>Cidade do titular, como a comissão digitou.</summary>
    public string Cidade { get; private set; } = string.Empty;

    /// <summary>Quando o Presidente confirmou o titular pelo PIX de teste, em UTC. Nulo: não conferida.</summary>
    public DateTime? ConferidaEm { get; private set; }

    /// <summary>Quem confirmou.</summary>
    public Guid? ConferidaPorUsuarioId { get; private set; }

    /// <summary>Se o Presidente já confirmou o titular pelo PIX de teste.</summary>
    public bool Conferida => ConferidaEm is not null;

    /// <summary>
    /// Grava a chave, o titular e a cidade. Qualquer mudança desfaz a conferência.
    /// </summary>
    /// <remarks>
    /// O PIX de teste confirma a combinação inteira — "esta chave é deste titular". Mudou qualquer
    /// parte, a confirmação antiga não vale para a nova.
    /// </remarks>
    /// <param name="dados">Dados já validados.</param>
    /// <returns>Se algo mudou.</returns>
    /// <exception cref="ArgumentException">Se a chave não for válida para o tipo — o validator deveria ter barrado.</exception>
    public bool Aplicar(DadosDaConta dados)
    {
        var chave = ChavePix.Normalizar(dados.TipoDeChave, dados.Chave) ?? throw new ArgumentException("Chave inválida para o tipo.", nameof(dados));
        var nome = dados.NomeDoTitular.Trim();
        var cidade = dados.Cidade.Trim();

        if (TipoDeChave == dados.TipoDeChave && Chave == chave && NomeDoTitular == nome && Cidade == cidade)
            return false;

        TipoDeChave = dados.TipoDeChave;
        Chave = chave;
        NomeDoTitular = nome;
        Cidade = cidade;
        ConferidaEm = null;
        ConferidaPorUsuarioId = null;

        return true;
    }

    /// <summary>O Presidente pagou o PIX de teste e o banco mostrou este titular.</summary>
    /// <param name="usuarioId">Quem confirma.</param>
    /// <param name="agoraUtc">Momento da confirmação.</param>
    public Result Conferir(Guid usuarioId, DateTime agoraUtc)
    {
        if (Conferida)
            return Result.Falha(Erro.Conflito("recebimento.conta_ja_conferida", "Esta chave já foi conferida."));

        ConferidaEm = agoraUtc;
        ConferidaPorUsuarioId = usuarioId;

        return Result.Ok();
    }

    /// <summary>Chave, titular e cidade como dados — o antes e o depois da troca na auditoria.</summary>
    public DadosDaConta ParaDados() => new(TipoDeChave, Chave, NomeDoTitular, Cidade);
}
