using System.Text.RegularExpressions;

namespace Backend.Business.Common.Texto;

/// <summary>
/// Conferência e normalização de CPF, telefone, CEP e UF.
/// </summary>
/// <remarks>
/// Um lugar só para as duas pontas da mesma regra: o validator pergunta "é válido?" e a entidade
/// grava a forma normalizada. Se cada um tivesse a sua versão, um dia o validator aceitaria um
/// formato que a normalização não entende.
/// </remarks>
public static partial class FormatosBrasileiros
{
    private static readonly HashSet<string> Ufs =
    [
        "AC",
        "AL",
        "AP",
        "AM",
        "BA",
        "CE",
        "DF",
        "ES",
        "GO",
        "MA",
        "MT",
        "MS",
        "MG",
        "PA",
        "PB",
        "PR",
        "PE",
        "PI",
        "RJ",
        "RN",
        "RS",
        "RO",
        "RR",
        "SC",
        "SP",
        "SE",
        "TO",
    ];

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

    /// <summary>Se o CEP tem 8 dígitos, com ou sem a máscara <c>00.000-000</c>.</summary>
    /// <param name="cep">CEP informado.</param>
    public static bool CepValido(string? cep) => cep is not null && Cep().IsMatch(cep.Trim());

    /// <summary>Se é uma das 27 siglas de unidade federativa, sem diferenciar maiúsculas.</summary>
    /// <param name="uf">Sigla informada.</param>
    public static bool UfValida(string? uf) => uf is not null && Ufs.Contains(uf.Trim().ToUpperInvariant());

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

    [GeneratedRegex(@"^\d{2}\.?\d{3}-?\d{3}$")]
    private static partial Regex Cep();
}
