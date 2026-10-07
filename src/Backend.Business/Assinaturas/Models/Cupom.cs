using System.Text.RegularExpressions;
using Backend.Business.Abstractions;

namespace Backend.Business.Assinaturas.Models;

/// <summary>
/// Cupom de desconto na primeira cobrança do plano (Sprint 51).
/// </summary>
/// <remarks>
/// Da plataforma, como o <see cref="Plano"/>: o Administrador cria, qualquer turma que nunca pagou usa. Vale só
/// na <b>primeira</b> cobrança, no mensal ou no anual (D4) — da segunda em diante, preço cheio.
/// <para>
/// Não se edita: muda o percentual de um cupom que já foi usado e o histórico das turmas passa a mentir. Desativa
/// e cria outro. <see cref="Usos"/> só sobe pelo <c>UPDATE</c> condicional do repositório — é ele, e não esta
/// classe, que garante o limite com dois checkouts ao mesmo tempo.
/// </para>
/// </remarks>
public partial class Cupom : Entity
{
    /// <summary>Teto do desconto (D3): cupom vaza em grupo de WhatsApp, e 50% é o máximo que pode vazar.</summary>
    public const int PercentualMaximo = 50;

    /// <summary>Tamanho mínimo do código: curto demais é adivinhável mesmo com o rate limit.</summary>
    public const int TamanhoMinimo = 6;

    /// <summary>Tamanho máximo do código.</summary>
    public const int TamanhoMaximo = 20;

    /// <summary>Código digitado no checkout, sempre maiúsculo.</summary>
    public string Codigo { get; init; } = string.Empty;

    /// <summary>Desconto na primeira cobrança, de 1 a <see cref="PercentualMaximo"/>.</summary>
    public int Percentual { get; init; }

    /// <summary>Último instante em que o cupom vale, em UTC.</summary>
    public DateTime ValidoAte { get; init; }

    /// <summary>Quantas turmas podem usar.</summary>
    public int LimiteDeUsos { get; init; }

    /// <summary>Quantas já usaram. Conta no checkout (D5).</summary>
    public int Usos { get; private set; }

    /// <summary>Desativado pelo Administrador: deixa de valer na hora, sem esperar a validade.</summary>
    public bool Ativo { get; private set; } = true;

    /// <summary>Deixa de valer para turmas novas. Quem já contratou com ele não muda.</summary>
    public void Desativar() => Ativo = false;

    /// <summary>Se ainda pode ser usado agora. A conferência definitiva do limite é a do repositório.</summary>
    /// <param name="agoraUtc">Momento da conferência.</param>
    public bool Disponivel(DateTime agoraUtc) => Ativo && Usos < LimiteDeUsos && ValidoAte > agoraUtc;

    /// <summary>O preço da primeira cobrança com o desconto.</summary>
    /// <remarks>O desconto arredonda para baixo: o Kapa perde o centavo, a turma não.</remarks>
    /// <param name="precoEmCentavos">Preço cheio do ciclo.</param>
    public long Aplicar(long precoEmCentavos) => ComDesconto(precoEmCentavos, Percentual);

    /// <summary>O preço com o percentual aplicado. Mora aqui para o front e o checkout não divergirem.</summary>
    /// <param name="precoEmCentavos">Preço cheio.</param>
    /// <param name="percentual">Desconto, em %.</param>
    public static long ComDesconto(long precoEmCentavos, int percentual) => precoEmCentavos - precoEmCentavos * percentual / 100;

    /// <summary>O código como é guardado: sem espaços nas pontas, maiúsculo.</summary>
    /// <param name="codigo">O que foi digitado.</param>
    public static string Normalizar(string? codigo) => (codigo ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Se o código tem a forma aceita: 6 a 20 de A–Z, 0–9 e hífen.</summary>
    /// <param name="codigo">Código já normalizado.</param>
    public static bool FormaValida(string codigo) => FormaDoCodigo().IsMatch(codigo);

    [GeneratedRegex("^[A-Z0-9-]{6,20}$")]
    private static partial Regex FormaDoCodigo();
}
