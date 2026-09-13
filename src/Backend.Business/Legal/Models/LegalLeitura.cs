namespace Backend.Business.Legal.Models;

/// <summary>Uma versão de documento, com o texto.</summary>
/// <param name="Id">Identificador da versão.</param>
/// <param name="Tipo">Qual documento. Ver <see cref="TipoDeDocumento"/>.</param>
/// <param name="Versao">Rótulo da versão.</param>
/// <param name="Conteudo">Texto integral, em markdown.</param>
/// <param name="VigenteDesde">A partir de quando vale, em UTC.</param>
public sealed record VersaoDeDocumento(Guid Id, string Tipo, string Versao, string Conteudo, DateTime VigenteDesde);

/// <summary>A versão que o usuário diz ter lido e aceitado.</summary>
/// <param name="Tipo">Qual documento.</param>
/// <param name="Versao">Rótulo da versão lida.</param>
public sealed record AceiteDeDocumento(string Tipo, string Versao);

/// <summary>De onde veio o aceite — o que transforma um clique em prova.</summary>
/// <param name="EnderecoIp">IP do cliente, já corrigido pelos cabeçalhos de proxy confiável.</param>
/// <param name="UserAgent">Cabeçalho <c>User-Agent</c> da requisição.</param>
public sealed record OrigemDoAceite(string? EnderecoIp, string? UserAgent);

/// <summary>Uma linha do histórico de consentimento do usuário.</summary>
/// <param name="Tipo">Documento aceito.</param>
/// <param name="Versao">Versão aceita.</param>
/// <param name="AceitoEm">Momento do registro, em UTC.</param>
/// <param name="Revogado">Se a linha registra uma revogação.</param>
public sealed record ConsentimentoDoUsuario(string Tipo, string Versao, DateTime AceitoEm, bool Revogado);

/// <summary>Documento vigente que o usuário ainda não aceitou.</summary>
/// <param name="Tipo">Documento.</param>
/// <param name="Versao">Versão vigente, que é a que precisa ser aceita.</param>
public sealed record AceitePendente(string Tipo, string Versao);

/// <summary>Histórico de consentimento e o que falta aceitar.</summary>
/// <param name="Historico">Registros do usuário, do mais recente para o mais antigo.</param>
/// <param name="Pendencias">Versões vigentes sem aceite válido.</param>
public sealed record MeusAceites(IReadOnlyList<ConsentimentoDoUsuario> Historico, IReadOnlyList<AceitePendente> Pendencias);

/// <summary>Pedido de aceite feito por quem já tem conta.</summary>
/// <param name="Aceites">Versões aceitas.</param>
public sealed record RegistrarAceites(IReadOnlyList<AceiteDeDocumento> Aceites);
