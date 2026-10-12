using Backend.Business.Abstractions;

namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// O desconto ou a isenção que a tesouraria autoriza a uma pessoa da turma (Sprint 54).
/// </summary>
/// <remarks>
/// É uma <b>autorização</b>, não uma contratação: a pessoa continua escolhendo os pacotes, e o benefício só alcança os
/// pacotes listados que ela de fato escolher — na adesão e em cada acréscimo da cesta depois (P12). O cargo não concede
/// nem retira nada (P9); quem concede é a Tesouraria, com auditoria.
/// <para>
/// Uma por pessoa. Pertence ao <see cref="VinculoId"/> de quem já entrou, ou ao <see cref="Email"/> do convite pessoal de
/// quem ainda não entrou — o aceite do convite, que confere o dono do e-mail, passa a condição para o vínculo
/// (<see cref="TransferirPara"/>). Reenviar o convite não a perde, e encaminhar o link não a entrega a outro.
/// </para>
/// <para>
/// O que vale depois do aceite é o que foi aceito: o desconto aplicado fica no snapshot da adesão (no hash) e em
/// <see cref="EscolhaDaCesta.DescontoEmCentavos"/>. Alterar ou revogar esta linha muda só o que ainda vai ser contratado.
/// </para>
/// <para>
/// Sem prazo de validade (revisão de 12/10/2026): vale até a tesouraria revogar.
/// </para>
/// </remarks>
public class CondicaoFinanceira : EntidadeDaFormatura
{
    /// <summary>Quem recebe, se já está na turma.</summary>
    public Guid? VinculoId { get; private set; }

    /// <summary>O e-mail do convite pessoal de quem ainda não entrou, em minúsculas.</summary>
    public string? Email { get; private set; }

    /// <summary>Quem concedeu ou alterou por último.</summary>
    public Guid ConcedidaPorUsuarioId { get; private set; }

    /// <summary>Os pacotes alcançados, um benefício por pacote ou grupo de faixas (P2/P3).</summary>
    public List<BeneficioDaCondicao> Beneficios { get; private set; } = [];

    /// <summary>A condição de quem já está na turma.</summary>
    /// <param name="vinculoId">Quem recebe.</param>
    /// <param name="dados">Benefícios, já validados.</param>
    /// <param name="autorId">Quem concede.</param>
    public static CondicaoFinanceira DoVinculo(Guid vinculoId, DadosDaCondicao dados, Guid autorId)
    {
        var condicao = new CondicaoFinanceira { VinculoId = vinculoId };
        condicao.Alterar(dados, autorId);

        return condicao;
    }

    /// <summary>A condição preparada no convite pessoal, para quem ainda não entrou.</summary>
    /// <param name="email">E-mail do convite.</param>
    /// <param name="dados">Benefícios, já validados.</param>
    /// <param name="autorId">Quem concede.</param>
    public static CondicaoFinanceira DoConvidado(string email, DadosDaCondicao dados, Guid autorId)
    {
        var condicao = new CondicaoFinanceira { Email = NormalizarEmail(email) };
        condicao.Alterar(dados, autorId);

        return condicao;
    }

    /// <summary>O e-mail como a condição o guarda e o procura.</summary>
    /// <param name="email">E-mail como veio.</param>
    public static string NormalizarEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Troca os benefícios. Não muda o que já foi aceito.</summary>
    /// <param name="dados">Benefícios, já validados.</param>
    /// <param name="autorId">Quem altera.</param>
    public void Alterar(DadosDaCondicao dados, Guid autorId)
    {
        Beneficios = [.. dados.Beneficios];
        ConcedidaPorUsuarioId = autorId;
    }

    /// <summary>A pessoa do convite entrou: a condição passa a ser do vínculo dela.</summary>
    /// <param name="vinculoId">Vínculo criado pelo aceite.</param>
    public void TransferirPara(Guid vinculoId)
    {
        VinculoId = vinculoId;
        Email = null;
    }

    /// <summary>
    /// Quanto a condição desconta de um pacote do catálogo, em centavos; zero se não o alcança.
    /// </summary>
    /// <remarks>
    /// O benefício aponta um pacote. Num grupo de faixas, ele vale para todo o grupo <b>até o preço</b> da faixa
    /// apontada (P3): isenção da Festa 10 (R$ 5.000) dá R$ 5.000 de desconto na Festa 15 (R$ 6.000), que fica em
    /// R$ 1.000; 20% da Festa 10 numa faixa menor é 20% da faixa menor. O desconto nunca passa do preço do pacote.
    /// </remarks>
    /// <param name="pacote">Pacote escolhido.</param>
    /// <param name="catalogo">Itens do plano, para achar a faixa apontada.</param>
    public long DescontoDe(ItemDeCobranca pacote, IReadOnlyCollection<ItemDeCobranca> catalogo)
    {
        var apontado = Beneficios
            .Select(beneficio => (Beneficio: beneficio, Item: catalogo.FirstOrDefault(item => item.Id == beneficio.ItemDeCobrancaId)))
            .FirstOrDefault(par =>
                par.Item is not null
                && (par.Item.Id == pacote.Id || (pacote.Grupo is not null && string.Equals(par.Item.Grupo, pacote.Grupo, StringComparison.Ordinal)))
            );

        if (apontado.Item is null)
            return 0;

        var desconto = apontado.Beneficio.Percentual is { } percentual
            ? Math.Min(pacote.ValorEmCentavos, apontado.Item.ValorEmCentavos) * percentual / BeneficioDaCondicao.Inteiro
            : apontado.Beneficio.ValorEmCentavos ?? 0;

        return Math.Clamp(desconto, 0, Math.Max(pacote.ValorEmCentavos, 0));
    }

    /// <summary>O desconto de cada pacote da cesta que a condição alcança.</summary>
    /// <param name="condicao">Condição da pessoa, se houver.</param>
    /// <param name="cesta">Pacotes escolhidos.</param>
    /// <param name="catalogo">Itens do plano.</param>
    public static IReadOnlyDictionary<Guid, long> Descontos(
        CondicaoFinanceira? condicao,
        IEnumerable<ItemDeCobranca> cesta,
        IReadOnlyCollection<ItemDeCobranca> catalogo
    ) =>
        condicao is null
            ? new Dictionary<Guid, long>()
            : cesta
                .Select(pacote => (pacote.Id, Desconto: condicao.DescontoDe(pacote, catalogo)))
                .Where(par => par.Desconto > 0)
                .ToDictionary(par => par.Id, par => par.Desconto);
}

/// <summary>Um benefício da condição: um pacote (ou o grupo de faixas dele) e quanto desconta.</summary>
/// <remarks>Ou percentual, ou valor fixo (P2). <c>10.000</c> é isenção.</remarks>
/// <param name="ItemDeCobrancaId">Pacote apontado; num grupo, a faixa até cujo preço o benefício vale.</param>
/// <param name="Percentual">Percentual, base 10.000.</param>
/// <param name="ValorEmCentavos">Valor fixo, em centavos.</param>
public sealed record BeneficioDaCondicao(Guid ItemDeCobrancaId, int? Percentual, long? ValorEmCentavos)
{
    /// <summary>Cem por cento, na base dos percentuais do projeto.</summary>
    public const int Inteiro = 10_000;
}

/// <summary>A condição como a tesouraria a informa.</summary>
/// <param name="Beneficios">Pacotes alcançados.</param>
public sealed record DadosDaCondicao(IReadOnlyList<BeneficioDaCondicao> Beneficios);

/// <summary>De quem é a condição: o usuário que já entrou, ou o e-mail do convite pessoal de quem ainda não.</summary>
/// <param name="UsuarioId">Usuário na turma.</param>
/// <param name="Email">E-mail do convite pessoal.</param>
public sealed record PessoaDaCondicao(Guid? UsuarioId, string? Email);

/// <summary>A condição da pessoa, como a Gestão a lê; nula quando não há.</summary>
/// <param name="Condicao">A condição, se houver.</param>
public sealed record CondicaoDaPessoa(CondicaoLida? Condicao);

/// <summary>Uma condição financeira, com o efeito de cada benefício no catálogo de hoje.</summary>
/// <param name="Beneficios">Os pacotes alcançados.</param>
/// <param name="AtualizadaEm">Última alteração, em UTC.</param>
public sealed record CondicaoLida(IReadOnlyList<BeneficioLido> Beneficios, DateTime AtualizadaEm);

/// <summary>Um benefício, com o pacote e o desconto que dá nele.</summary>
/// <param name="ItemDeCobrancaId">Pacote apontado.</param>
/// <param name="Rotulo">"Festa — 10 pessoas", ou o nome do pacote; nulo se o pacote saiu do catálogo.</param>
/// <param name="Percentual">Percentual, base 10.000.</param>
/// <param name="ValorEmCentavos">Valor fixo.</param>
/// <param name="PrecoEmCentavos">Preço do pacote no catálogo de hoje.</param>
/// <param name="DescontoEmCentavos">Quanto o benefício desconta do pacote apontado.</param>
public sealed record BeneficioLido(
    Guid ItemDeCobrancaId,
    string? Rotulo,
    int? Percentual,
    long? ValorEmCentavos,
    long PrecoEmCentavos,
    long DescontoEmCentavos
);
