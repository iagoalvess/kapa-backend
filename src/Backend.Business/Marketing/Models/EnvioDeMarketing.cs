using Backend.Business.Abstractions;

namespace Backend.Business.Marketing.Models;

/// <summary>
/// Um e-mail de marketing que o Kapa mandou: para quem, de qual jornada, sobre qual turma e quando.
/// </summary>
/// <remarks>
/// É o que impede mandar a mesma jornada duas vezes (índice único em usuário, turma e jornada), o que
/// segura a frequência (P3: um por pessoa a cada 14 dias) e o que a exportação da LGPD e o painel de
/// suporte mostram.
/// <para>
/// Não herda de <c>EntidadeDaFormatura</c>: a jornada é da pessoa, que atravessa turmas, e quem grava é o
/// worker sem turma na sessão. A turma fica como coluna comum — é o assunto do e-mail, não o dono da linha.
/// </para>
/// </remarks>
public class EnvioDeMarketing : Entity
{
    /// <summary>Quem recebeu.</summary>
    public Guid UsuarioId { get; init; }

    /// <summary>A turma de que o e-mail falava.</summary>
    public Guid FormaturaId { get; init; }

    /// <summary>Qual jornada — um de <see cref="JornadaDeMarketing"/>.</summary>
    public string Jornada { get; init; } = string.Empty;

    /// <summary>Quando entrou na fila, em UTC.</summary>
    public DateTime EnviadoEm { get; init; }
}

/// <summary>
/// As jornadas de ciclo de vida da primeira versão: só as de retomada (Sprint 40, P2).
/// </summary>
/// <remarks>Texto estável, gravado no banco — é dado, não enum.</remarks>
public static class JornadaDeMarketing
{
    /// <summary>Três dias depois de criar a turma, se ninguém da comissão entrou de novo.</summary>
    public const string CriouENaoVoltou = "criou_e_nao_voltou";

    /// <summary>Quatorze dias depois de criar, se tem plano de cobrança e nenhum formando.</summary>
    public const string MontouEParou = "montou_e_parou";

    /// <summary>A partir de quando, contado da criação da turma, "criou e não voltou" vence.</summary>
    public static readonly TimeSpan InicioDeCriouENaoVoltou = TimeSpan.FromDays(3);

    /// <summary>A partir de quando "montou e parou" vence — e "criou e não voltou" deixa de valer.</summary>
    public static readonly TimeSpan InicioDeMontouEParou = TimeSpan.FromDays(14);

    /// <summary>
    /// Até quando a turma ainda é assunto de jornada de retomada.
    /// </summary>
    /// <remarks>
    /// Sem teto, a primeira rodada depois do deploy falaria com toda turma antiga do gratuito — e
    /// "você criou a turma há 14 dias" para quem criou há seis meses é mentira.
    /// </remarks>
    public static readonly TimeSpan FimDasJornadas = TimeSpan.FromDays(30);

    /// <summary>O intervalo mínimo entre dois e-mails de marketing para a mesma pessoa (P3).</summary>
    public static readonly TimeSpan Intervalo = TimeSpan.FromDays(14);
}
