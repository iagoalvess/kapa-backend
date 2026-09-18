using Backend.Business.Abstractions;
using Backend.Business.Leads.Models;

namespace Backend.Business.Leads.Interfaces;

/// <summary>
/// Acesso aos contatos deixados na página institucional.
/// </summary>
public interface ILeadRepository
{
    /// <summary>Marca um contato novo para gravação.</summary>
    /// <param name="lead">Contato a persistir.</param>
    Task Adicionar(Lead lead, CancellationToken ct = default);

    /// <summary>Uma página dos contatos, do mais recente para o mais antigo.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="busca">Trecho de nome, e-mail, instituição ou curso. Nulo lista todos.</param>
    Task<PaginaDe<LeadResumo>> Listar(PaginacaoRequest paginacao, string? busca, CancellationToken ct = default);

    /// <summary>
    /// Se este e-mail já deixou contato dentro da janela informada.
    /// </summary>
    /// <remarks>
    /// Trava contra o duplo clique e contra o mesmo formulário reenviado: sem ela, a caixa do
    /// comercial recebe a mesma pessoa três vezes e o painel vira lista de repetição.
    /// </remarks>
    /// <param name="email">E-mail em minúsculas.</param>
    /// <param name="desde">Início da janela, em UTC.</param>
    Task<bool> JaRegistrado(string email, DateTime desde, CancellationToken ct = default);
}

/// <summary>
/// O formulário de contato da página institucional.
/// </summary>
public interface ILeadService
{
    /// <summary>
    /// Registra um contato e avisa o comercial.
    /// </summary>
    /// <remarks>
    /// Anônimo. Honeypot preenchido e contato repetido respondem <b>sucesso</b> sem gravar: os dois
    /// são o mesmo tipo de resposta — nada a fazer, e nada a contar a quem enviou.
    /// </remarks>
    /// <param name="dados">O que o formulário enviou.</param>
    /// <param name="origem">IP e navegador, gravados como prova do consentimento.</param>
    Task<Result> Registrar(NovoLead dados, OrigemDoContato origem, CancellationToken ct = default);

    /// <summary>Os contatos recebidos, paginados. Só o <c>Administrador</c> da plataforma.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="busca">Trecho de nome, e-mail, instituição ou curso.</param>
    Task<Result<PaginaDe<LeadResumo>>> Listar(PaginacaoRequest paginacao, string? busca, CancellationToken ct = default);
}

/// <summary>De onde veio o contato — o que transforma a caixa marcada em prova.</summary>
/// <remarks>
/// Espelha <c>OrigemDoAceite</c> da Sprint 1, e é um tipo à parte porque vive na feature de leads:
/// uma feature não importa de outra, e <c>Legal</c> não tem por que conhecer o formulário da
/// página institucional.
/// </remarks>
/// <param name="EnderecoIp">IP do cliente, já corrigido pelos cabeçalhos de proxy confiável.</param>
/// <param name="UserAgent">Cabeçalho <c>User-Agent</c> da requisição.</param>
public sealed record OrigemDoContato(string? EnderecoIp, string? UserAgent);
