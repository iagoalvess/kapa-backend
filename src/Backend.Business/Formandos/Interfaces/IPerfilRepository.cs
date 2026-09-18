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
    Task<MembroDoPerfil?> ObterMembro(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// O mesmo de <see cref="ObterMembro"/>, aceitando também o vínculo <b>desligado</b>.
    /// </summary>
    /// <remarks>
    /// Só o que é do próprio titular e só em leitura: o extrato e a adesão dele (P5 de 17/09/2026).
    /// O cadastro, os avisos e tudo o mais continuam em <see cref="ObterMembro"/>, que exige vínculo
    /// ativo — quem saiu consulta o que pagou, não edita a turma.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Membro.</param>
    Task<MembroDoPerfil?> ObterTitular(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>O cadastro do vínculo, sem rastreamento; nulo se ainda não foi criado.</summary>
    /// <param name="vinculoId">Vínculo dono.</param>
    Task<PerfilDoFormando?> ObterDoVinculo(Guid vinculoId, CancellationToken ct = default);

    /// <summary>O cadastro do vínculo, rastreado para alteração; nulo se ainda não foi criado.</summary>
    /// <param name="vinculoId">Vínculo dono.</param>
    Task<PerfilDoFormando?> ObterParaEdicao(Guid vinculoId, CancellationToken ct = default);

    /// <summary>Marca um cadastro novo para inclusão.</summary>
    /// <param name="perfil">Cadastro.</param>
    Task Adicionar(PerfilDoFormando perfil, CancellationToken ct = default);

    /// <summary>Marca o registro de uma correção feita pela comissão.</summary>
    /// <param name="correcao">Registro.</param>
    Task RegistrarCorrecao(CorrecaoDePerfil correcao, CancellationToken ct = default);
}
