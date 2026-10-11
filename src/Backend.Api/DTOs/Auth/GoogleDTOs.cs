namespace Backend.Api.DTOs.Auth;

/// <summary>Convite público opcional para retomar a adesão depois da autenticação.</summary>
public sealed record InicioGoogleRequestDTO(string? Convite);

/// <summary>Credencial GIS e a tentativa iniciada pelo app.</summary>
public sealed record GoogleRequestDTO(string Tentativa, string Credencial);

/// <summary>Conclusão de cadastro; e-mail e identidade vêm do contexto validado.</summary>
public sealed record CadastroGoogleRequestDTO(
    string Contexto,
    string Nome,
    IReadOnlyList<AceiteDeDocumentoDTO?>? Aceites,
    bool? ReceberComunicacaoDoKapa
);

/// <summary>Solicitação de comprovação da conta existente.</summary>
public sealed record SolicitarVinculoGoogleRequestDTO(string Contexto);

/// <summary>Comprovante recebido por e-mail; contexto pode vir do cookie HttpOnly.</summary>
public sealed record VinculoGoogleRequestDTO(string? Contexto, string Comprovante);
