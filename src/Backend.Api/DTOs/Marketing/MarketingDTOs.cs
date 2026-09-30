namespace Backend.Api.DTOs.Marketing;

/// <summary>Liga ou desliga "Receber novidades do Kapa".</summary>
/// <param name="Receber">O valor novo.</param>
public sealed record ComunicacaoDoKapaRequestDTO(bool Receber);
