namespace Backend.Business.Leads.Models;

/// <summary>
/// O que o formulário da página institucional envia.
/// </summary>
/// <remarks>
/// <see cref="Sobrenome"/> é o <b>honeypot</b>: um campo escondido por CSS que nenhuma pessoa
/// enxerga e que todo robô de formulário preenche. Vindo com conteúdo, o envio é descartado — e
/// respondido como se tivesse dado certo, porque dizer "você é um robô" ensina o robô a passar.
/// <para>
/// <c>ponytail:</c> captcha só quando o honeypot deixar passar. Ele é um campo escondido e um
/// <c>if</c>; o captcha é uma dependência de terceiro, uma chave para rotacionar e um obstáculo
/// para quem tem leitor de tela.
/// </para>
/// </remarks>
/// <param name="Nome">Nome de quem preencheu.</param>
/// <param name="Email">E-mail de contato.</param>
/// <param name="Telefone">Telefone com DDD.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Curso">Curso.</param>
/// <param name="TamanhoDaTurma">Quantos formandos.</param>
/// <param name="PrevisaoDeColacao">Mês previsto da colação, no formato <c>aaaa-mm</c>.</param>
/// <param name="Mensagem">Recado livre.</param>
/// <param name="AceitaPrivacidade">Se a caixa do consentimento foi marcada.</param>
/// <param name="Origem"><c>utm_source</c> da visita.</param>
/// <param name="Meio"><c>utm_medium</c> da visita.</param>
/// <param name="Campanha"><c>utm_campaign</c> da visita.</param>
/// <param name="Sobrenome">Honeypot. Preenchido só por robô.</param>
public sealed record NovoLead(
    string Nome,
    string Email,
    string? Telefone,
    string Instituicao,
    string Curso,
    int TamanhoDaTurma,
    string? PrevisaoDeColacao,
    string? Mensagem,
    bool AceitaPrivacidade,
    string? Origem,
    string? Meio,
    string? Campanha,
    string? Sobrenome
);

/// <summary>Um contato como o comercial o lê no painel.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome de quem preencheu.</param>
/// <param name="Email">E-mail de contato.</param>
/// <param name="Telefone">Telefone em E.164, ou nulo.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Curso">Curso.</param>
/// <param name="TamanhoDaTurma">Quantos formandos.</param>
/// <param name="PrevisaoDeColacao">Mês previsto da colação, ou nulo.</param>
/// <param name="Mensagem">Recado livre, ou nulo.</param>
/// <param name="Origem"><c>utm_source</c>, ou nulo.</param>
/// <param name="Meio"><c>utm_medium</c>, ou nulo.</param>
/// <param name="Campanha"><c>utm_campaign</c>, ou nulo.</param>
/// <param name="CriadoEm">Quando chegou, em UTC.</param>
public sealed record LeadResumo(
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
