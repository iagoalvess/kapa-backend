using System.Text.RegularExpressions;
using Backend.Business.Common.Texto;

namespace Backend.Business.Recebimentos.Models;

/// <summary>Os cinco tipos de chave do diretório do PIX.</summary>
/// <remarks>Gravado como texto: renomear um valor aqui é migration, não refatoração.</remarks>
public enum TipoDeChavePix
{
    /// <summary>CPF do titular, só os 11 dígitos.</summary>
    Cpf,

    /// <summary>CNPJ do titular, 14 posições — numérico ou alfanumérico.</summary>
    Cnpj,

    /// <summary>E-mail, em minúsculas.</summary>
    Email,

    /// <summary>Celular em E.164, <c>+55</c> com DDD.</summary>
    Telefone,

    /// <summary>Chave aleatória: um UUID gerado pelo banco.</summary>
    Aleatoria,
}

/// <summary>
/// A forma de cada tipo de chave: o que é válido e como é gravado.
/// </summary>
/// <remarks>
/// Uma função para as duas pontas, como em <see cref="FormatosBrasileiros"/>: o validator pergunta
/// se <see cref="Normalizar"/> devolve algo, e a entidade grava o que ela devolve. A chave é gravada
/// no formato do diretório do PIX porque é ela, sem máscara, que entra no BR Code.
/// <para>
/// Formato é tudo o que dá para conferir aqui. Se a chave existe e de quem é, só o diretório do PIX
/// sabe — e ele só responde a instituição participante. Por isso o PIX de teste.
/// </para>
/// </remarks>
public static partial class ChavePix
{
    /// <summary>Teto do diretório do PIX para chave de e-mail — é o que cabe no campo 26 do BR Code.</summary>
    public const int TamanhoMaximoDoEmail = 77;

    /// <summary>
    /// A chave no formato do diretório do PIX, ou nulo se não for válida para o tipo.
    /// </summary>
    /// <remarks>Telefone só celular: é o que os bancos aceitam como chave.</remarks>
    /// <param name="tipo">Tipo da chave.</param>
    /// <param name="chave">Chave como a pessoa digitou.</param>
    public static string? Normalizar(TipoDeChavePix tipo, string? chave)
    {
        var texto = chave?.Trim() ?? string.Empty;

        return tipo switch
        {
            TipoDeChavePix.Cpf => FormatosBrasileiros.CpfValido(texto) ? FormatosBrasileiros.SomenteDigitos(texto) : null,
            TipoDeChavePix.Cnpj => FormatosBrasileiros.CnpjValido(texto) ? FormatosBrasileiros.NormalizarCnpj(texto) : null,
            TipoDeChavePix.Email => texto.Length <= TamanhoMaximoDoEmail && Email().IsMatch(texto) ? texto.ToLowerInvariant() : null,
            TipoDeChavePix.Telefone => Celular(texto),
            TipoDeChavePix.Aleatoria => Guid.TryParseExact(texto, "D", out var aleatoria) ? aleatoria.ToString("D") : null,
            _ => null,
        };
    }

    /// <summary>Nome do tipo para gente ler, em e-mail e em tela: "CPF", "Celular".</summary>
    /// <param name="tipo">Tipo da chave.</param>
    public static string Rotulo(TipoDeChavePix tipo) =>
        tipo switch
        {
            TipoDeChavePix.Cpf => "CPF",
            TipoDeChavePix.Cnpj => "CNPJ",
            TipoDeChavePix.Email => "E-mail",
            TipoDeChavePix.Telefone => "Celular",
            _ => "Chave aleatória",
        };

    /// <summary>
    /// O documento do titular, quando a chave é um: CPF mascarado, CNPJ inteiro. Nulo nas demais.
    /// </summary>
    /// <remarks>
    /// É a linha "CPF ***.982.247-**" ao lado do nome, na tela de pagamento e no recibo (Sprint 22).
    /// O CPF sai no formato de documento público; o CNPJ é público por natureza.
    /// </remarks>
    /// <param name="tipo">Tipo da chave.</param>
    /// <param name="chave">Chave normalizada.</param>
    public static string? DocumentoDoTitular(TipoDeChavePix tipo, string chave) =>
        tipo switch
        {
            TipoDeChavePix.Cpf => $"CPF {FormatosBrasileiros.MascararCpf(chave)}",
            TipoDeChavePix.Cnpj => $"CNPJ {chave}",
            _ => null,
        };

    /// <summary>O celular brasileiro em E.164 (<c>+55</c>, DDD, 9 e oito dígitos), ou nulo.</summary>
    /// <param name="texto">Telefone informado.</param>
    private static string? Celular(string texto) =>
        FormatosBrasileiros.TelefoneE164(texto) is { Length: 14 } e164 && e164.StartsWith("+55", StringComparison.Ordinal) && e164[5] == '9'
            ? e164
            : null;

    [GeneratedRegex(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}$")]
    private static partial Regex Email();
}
