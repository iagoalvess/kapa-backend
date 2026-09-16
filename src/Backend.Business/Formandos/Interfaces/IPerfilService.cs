using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Formandos.Models;

namespace Backend.Business.Formandos.Interfaces;

/// <summary>
/// Cadastro do formando: o próprio preenche, a comissão consulta e corrige.
/// </summary>
/// <remarks>
/// O usuário vem sempre de quem chama — do token, no <c>/eu</c>; da rota, só nos endpoints da
/// comissão. A formatura vem sempre da sessão.
/// </remarks>
public interface IPerfilService
{
    /// <summary>O cadastro do membro, vazio se ele ainda não preencheu nada.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Membro.</param>
    Task<Result<PerfilDetalhe>> Obter(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>O cadastro de um formando como a comissão o vê: com o CPF mascarado.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Formando.</param>
    Task<Result<PerfilDetalhe>> ObterParaComissao(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>O formando altera o próprio cadastro.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Usuário do token.</param>
    /// <param name="dados">Seções a gravar.</param>
    Task<Result<PerfilDetalhe>> Atualizar(Guid formaturaId, Guid usuarioId, AtualizarPerfil dados, CancellationToken ct = default);

    /// <summary>A comissão corrige o cadastro de um formando, com registro do autor. O CPF fica como está.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Formando corrigido.</param>
    /// <param name="autorId">Quem corrige.</param>
    /// <param name="dados">Seções a gravar.</param>
    Task<Result<PerfilDetalhe>> Corrigir(Guid formaturaId, Guid usuarioId, Guid autorId, AtualizarPerfil dados, CancellationToken ct = default);

    /// <summary>Troca a foto do formando por uma imagem nova, redimensionada no servidor.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Usuário do token.</param>
    /// <param name="conteudo">Bytes enviados. Quem chama é dono do descarte.</param>
    /// <param name="tamanho">Tamanho declarado do envio, em bytes.</param>
    Task<Result<PerfilDetalhe>> EnviarFoto(Guid formaturaId, Guid usuarioId, Stream conteudo, long tamanho, CancellationToken ct = default);

    /// <summary>A foto de um formando da turma, para a comissão ver.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Formando.</param>
    Task<Result<ArquivoParaDownload>> BaixarFoto(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);
}
