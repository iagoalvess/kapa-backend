namespace Backend.Api.DTOs.Marketing;

/// <summary>Preferência e histórico de marketing consultados pelo suporte.</summary>
public sealed record ComunicacaoDoKapaDTO(
    bool Receber,
    IReadOnlyList<RegistroDaComunicacaoDoKapaDTO> Historico,
    IReadOnlyList<EnvioDoKapaDTO> Envios
);

/// <summary>Aceite ou retirada registrada no cadastro, no e-mail ou pelo suporte.</summary>
public sealed record RegistroDaComunicacaoDoKapaDTO(bool Aceito, string Origem, string VersaoDoTexto, DateTime RegistradoEm);

/// <summary>Um e-mail de marketing enviado ao titular.</summary>
public sealed record EnvioDoKapaDTO(string Jornada, string Formatura, DateTime EnviadoEm);
