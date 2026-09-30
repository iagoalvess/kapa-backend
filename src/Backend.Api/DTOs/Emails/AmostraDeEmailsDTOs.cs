namespace Backend.Api.DTOs.Emails;

/// <summary>A amostra de e-mails que entrou na fila.</summary>
/// <param name="EnfileiradoEm">Quando a amostra começou a entrar na fila, em UTC.</param>
/// <param name="Para">Quem recebe.</param>
public sealed record AmostraDeEmailsDTO(DateTime EnfileiradoEm, string Para);
