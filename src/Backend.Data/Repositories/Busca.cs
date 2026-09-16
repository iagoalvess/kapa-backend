using Backend.Business.Common.Texto;

namespace Backend.Data.Repositories;

/// <summary>
/// O termo digitado numa busca por nome, como o <c>ILIKE</c> o espera.
/// </summary>
/// <remarks>
/// As buscas por nome comparam <c>unaccent(coluna)</c> com o termo já sem acento: "julia" acha
/// "Júlia" e "sao joao" acha "São João" — em nome brasileiro, acento é a regra, e quem digita no
/// celular não põe. A coluna é desacentuada pelo <c>unaccent</c> do Postgres (a extensão é
/// declarada no <c>AppDbContext</c>); o termo, pelo <see cref="TextoUtils.SemAcento"/>, que
/// concorda com ele nos acentos do português.
/// <para>
/// ponytail: <c>%termo%</c> não usa índice, com ou sem <c>unaccent</c> — é varredura da turma, que
/// tem centenas de linhas. Com dezenas de milhares, o caminho é um índice GIN de trigrama sobre
/// <c>unaccent(nome)</c>.
/// </para>
/// </remarks>
internal static class Busca
{
    /// <summary>O padrão do <c>ILIKE</c>: o termo sem acento, entre curingas.</summary>
    /// <param name="termo">O que a pessoa digitou.</param>
    public static string Padrao(string termo) => $"%{TextoUtils.SemAcento(termo)}%";
}
