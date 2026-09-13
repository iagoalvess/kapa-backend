using Backend.Business.Abstractions;
using Backend.Business.Formandos.Models;

namespace Backend.Business.Formandos.Interfaces;

/// <summary>
/// Cadastros dos formandos da formatura selecionada.
/// </summary>
public interface IPerfilRepository
{
    /// <summary>O membro ativo da formatura, com o que vem da conta; nulo se não houver vínculo ativo.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Membro.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<MembroDoPerfil?> ObterMembro(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>O cadastro do vínculo, sem rastreamento; nulo se ainda não foi criado.</summary>
    /// <param name="vinculoId">Vínculo dono.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<PerfilDoFormando?> ObterDoVinculo(Guid vinculoId, CancellationToken ct = default);

    /// <summary>O cadastro do vínculo, rastreado para alteração; nulo se ainda não foi criado.</summary>
    /// <param name="vinculoId">Vínculo dono.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<PerfilDoFormando?> ObterParaEdicao(Guid vinculoId, CancellationToken ct = default);

    /// <summary>
    /// Uma página dos membros ativos da formatura com a completude de cada cadastro.
    /// </summary>
    /// <remarks>Quem ainda não abriu o cadastro aparece com 0% e o essencial pendente.</remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Busca e situação do cadastro.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<PaginaDe<FormandoResumo>> Listar(Guid formaturaId, PaginacaoRequest paginacao, FiltroDeFormandos filtro, CancellationToken ct = default);

    /// <summary>Marca um cadastro novo para inclusão.</summary>
    /// <param name="perfil">Cadastro.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task Adicionar(PerfilDoFormando perfil, CancellationToken ct = default);

    /// <summary>Marca o registro de uma correção feita pela comissão.</summary>
    /// <param name="correcao">Registro.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task RegistrarCorrecao(CorrecaoDePerfil correcao, CancellationToken ct = default);
}
