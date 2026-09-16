using System.Security.Cryptography;
using System.Text;
using Backend.Business.Abstractions;

namespace Backend.Business.Adesoes.Models;

/// <summary>
/// Prova de que um formando aceitou uma versão do termo, com o plano que ela trazia.
/// </summary>
/// <remarks>
/// Hash do conteúdo + data/hora UTC + IP + User-Agent + código confirmado no e-mail + quem — e não
/// certificado ICP-Brasil, que exige e-CPF e derruba a adesão a zero. O código é o que separa o
/// aceite de uma sessão aberta em máquina emprestada do aceite de quem tem a caixa de entrada.
/// O que dá valor a esse conjunto é a imutabilidade: as
/// propriedades são <c>init</c>, e o banco recusa <c>UPDATE</c> e <c>DELETE</c> na tabela, como no
/// consentimento da Sprint 1.
/// <para>
/// Nome e CPF são os do <b>instante do aceite</b>, copiados do cadastro: corrigir o cadastro depois
/// não muda o termo assinado. O CPF é cifrado no mapeamento, como no perfil; ao lado dele, uma coluna
/// com o HMAC do CPF (mantida pelo repositório) é o que permite recusar o mesmo CPF duas vezes na turma.
/// </para>
/// </remarks>
public class AdesaoDoFormando : EntidadeDaFormatura
{
    /// <summary>Vínculo de quem aceitou.</summary>
    public Guid VinculoId { get; init; }

    /// <summary>A versão exata do termo aceita.</summary>
    public Guid TermoId { get; init; }

    /// <summary>Número da versão, copiado do termo para o registro se ler sozinho.</summary>
    public int Versao { get; init; }

    /// <summary>SHA-256 do que estava na tela, em hexadecimal minúsculo. Ver <see cref="CalcularHash"/>.</summary>
    public string HashDoConteudo { get; init; } = string.Empty;

    /// <summary>Momento do aceite, em UTC.</summary>
    public DateTime AceitoEm { get; init; }

    /// <summary>IP de onde veio o aceite.</summary>
    public string EnderecoIp { get; init; } = string.Empty;

    /// <summary>Navegador que enviou o aceite.</summary>
    public string UserAgent { get; init; } = string.Empty;

    /// <summary>
    /// E-mail para onde foi o código de confirmação conferido neste aceite.
    /// </summary>
    /// <remarks>
    /// É a diferença entre "alguém logado clicou" e "quem controla esta caixa de entrada confirmou".
    /// Gravado no instante do aceite porque o e-mail da conta muda depois; o que vale é o do dia.
    /// Vazio nas adesões anteriores ao código, e aí o PDF omite a linha.
    /// </remarks>
    public string EmailDoAceite { get; init; } = string.Empty;

    /// <summary>Nome civil no instante do aceite.</summary>
    public string NomeCompleto { get; init; } = string.Empty;

    /// <summary>CPF no instante do aceite, só os dígitos. Cifrado na coluna.</summary>
    public string Cpf { get; init; } = string.Empty;

    /// <summary>JSON do <see cref="SnapshotDoPlano"/> aceito, exatamente como entrou no hash.</summary>
    public string PlanoAceito { get; init; } = string.Empty;

    /// <summary>O plano aceito, lido do JSON gravado.</summary>
    public SnapshotDoPlano LerPlano() => SnapshotDoPlano.Ler(PlanoAceito);

    /// <summary>
    /// O hash do que o formando aceita: o texto do termo e o plano financeiro, juntos.
    /// </summary>
    /// <remarks>
    /// O plano entra no hash porque o resumo financeiro faz parte do que foi aceito — "não vi o valor"
    /// não se sustenta. Efeito colateral útil: se a tesouraria muda o plano enquanto o formando lê, o
    /// hash que ele enviou deixa de bater, e o aceite é recusado em vez de registrar um valor que ele
    /// não viu. Conferir depois é refazer a conta sobre o termo e o <see cref="PlanoAceito"/> gravados.
    /// </remarks>
    /// <param name="conteudoDoTermo">Markdown da versão.</param>
    /// <param name="planoJson">JSON do snapshot, como sai de <see cref="SnapshotDoPlano.ParaJson"/>.</param>
    public static string CalcularHash(string conteudoDoTermo, string planoJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{conteudoDoTermo}\n{planoJson}")));
}
