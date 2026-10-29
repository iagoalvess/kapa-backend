using Backend.Business.Abstractions;
using Backend.Business.Festa.Models;

namespace Backend.Business.Festa.Interfaces;

/// <summary>
/// As pessoas de fora da turma liberadas para a portaria de um evento (15/10/2026).
/// </summary>
public interface IAcessosDaPortariaService
{
    /// <summary>Os acessos que ainda valem no evento, do mais novo para o mais antigo; vazio depois da janela.</summary>
    /// <param name="eventoId">Evento da portaria.</param>
    Task<Result<IReadOnlyList<AcessoNaLista>>> Listar(Guid eventoId, CancellationToken ct = default);

    /// <summary>Libera uma pessoa: cria o acesso com o link por abrir.</summary>
    /// <param name="eventoId">Evento da portaria.</param>
    /// <param name="nome">Como a comissão chama a pessoa.</param>
    /// <param name="usuarioId">Quem libera.</param>
    Task<Result<AcessoNaLista>> Liberar(Guid eventoId, string? nome, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Corta o acesso e cria outro, com link novo, para a mesma pessoa — o celular trocou ou o link vazou.</summary>
    /// <param name="id">Acesso.</param>
    /// <param name="usuarioId">Quem libera.</param>
    Task<Result<AcessoNaLista>> NovoLink(Guid id, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Corta o acesso: o celular para de validar na requisição seguinte.</summary>
    /// <param name="id">Acesso.</param>
    Task<Result> Revogar(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Abre o link: gasta o segredo e devolve a credencial do celular. Anônimo.
    /// </summary>
    /// <remarks>
    /// Link gasto, cortado, inexistente ou de evento que já fechou respondem o mesmo 404: quem tem um link que não
    /// vale não precisa saber por quê, e a Gestão vê a situação na lista dela.
    /// </remarks>
    /// <param name="token">O segredo do link.</param>
    Task<Result<CredencialDaPortaria>> Ativar(string? token, CancellationToken ct = default);
}

/// <summary>Os acessos da portaria. Isolados pelo filtro global, menos os <c>DeTodasAsFormaturas</c>.</summary>
public interface IAcessoDaPortariaRepository
{
    /// <summary>Os acessos não cortados do evento, do mais novo para o mais antigo.</summary>
    /// <param name="eventoId">Evento.</param>
    Task<IReadOnlyList<AcessoDaPortaria>> Listar(Guid eventoId, CancellationToken ct = default);

    /// <summary>Um acesso para alterar; nulo se não existe nesta turma.</summary>
    /// <param name="id">Acesso.</param>
    Task<AcessoDaPortaria?> ObterParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>O acesso do link, de qualquer turma — o link é tudo o que o celular tem.</summary>
    /// <param name="token">Segredo do link.</param>
    Task<AcessoDaPortaria?> ObterPeloTokenDeTodasAsFormaturas(string token, CancellationToken ct = default);

    /// <summary>Se o acesso existe na turma, foi ativado e não foi cortado.</summary>
    /// <param name="id">Acesso.</param>
    /// <param name="formaturaId">Turma da credencial.</param>
    Task<bool> AtivoDeTodasAsFormaturas(Guid id, Guid formaturaId, CancellationToken ct = default);

    /// <summary>Marca o acesso novo para inclusão.</summary>
    /// <param name="acesso">Acesso.</param>
    Task Adicionar(AcessoDaPortaria acesso, CancellationToken ct = default);
}

/// <summary>Assina a credencial do celular da portaria.</summary>
/// <remarks>Implementada pelo <c>TokenService</c>, que já tem a chave.</remarks>
public interface IEmissorDaPortaria
{
    /// <summary>
    /// Um token que só o esquema da portaria aceita — a audiência é outra, e o resto da API o trata como anônimo.
    /// </summary>
    /// <param name="acessoId">Acesso; vira o <c>sub</c>.</param>
    /// <param name="nome">Nome da pessoa.</param>
    /// <param name="formaturaId">Turma.</param>
    /// <param name="eventoId">Evento da porta.</param>
    /// <param name="expiraEm">Fim da janela de validação, em UTC.</param>
    string Emitir(Guid acessoId, string nome, Guid formaturaId, Guid eventoId, DateTime expiraEm);
}
