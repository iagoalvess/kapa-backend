using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Usuarios.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Privacidade.Services;

/// <summary>
/// Torna o titular irreconhecível e preserva o registro financeiro da turma.
/// </summary>
/// <remarks>
/// <b>O que some:</b> nome, e-mail, telefone, CPF, RG, matrícula, data de nascimento, endereço,
/// contato de emergência, observações e a foto — na conta e em todos os cadastros de turma.
/// <para>
/// <b>O que fica:</b> parcelas, recebimentos, despesas e a adesão. Quem pagou R$ 8.400 à turma não
/// pode ter o lançamento apagado — a comissão presta contas com ele e a guarda fiscal o exige. Os
/// lançamentos passam a apontar para um marcador (<c>Formando #a1b2c3</c>), estável e irreversível:
/// dá para seguir o dinheiro de uma pessoa sem saber quem ela é.
/// </para>
/// <para>
/// <b>O limite, escrito:</b> <c>adesoes</c> e <c>consentimentos</c> são <i>append-only</i> no banco
/// — o gatilho recusa <c>UPDATE</c> e <c>DELETE</c>, e é essa recusa que faz delas prova. A adesão
/// guarda o nome e o CPF do instante do aceite, porque um contrato sem quem o assinou não prova
/// contrato nenhum (LGPD, art. 16, I e II). O que a Sprint 14 faz com eles é <b>não servi-los</b>:
/// <c>Usuario.AnonimizadoEm</c> passa a mascarar essas leituras. A linha segue no banco, alcançável
/// por ordem judicial e por mais ninguém.
/// </para>
/// </remarks>
/// <param name="privacidadeRepository">Cadastros do titular em todas as turmas.</param>
/// <param name="userManager">A conta do Identity.</param>
/// <param name="arquivoService">Onde a foto mora.</param>
/// <param name="eventos">Trilha de auditoria.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class AnonimizacaoDeTitular(
    IPrivacidadeRepository privacidadeRepository,
    UserManager<Usuario> userManager,
    IArquivoService arquivoService,
    IEventoRepository eventos,
    IUnitOfWork unitOfWork,
    ILogger<AnonimizacaoDeTitular> logger
) : IAnonimizacaoDeTitular
{
    /// <summary>Domínio reservado que nunca resolve (RFC 2606), para o e-mail anonimizado.</summary>
    /// <remarks>
    /// O Identity exige e-mail único, então não dá para deixá-lo vazio. Um domínio reservado garante
    /// que a caixa não existe em lugar nenhum — e que ninguém registre depois o endereço de alguém
    /// que pediu para ser esquecido.
    /// </remarks>
    private const string DominioAnonimo = "anonimizado.invalid";

    /// <summary>
    /// O marcador que substitui o nome nos lançamentos.
    /// </summary>
    /// <remarks>
    /// Derivado do id, e não de um contador: um contador por turma exigiria coluna nova e daria
    /// números diferentes à mesma pessoa em duas turmas, o que é o oposto do que a decisão 2 pede.
    /// Seis dígitos hexadecimais bastam para a assembleia distinguir duas pessoas na mesma lista, e
    /// não voltam ao id — o id inteiro continua na chave estrangeira, que é o que amarra o dinheiro.
    /// </remarks>
    /// <param name="usuarioId">Titular.</param>
    public static string Marcador(Guid usuarioId) => $"Formando #{usuarioId.ToString("N")[..6]}";

    /// <inheritdoc />
    public async Task<Result<string>> Anonimizar(Guid usuarioId, CancellationToken ct = default)
    {
        var usuario = await userManager.FindByIdAsync(usuarioId.ToString());

        if (usuario is null)
            return Erro.NaoEncontrado("privacidade.titular_nao_encontrado", "Titular não encontrado.");

        var marcador = Marcador(usuarioId);

        if (usuario.AnonimizadoEm is not null)
            return Result.Ok(marcador);

        var emailOriginal = usuario.Email;

        await AnonimizarCadastros(usuarioId, ct);

        usuario.Nome = marcador;
        usuario.Email = $"{usuarioId:N}@{DominioAnonimo}";
        usuario.NormalizedEmail = userManager.NormalizeEmail(usuario.Email);
        usuario.UserName = usuario.Email;
        usuario.NormalizedUserName = userManager.NormalizeName(usuario.UserName);
        usuario.PhoneNumber = null;
        usuario.PhoneNumberConfirmed = false;
        usuario.EmailConfirmed = false;
        usuario.Ativo = false;
        usuario.AnonimizadoEm = DateTime.UtcNow;

        await userManager.UpdateSecurityStampAsync(usuario);

        var atualizacao = await userManager.UpdateAsync(usuario);

        if (!atualizacao.Succeeded)
            return Erro.Conflito("privacidade.anonimizacao_recusada", "Não foi possível anonimizar a conta. Tente de novo.");

        await eventos.Auditar(
            NomesDeAuditoria.TitularAnonimizado,
            usuarioId,
            new
            {
                titularUsuarioId = usuarioId,
                marcador,
                emailOriginalMascarado = Mascarar(emailOriginal),
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        logger.LogWarning("Titular {UsuarioId} anonimizado por pedido de eliminação; passou a ser {Marcador}.", usuarioId, marcador);

        return Result.Ok(marcador);
    }

    /// <summary>Esvazia os cadastros de turma e apaga os bytes das fotos.</summary>
    /// <remarks>
    /// Os bytes primeiro, a coluna depois: o contrário deixaria a foto no provedor sem ninguém que
    /// a referencie, e ela não seria apagada nunca mais.
    /// </remarks>
    /// <param name="usuarioId">Titular.</param>
    private async Task AnonimizarCadastros(Guid usuarioId, CancellationToken ct)
    {
        var perfis = await privacidadeRepository.ListarPerfisParaAnonimizarDeTodasAsFormaturas(usuarioId, ct);

        foreach (var perfil in perfis)
        {
            if (perfil.Anonimizar() is not { } foto)
                continue;

            var remocao = await arquivoService.Remover(foto, new SolicitanteDeArquivo(usuarioId, EhAdministrador: true), ct);

            if (remocao.Falhou)
                logger.LogWarning("Foto {ArquivoId} do titular {UsuarioId} não foi apagada: {Codigo}.", foto, usuarioId, remocao.PrimeiroErro.Codigo);
        }
    }

    /// <summary>
    /// O e-mail original reduzido ao que não identifica: <c>j***@g***</c>.
    /// </summary>
    /// <remarks>
    /// Guardar o e-mail inteiro no evento desfaria a anonimização com um <c>SELECT</c>. Guardar nada
    /// tira da trilha a única pista de que a conta certa foi atendida — a mascarada é o meio-termo
    /// que serve para conferir e não serve para encontrar.
    /// </remarks>
    /// <param name="email">E-mail antes da anonimização.</param>
    private static string Mascarar(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.IndexOf('@', StringComparison.Ordinal) is var arroba && arroba <= 0)
            return "***";

        var dominio = email[(arroba + 1)..];

        return $"{email[0]}***@{(dominio.Length > 0 ? dominio[0] : '*')}***";
    }
}
