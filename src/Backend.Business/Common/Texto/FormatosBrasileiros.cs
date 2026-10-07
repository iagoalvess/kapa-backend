using System.Globalization;
using System.Text.RegularExpressions;

namespace Backend.Business.Common.Texto;

/// <summary>
/// Conferência e normalização de CPF, CNPJ, telefone, CEP e UF, e a escrita de dinheiro e percentual.
/// </summary>
/// <remarks>
/// Um lugar só para as duas pontas da mesma regra: o validator pergunta "é válido?" e a entidade
/// grava a forma normalizada. Se cada um tivesse a sua versão, um dia o validator aceitaria um
/// formato que a normalização não entende.
/// </remarks>
public static partial class FormatosBrasileiros
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Valor em centavos como <c>R$ 1.234,56</c>, para texto de e-mail e de documento.</summary>
    /// <remarks>Montado à mão, e não pelo formato de moeda da cultura: ICU e Windows discordam no espaço depois do R$.</remarks>
    /// <param name="centavos">Valor em centavos.</param>
    public static string Reais(long centavos) => $"R$ {(centavos / 100m).ToString("N2", PtBr)}";

    /// <summary>Percentual em base 10.000 como <c>2,5%</c> — <c>250</c> é 2,5%.</summary>
    /// <param name="baseDezMil">Percentual em base 10.000.</param>
    public static string Percentual(int baseDezMil) => $"{(baseDezMil / 100m).ToString("0.##", PtBr)}%";

    /// <summary>CPF como <c>529.982.247-25</c>. O que não tiver 11 dígitos sai como veio.</summary>
    /// <param name="cpf">CPF, com ou sem máscara.</param>
    public static string FormatarCpf(string cpf)
    {
        var digitos = SomenteDigitos(cpf);

        return digitos.Length == 11 ? $"{digitos[..3]}.{digitos[3..6]}.{digitos[6..9]}-{digitos[9..]}" : cpf;
    }

    /// <summary>Só os dígitos do texto — descarta ponto, traço, barra, parêntese e espaço.</summary>
    /// <param name="texto">Texto informado.</param>
    public static string SomenteDigitos(string? texto) => string.IsNullOrEmpty(texto) ? string.Empty : new([.. texto.Where(char.IsAsciiDigit)]);

    /// <summary>
    /// Se o CPF tem 11 dígitos e os dois verificadores conferem.
    /// </summary>
    /// <remarks>
    /// Aceita com ou sem máscara. Recusa os onze dígitos iguais, que passam na conta do verificador
    /// e não são CPF de ninguém.
    /// </remarks>
    /// <param name="cpf">CPF informado.</param>
    public static bool CpfValido(string? cpf)
    {
        var digitos = SomenteDigitos(cpf);

        if (digitos.Length != 11 || digitos.Distinct().Count() == 1)
            return false;

        return digitos[9] - '0' == DigitoVerificador(digitos, 9) && digitos[10] - '0' == DigitoVerificador(digitos, 10);
    }

    /// <summary>CNPJ só com letras maiúsculas e dígitos — sem ponto, barra, traço nem espaço.</summary>
    /// <param name="cnpj">CNPJ, com ou sem máscara.</param>
    public static string NormalizarCnpj(string? cnpj) =>
        string.IsNullOrEmpty(cnpj) ? string.Empty : new([.. cnpj.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

    /// <summary>
    /// Se o CNPJ tem 14 posições e os dois verificadores conferem.
    /// </summary>
    /// <remarks>
    /// Aceita o alfanumérico da Receita (julho de 2026): as 12 primeiras posições podem ser letras,
    /// que valem o código ASCII menos 48 na conta; os verificadores continuam dígitos. Para o CNPJ só
    /// de números a conta é a de sempre.
    /// </remarks>
    /// <param name="cnpj">CNPJ informado, com ou sem máscara.</param>
    public static bool CnpjValido(string? cnpj)
    {
        var texto = NormalizarCnpj(cnpj);

        if (!Cnpj().IsMatch(texto) || texto.Distinct().Count() == 1)
            return false;

        return texto[12] - '0' == DigitoDoCnpj(texto, 12) && texto[13] - '0' == DigitoDoCnpj(texto, 13);
    }

    /// <summary>CPF com só os seis dígitos do meio à mostra — <c>***.982.247-**</c>. Nulo continua nulo.</summary>
    /// <remarks>
    /// É o formato que o governo usa em documento público: confirma a pessoa para quem já tem o
    /// CPF dela e não entrega o número a quem não tem.
    /// </remarks>
    /// <param name="cpf">CPF gravado.</param>
    public static string? MascararCpf(string? cpf)
    {
        if (string.IsNullOrEmpty(cpf))
            return null;

        var digitos = SomenteDigitos(cpf);

        return digitos.Length == 11 ? $"***.{digitos[3..6]}.{digitos[6..9]}-**" : "***";
    }

    /// <summary>
    /// Telefone em E.164 (<c>+5541998765432</c>), ou nulo se o formato não for reconhecido.
    /// </summary>
    /// <remarks>
    /// Aceita E.164 (<c>+</c> seguido de 8 a 15 dígitos) ou o formato nacional com DDD — 10
    /// dígitos para fixo, 11 para celular, que começa com 9 depois do DDD. Nacional ganha o
    /// <c>+55</c>: gravar um formato só é o que deixa a busca e o envio de mensagem funcionarem
    /// sem adivinhar.
    /// </remarks>
    /// <param name="telefone">Telefone informado.</param>
    public static string? TelefoneE164(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone))
            return null;

        var digitos = SomenteDigitos(telefone);

        if (telefone.TrimStart().StartsWith('+'))
            return digitos.Length is >= 8 and <= 15 && digitos[0] != '0' ? $"+{digitos}" : null;

        var dddValido = digitos.Length >= 2 && digitos[0] != '0' && digitos[1] != '0';
        var nacional = digitos.Length == 10 || (digitos.Length == 11 && digitos[2] == '9');

        return dddValido && nacional ? $"+55{digitos}" : null;
    }

    /// <summary>Soma ponderada do CPF, módulo 11.</summary>
    /// <param name="digitos">CPF com 11 dígitos.</param>
    /// <param name="quantos">Quantos dígitos entram na soma: 9 para o primeiro verificador, 10 para o segundo.</param>
    private static int DigitoVerificador(string digitos, int quantos)
    {
        var soma = 0;

        for (var i = 0; i < quantos; i++)
            soma += (digitos[i] - '0') * (quantos + 1 - i);

        var resto = soma % 11;

        return resto < 2 ? 0 : 11 - resto;
    }

    /// <summary>Soma ponderada do CNPJ, módulo 11. Os pesos vão de 2 a 9, da direita para a esquerda, e recomeçam.</summary>
    /// <param name="cnpj">CNPJ normalizado, com 14 posições.</param>
    /// <param name="quantos">Quantas posições entram na soma: 12 para o primeiro verificador, 13 para o segundo.</param>
    private static int DigitoDoCnpj(string cnpj, int quantos)
    {
        var soma = 0;

        for (var i = 0; i < quantos; i++)
            soma += (cnpj[i] - '0') * (2 + ((quantos - 1 - i) % 8));

        var resto = soma % 11;

        return resto < 2 ? 0 : 11 - resto;
    }

    [GeneratedRegex("^[0-9A-Z]{12}[0-9]{2}$")]
    private static partial Regex Cnpj();
}
