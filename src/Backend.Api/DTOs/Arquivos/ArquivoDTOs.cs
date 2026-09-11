namespace Backend.Api.DTOs.Arquivos;

/// <summary>Metadados de um arquivo.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome original enviado.</param>
/// <param name="ContentType">Tipo do conteúdo.</param>
/// <param name="Tamanho">Tamanho em bytes.</param>
/// <param name="Categoria">Agrupamento lógico.</param>
/// <param name="EnviadoPorId">Usuário que enviou.</param>
/// <param name="CriadoEm">Momento do envio, em UTC (ISO 8601).</param>
public sealed record ArquivoResumoDTO(Guid Id, string Nome, string ContentType, long Tamanho, string Categoria, Guid EnviadoPorId, DateTime CriadoEm);
