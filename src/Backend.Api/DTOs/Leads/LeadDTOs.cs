namespace Backend.Api.DTOs.Leads;

/// <summary>
/// O formulário de contato da página institucional.
/// </summary>
/// <remarks>
/// <see cref="Sobrenome"/> é o honeypot: o campo fica escondido por CSS e a pessoa nunca o vê.
/// Vindo preenchido, a API responde 204 e não grava nada — dizer "você é um robô" ensinaria o robô.
/// <para>
/// Os três <c>utm_*</c> chegam do próprio formulário, lidos da query string da landing pelo front:
/// a API não vê o <c>Referer</c> da primeira visita, só o da submissão.
/// </para>
/// </remarks>
/// <param name="Nome">Nome de quem preencheu.</param>
/// <param name="Email">E-mail de contato.</param>
/// <param name="Telefone">Telefone com DDD.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Curso">Curso.</param>
/// <param name="TamanhoDaTurma">Quantos formandos a turma tem.</param>
/// <param name="PrevisaoDeColacao">Mês previsto da colação, no formato <c>aaaa-mm</c>.</param>
/// <param name="Mensagem">Recado livre.</param>
/// <param name="AceitaPrivacidade">Se a caixa do consentimento foi marcada. Obrigatória.</param>
/// <param name="Origem"><c>utm_source</c>.</param>
/// <param name="Meio"><c>utm_medium</c>.</param>
/// <param name="Campanha"><c>utm_campaign</c>.</param>
/// <param name="Sobrenome">Honeypot; deixe vazio.</param>
public sealed record NovoLeadRequestDTO(
    string? Nome,
    string? Email,
    string? Telefone,
    string? Instituicao,
    string? Curso,
    int TamanhoDaTurma,
    string? PrevisaoDeColacao,
    string? Mensagem,
    bool AceitaPrivacidade,
    string? Origem,
    string? Meio,
    string? Campanha,
    string? Sobrenome
);

/// <summary>Um contato recebido, como o painel do administrador o lê.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome de quem preencheu.</param>
/// <param name="Email">E-mail de contato.</param>
/// <param name="Telefone">Telefone em E.164, ou nulo.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Curso">Curso.</param>
/// <param name="TamanhoDaTurma">Quantos formandos.</param>
/// <param name="PrevisaoDeColacao">Mês previsto da colação (primeiro dia do mês), ou nulo.</param>
/// <param name="Mensagem">Recado livre, ou nulo.</param>
/// <param name="Origem"><c>utm_source</c>, ou nulo.</param>
/// <param name="Meio"><c>utm_medium</c>, ou nulo.</param>
/// <param name="Campanha"><c>utm_campaign</c>, ou nulo.</param>
/// <param name="CriadoEm">Quando chegou, em UTC (ISO 8601).</param>
public sealed record LeadDTO(
    Guid Id,
    string Nome,
    string Email,
    string? Telefone,
    string Instituicao,
    string Curso,
    int TamanhoDaTurma,
    DateOnly? PrevisaoDeColacao,
    string? Mensagem,
    string? Origem,
    string? Meio,
    string? Campanha,
    DateTime CriadoEm
);
