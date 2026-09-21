using Backend.Business.Abstractions;

namespace Backend.Business.Recebimentos.Models;

/// <summary>
/// Como esta turma recebe: os meios que ela aceita e o que cada um precisa mostrar. Uma por formatura.
/// </summary>
/// <remarks>
/// O Kapa não recebe nem repassa. Do PIX ele monta o texto do BR Code, e o formando paga direto na
/// conta da comissão; os outros meios são <b>instrução</b> — dados bancários, um nome, uma linha de
/// texto —, e nada aqui move dinheiro nem emite documento.
/// <para>
/// Cada meio é um grupo de colunas anuláveis, e o meio existe quando o grupo está preenchido
/// (<see cref="MeiosDaConta" />). Ao menos um grupo é obrigatório — quem garante é o validator, e a
/// turma sem nenhum não teria o que mostrar na tela de pagamento.
/// </para>
/// <para>
/// A conferência é do PIX e só dele: é a chave que o PIX de teste confirma. Trocar os dados do TED ou
/// o nome de quem recebe em dinheiro não desfaz uma conferência que não falava deles.
/// </para>
/// </remarks>
public class ContaDeRecebimento : EntidadeDaFormatura
{
    /// <summary>Tipo da chave. Nulo: a turma não aceita PIX.</summary>
    public TipoDeChavePix? TipoDeChave { get; private set; }

    /// <summary>A chave, no formato do diretório do PIX (<see cref="ChavePix.Normalizar"/>).</summary>
    public string? Chave { get; private set; }

    /// <summary>Nome do titular da chave, como a comissão digitou.</summary>
    public string? NomeDoTitular { get; private set; }

    /// <summary>Cidade do titular, como a comissão digitou.</summary>
    public string? Cidade { get; private set; }

    /// <summary>Banco da conta para transferência. Nulo: a turma não aceita TED.</summary>
    public string? Banco { get; private set; }

    /// <summary>Agência.</summary>
    public string? Agencia { get; private set; }

    /// <summary>Conta, com o dígito.</summary>
    public string? Conta { get; private set; }

    /// <summary>"Corrente" ou "Poupança", como a comissão digitou.</summary>
    public string? TipoDeConta { get; private set; }

    /// <summary>Titular da conta bancária — nem sempre é o mesmo da chave PIX.</summary>
    public string? TitularDaConta { get; private set; }

    /// <summary>Quem recebe em espécie. Nulo: a turma não aceita dinheiro.</summary>
    public string? DinheiroCom { get; private set; }

    /// <summary>Onde encontrar essa pessoa.</summary>
    public string? DinheiroOnde { get; private set; }

    /// <summary>Quando o Presidente confirmou o titular pelo PIX de teste, em UTC. Nulo: não conferida.</summary>
    public DateTime? ConferidaEm { get; private set; }

    /// <summary>Quem confirmou.</summary>
    public Guid? ConferidaPorUsuarioId { get; private set; }

    /// <summary>Se o Presidente já confirmou o titular pelo PIX de teste.</summary>
    public bool Conferida => ConferidaEm is not null;

    /// <summary>
    /// Grava os meios. Mudar o PIX desfaz a conferência; mudar os outros, não.
    /// </summary>
    /// <remarks>
    /// O PIX de teste confirma a combinação inteira — "esta chave é deste titular". Mudou qualquer
    /// parte dela, a confirmação antiga não vale para a nova.
    /// </remarks>
    /// <param name="meios">Meios já validados.</param>
    /// <returns>Se algo mudou.</returns>
    /// <exception cref="ArgumentException">Se a chave não for válida para o tipo — o validator deveria ter barrado.</exception>
    public bool Aplicar(MeiosDaConta meios)
    {
        var antes = ParaMeios();
        var depois = Normalizar(meios);

        if (antes == depois)
            return false;

        TipoDeChave = depois.Pix?.TipoDeChave;
        Chave = depois.Pix?.Chave;
        NomeDoTitular = depois.Pix?.NomeDoTitular;
        Cidade = depois.Pix?.Cidade;

        Banco = depois.Transferencia?.Banco;
        Agencia = depois.Transferencia?.Agencia;
        Conta = depois.Transferencia?.Conta;
        TipoDeConta = depois.Transferencia?.TipoDeConta;
        TitularDaConta = depois.Transferencia?.Titular;

        DinheiroCom = depois.Dinheiro?.Nome;
        DinheiroOnde = depois.Dinheiro?.Onde;

        if (antes.Pix != depois.Pix)
        {
            ConferidaEm = null;
            ConferidaPorUsuarioId = null;
        }

        return true;
    }

    /// <summary>O Presidente pagou o PIX de teste e o banco mostrou este titular.</summary>
    /// <param name="usuarioId">Quem confirma.</param>
    /// <param name="agoraUtc">Momento da confirmação.</param>
    public Result Conferir(Guid usuarioId, DateTime agoraUtc)
    {
        if (Chave is null)
            return Result.Falha(Erro.Conflito("recebimento.sem_chave_pix", "Esta turma não aceita PIX."));

        if (Conferida)
            return Result.Falha(Erro.Conflito("recebimento.conta_ja_conferida", "Esta chave já foi conferida."));

        ConferidaEm = agoraUtc;
        ConferidaPorUsuarioId = usuarioId;

        return Result.Ok();
    }

    /// <summary>Os meios como dados — o antes e o depois da troca na auditoria.</summary>
    public MeiosDaConta ParaMeios() =>
        new(
            TipoDeChave is { } tipo && Chave is not null
                ? new ChavePixDaConta(tipo, Chave, NomeDoTitular ?? string.Empty, Cidade ?? string.Empty)
                : null,
            Banco is null
                ? null
                : new DadosBancarios(
                    Banco,
                    Agencia ?? string.Empty,
                    Conta ?? string.Empty,
                    TipoDeConta ?? string.Empty,
                    TitularDaConta ?? string.Empty
                ),
            DinheiroCom is null ? null : new DinheiroComAlguem(DinheiroCom, DinheiroOnde)
        );

    /// <summary>Apara o texto de cada campo e normaliza a chave; grupo que sobrou vazio vira meio desligado.</summary>
    private static MeiosDaConta Normalizar(MeiosDaConta meios)
    {
        ChavePixDaConta? pix = null;

        if (meios.Pix is { } chavePix)
        {
            var chave =
                ChavePix.Normalizar(chavePix.TipoDeChave, chavePix.Chave)
                ?? throw new ArgumentException("Chave inválida para o tipo.", nameof(meios));

            pix = new ChavePixDaConta(chavePix.TipoDeChave, chave, chavePix.NomeDoTitular.Trim(), chavePix.Cidade.Trim());
        }

        var transferencia = meios.Transferencia is { } dados
            ? new DadosBancarios(dados.Banco.Trim(), dados.Agencia.Trim(), dados.Conta.Trim(), dados.TipoDeConta.Trim(), dados.Titular.Trim())
            : null;

        var dinheiro = meios.Dinheiro is { } com ? new DinheiroComAlguem(com.Nome.Trim(), Texto(com.Onde)) : null;

        return new MeiosDaConta(pix, transferencia, dinheiro);
    }

    /// <summary>Texto aparado, ou nulo quando só havia espaço.</summary>
    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
