using Backend.Api.DTOs.Auth;

namespace Backend.Api.DTOs.Legal;

/// <summary>Uma versão de documento legal.</summary>
/// <param name="Id">Identificador da versão.</param>
/// <param name="Tipo"><c>TermosDeUso</c> ou <c>PoliticaDePrivacidade</c>.</param>
/// <param name="Versao">Rótulo da versão — é o que se envia no aceite.</param>
/// <param name="Conteudo">Texto integral, em markdown.</param>
/// <param name="VigenteDesde">A partir de quando vale, em UTC.</param>
public sealed record DocumentoLegalDTO(Guid Id, string Tipo, string Versao, string Conteudo, DateTime VigenteDesde);

/// <summary>Corpo do aceite feito por quem já tem conta.</summary>
/// <remarks>Lista anulável pelo mesmo motivo de <see cref="RegistrarRequestDTO"/>.</remarks>
/// <param name="Aceites">Versões vigentes aceitas.</param>
public sealed record RegistrarAceitesRequestDTO(IReadOnlyList<AceiteDeDocumentoDTO>? Aceites);

/// <summary>Uma linha do histórico de consentimento.</summary>
/// <param name="Tipo">Documento.</param>
/// <param name="Versao">Versão.</param>
/// <param name="AceitoEm">Momento do registro, em UTC.</param>
/// <param name="Revogado">Se a linha registra uma revogação.</param>
public sealed record ConsentimentoDoUsuarioDTO(string Tipo, string Versao, DateTime AceitoEm, bool Revogado);

/// <summary>Versão vigente ainda não aceita.</summary>
/// <param name="Tipo">Documento.</param>
/// <param name="Versao">Versão a aceitar.</param>
public sealed record AceitePendenteDTO(string Tipo, string Versao);

/// <summary>Histórico e pendências de consentimento do usuário.</summary>
/// <param name="Historico">Registros, do mais recente para o mais antigo.</param>
/// <param name="Pendencias">Versões vigentes sem aceite. Vazia quando está tudo em dia.</param>
public sealed record MeusAceitesDTO(IReadOnlyList<ConsentimentoDoUsuarioDTO> Historico, IReadOnlyList<AceitePendenteDTO> Pendencias);
