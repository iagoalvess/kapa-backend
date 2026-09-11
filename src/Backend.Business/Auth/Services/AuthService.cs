using Backend.Business.Abstractions;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Auth.Settings;
using Backend.Business.Common.Texto;
using Backend.Business.Usuarios.Models;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.Auth.Services;

/// <summary>
/// Registro, login, renovação e revogação de sessão.
/// </summary>
/// <param name="userManager">API do Identity para usuário e senha.</param>
/// <param name="tokenService">Emissão de tokens.</param>
/// <param name="refreshTokenRepository">Persistência dos refresh tokens.</param>
/// <param name="emailsDeConta">Montagem e envio das mensagens de conta.</param>
/// <param name="registrarValidator">Validador dos dados de registro.</param>
/// <param name="credenciaisValidator">Validador das credenciais de login.</param>
/// <param name="contaOptions">Regras do ciclo de vida da conta.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class AuthService(
    UserManager<Usuario> userManager,
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IEmailsDeConta emailsDeConta,
    IValidator<RegistrarUsuario> registrarValidator,
    IValidator<Credenciais> credenciaisValidator,
    IOptions<ContaSettings> contaOptions,
    IUnitOfWork unitOfWork,
    ILogger<AuthService> logger
) : IAuthService
{
    private static readonly Erro CredenciaisInvalidas = Erro.NaoAutenticado("auth.credenciais_invalidas", "E-mail ou senha incorretos.");

    private static readonly Erro SessaoInvalida = Erro.NaoAutenticado("auth.sessao_invalida", "Sessão expirada. Faça login novamente.");

    /// <inheritdoc />
    public async Task<Result<ParDeTokens>> Registrar(RegistrarUsuario dados, string? ipDeOrigem, CancellationToken ct = default)
    {
        var validacao = registrarValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ParDeTokens>(validacao.Erros);

        var email = dados.Email.Trim();

        if (await userManager.FindByEmailAsync(email) is not null)
            return Erro.Conflito("usuario.email_em_uso", "Já existe uma conta com este e-mail.");

        var usuario = new Usuario
        {
            Nome = dados.Nome.Trim(),
            Email = email,
            UserName = email,
        };

        var criacao = await userManager.CreateAsync(usuario, dados.Senha);
        if (!criacao.Succeeded)
            return Result.Falha<ParDeTokens>(TraduzirErrosDoIdentity(criacao));

        await userManager.AddToRoleAsync(usuario, PerfisPadrao.Usuario);

        var confirmacao = await userManager.GenerateEmailConfirmationTokenAsync(usuario);
        await emailsDeConta.EnfileirarConfirmacao(usuario, confirmacao, ct);

        logger.LogInformation("Conta criada para {Email}.", TextoUtils.MascararEmail(email));

        return await EmitirSessao(usuario, ipDeOrigem, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Toda falha de login devolve a **mesma** mensagem, com o mesmo código: dizer "usuário não
    /// existe" versus "senha incorreta" transforma o endpoint em um verificador de quais e-mails
    /// têm conta. A distinção real vai só para o log.
    /// </remarks>
    public async Task<Result<ParDeTokens>> Autenticar(Credenciais credenciais, string? ipDeOrigem, CancellationToken ct = default)
    {
        var validacao = credenciaisValidator.Validar(credenciais);
        if (validacao.Falhou)
            return Result.Falha<ParDeTokens>(validacao.Erros);

        var emailMascarado = TextoUtils.MascararEmail(credenciais.Email);
        var usuario = await userManager.FindByEmailAsync(credenciais.Email.Trim());

        if (usuario is null)
        {
            QueimarTempoDeHash(credenciais.Senha);
            logger.LogInformation("Login recusado para {Email}: conta inexistente.", emailMascarado);
            return CredenciaisInvalidas;
        }

        if (await userManager.IsLockedOutAsync(usuario))
        {
            logger.LogWarning("Login recusado para {Email}: conta bloqueada por tentativas.", emailMascarado);
            return Erro.NaoAutenticado("auth.conta_bloqueada", "Conta temporariamente bloqueada por excesso de tentativas. Tente mais tarde.");
        }

        if (!await userManager.CheckPasswordAsync(usuario, credenciais.Senha))
        {
            await userManager.AccessFailedAsync(usuario);
            logger.LogInformation("Login recusado para {Email}: senha incorreta.", emailMascarado);
            return CredenciaisInvalidas;
        }

        if (!usuario.Ativo)
        {
            logger.LogWarning("Login recusado para {Email}: conta desativada.", emailMascarado);
            return Erro.Proibido("auth.conta_desativada", "Esta conta está desativada. Procure um administrador.");
        }

        if (contaOptions.Value.ExigirEmailConfirmado && !usuario.EmailConfirmed)
        {
            logger.LogInformation("Login recusado para {Email}: e-mail ainda não confirmado.", emailMascarado);
            return Erro.Proibido("auth.email_nao_confirmado", "Confirme seu e-mail antes de entrar. Verifique sua caixa de entrada.");
        }

        await userManager.ResetAccessFailedCountAsync(usuario);

        return await EmitirSessao(usuario, ipDeOrigem, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A rotação é obrigatória: cada renovação revoga o token apresentado e emite outro. Um
    /// token já rotacionado voltar a aparecer só tem uma explicação — alguém copiou a
    /// credencial — e nesse caso **todas** as sessões do usuário caem, porque não há como saber
    /// se quem está apresentando é o dono ou o atacante.
    /// </remarks>
    public async Task<Result<ParDeTokens>> Renovar(string refreshToken, string? ipDeOrigem, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return SessaoInvalida;

        var agora = DateTime.UtcNow;
        var armazenado = await refreshTokenRepository.ObterPorHash(tokenService.CalcularHash(refreshToken), ct);

        if (armazenado is null)
            return SessaoInvalida;

        if (!armazenado.Ativo(agora))
        {
            if (armazenado.SubstituidoPorHash is not null)
            {
                logger.LogWarning(
                    "Reúso de refresh token detectado para o usuário {UsuarioId} vindo de {Ip}. Revogando todas as sessões.",
                    armazenado.UsuarioId,
                    ipDeOrigem ?? "desconhecido"
                );

                await refreshTokenRepository.RevogarTodosDoUsuario(armazenado.UsuarioId, agora, ct);
                await unitOfWork.SalvarAsync(ct);
            }

            return SessaoInvalida;
        }

        var usuario = await userManager.FindByIdAsync(armazenado.UsuarioId.ToString());
        if (usuario is null || !usuario.Ativo)
        {
            await refreshTokenRepository.RevogarTodosDoUsuario(armazenado.UsuarioId, agora, ct);
            await unitOfWork.SalvarAsync(ct);

            return SessaoInvalida;
        }

        var perfis = await userManager.GetRolesAsync(usuario);
        var acesso = tokenService.GerarAccessToken(usuario, [.. perfis]);
        var novoRefresh = tokenService.GerarRefreshToken();

        armazenado.RevogadoEm = agora;
        armazenado.SubstituidoPorHash = novoRefresh.Hash;

        await refreshTokenRepository.Adicionar(MontarRegistro(usuario.Id, novoRefresh, ipDeOrigem), ct);
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok(new ParDeTokens(acesso.Token, acesso.ExpiraEm, novoRefresh.Token));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Idempotente e silencioso: token inexistente ou já revogado devolve sucesso. Logout que
    /// responde "este token não existe" vira mais um oráculo para quem está testando valores.
    /// </remarks>
    public async Task<Result> Revogar(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Result.Ok();

        var armazenado = await refreshTokenRepository.ObterPorHash(tokenService.CalcularHash(refreshToken), ct);

        if (armazenado is null || armazenado.RevogadoEm is not null)
            return Result.Ok();

        armazenado.RevogadoEm = DateTime.UtcNow;
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <summary>
    /// Gasta, para um e-mail sem conta, o mesmo tempo que gastaria conferindo a senha de um.
    /// </summary>
    /// <remarks>
    /// A mensagem devolvida já é a mesma nos dois casos, mas o **relógio** não era: conta
    /// inexistente voltava na hora, enquanto conta existente pagava o PBKDF2 do Identity — uma
    /// diferença de ordens de grandeza, medível de fora e suficiente para varrer uma lista de
    /// e-mails e descobrir quais têm cadastro. O hash descartado aqui reequilibra os dois
    /// caminhos.
    /// </remarks>
    /// <param name="senha">Senha apresentada, usada só para o hash ter o mesmo custo do real.</param>
    private void QueimarTempoDeHash(string senha) => userManager.PasswordHasher.HashPassword(new Usuario(), senha);

    private async Task<Result<ParDeTokens>> EmitirSessao(Usuario usuario, string? ipDeOrigem, CancellationToken ct)
    {
        var perfis = await userManager.GetRolesAsync(usuario);
        var acesso = tokenService.GerarAccessToken(usuario, [.. perfis]);
        var refresh = tokenService.GerarRefreshToken();

        await refreshTokenRepository.Adicionar(MontarRegistro(usuario.Id, refresh, ipDeOrigem), ct);
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok(new ParDeTokens(acesso.Token, acesso.ExpiraEm, refresh.Token));
    }

    private static RefreshToken MontarRegistro(Guid usuarioId, RefreshTokenGerado gerado, string? ipDeOrigem) =>
        new()
        {
            UsuarioId = usuarioId,
            TokenHash = gerado.Hash,
            ExpiraEm = gerado.ExpiraEm,
            CriadoPorIp = ipDeOrigem,
        };

    private static IReadOnlyList<Erro> TraduzirErrosDoIdentity(IdentityResult resultado) =>
        [.. resultado.Errors.Select(erro => Erro.Validacao($"identity.{erro.Code}", erro.Description))];
}
