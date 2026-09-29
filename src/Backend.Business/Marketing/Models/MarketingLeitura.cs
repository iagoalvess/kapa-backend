namespace Backend.Business.Marketing.Models;

/// <summary>
/// Uma pessoa da comissão de uma turma do gratuito que pode ouvir o Kapa, com o que a turma fez até agora.
/// </summary>
/// <remarks>
/// A consulta já deixou de fora quem não pode receber nada: formando, menor, comprador (só entra vínculo de
/// Gestão), quem não marcou a caixa, quem recebeu marketing há menos de 14 dias, turma que contratou e turma
/// fora de <c>Ativa</c>. Qual jornada venceu é decisão de <see cref="JornadaVencida"/>, testável sem banco.
/// </remarks>
/// <param name="UsuarioId">Quem recebe.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="FormaturaId">A turma de que o e-mail fala.</param>
/// <param name="Formatura">Nome da turma.</param>
/// <param name="TurmaCriadaEm">Quando a turma nasceu, em UTC — o relógio das jornadas.</param>
/// <param name="Criador">Se foi esta pessoa que criou a turma — muda o "por que você recebe".</param>
/// <param name="ComissaoVoltou">Se alguém da comissão abriu sessão a partir do dia seguinte à criação.</param>
/// <param name="TemPlanoDeCobranca">Se a turma já tem item de cobrança.</param>
/// <param name="TemFormando">Se a turma já tem formando com vínculo ativo.</param>
/// <param name="RecebeuCriouENaoVoltou">Se esta pessoa já recebeu "criou e não voltou" desta turma.</param>
/// <param name="RecebeuMontouEParou">Se esta pessoa já recebeu "montou e parou" desta turma.</param>
public sealed record CandidatoDeMarketing(
    Guid UsuarioId,
    string Nome,
    string Email,
    Guid FormaturaId,
    string Formatura,
    DateTime TurmaCriadaEm,
    bool Criador,
    bool ComissaoVoltou,
    bool TemPlanoDeCobranca,
    bool TemFormando,
    bool RecebeuCriouENaoVoltou,
    bool RecebeuMontouEParou
)
{
    /// <summary>
    /// A jornada que venceu agora para esta pessoa e esta turma, ou nulo.
    /// </summary>
    /// <remarks>
    /// As janelas não se sobrepõem: de 3 a 14 dias só "criou e não voltou", de 14 a 30 só "montou e parou".
    /// Quem recebeu a primeira no dia 3 fica sem a segunda até o dia 17 pela trava dos 14 dias (P3) — e
    /// ainda cabe na janela, que vai até o dia 30.
    /// </remarks>
    /// <param name="agoraUtc">Momento da rodada.</param>
    public string? JornadaVencida(DateTime agoraUtc)
    {
        var idade = agoraUtc - TurmaCriadaEm;

        if (idade >= JornadaDeMarketing.FimDasJornadas)
            return null;

        if (idade >= JornadaDeMarketing.InicioDeMontouEParou)
            return TemPlanoDeCobranca && !TemFormando && !RecebeuMontouEParou ? JornadaDeMarketing.MontouEParou : null;

        if (idade >= JornadaDeMarketing.InicioDeCriouENaoVoltou)
            return !ComissaoVoltou && !RecebeuCriouENaoVoltou ? JornadaDeMarketing.CriouENaoVoltou : null;

        return null;
    }
}

/// <summary>Um e-mail de marketing já mandado, como a exportação e o suporte o mostram.</summary>
/// <param name="Jornada">Qual jornada.</param>
/// <param name="Formatura">Nome da turma de que ele falava.</param>
/// <param name="EnviadoEm">Quando, em UTC.</param>
public sealed record EnvioDoKapa(string Jornada, string Formatura, DateTime EnviadoEm);

/// <summary>Uma linha do histórico da preferência, como a exportação a mostra.</summary>
/// <param name="Aceito">Aceite ou oposição.</param>
/// <param name="Origem">De onde veio.</param>
/// <param name="VersaoDoTexto">Versão do texto da caixa.</param>
/// <param name="RegistradoEm">Quando, em UTC.</param>
public sealed record RegistroDaComunicacaoDoKapa(bool Aceito, string Origem, string VersaoDoTexto, DateTime RegistradoEm);

/// <summary>A preferência de marketing da pessoa, o histórico dela e o que já foi mandado.</summary>
/// <param name="Receber">Se recebe hoje.</param>
/// <param name="Historico">Aceites e oposições, do mais recente.</param>
/// <param name="Envios">E-mails de marketing mandados, do mais recente.</param>
public sealed record ComunicacaoDoKapa(bool Receber, IReadOnlyList<RegistroDaComunicacaoDoKapa> Historico, IReadOnlyList<EnvioDoKapa> Envios);
