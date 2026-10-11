using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Auth.Settings;
using Backend.Business.Legal.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Backend.Business.Auth.Services;

/// <summary>SSO sem senha, preservando conta, permissões e confirmação das ações sensíveis.</summary>
/// <remarks>Contextos são rotacionados e consumidos sob trava na mesma transação do vínculo/cadastro.</remarks>
public sealed class GoogleService(
    UserManager<Usuario> usuarios,
    IAuthService auth,
    IValidadorGoogle validador,
    ITentativaGoogleRepository tentativas,
    IUnitOfWork unidade,
    IEmailsDeConta emails,
    TentativasDeSenha limites,
    IOptions<GoogleSettings> options
) : IGoogleService
{
    private static readonly Erro ContextoInvalido = Erro.NaoAutenticado(
        "auth.google_contexto_invalido",
        "Esta tentativa expirou ou já foi usada. Entre com Google novamente."
    );
    private static readonly Erro Desligado = Erro.NaoEncontrado("auth.google_desligado", "A entrada com Google não está disponível.");
    private static readonly Erro NaoPermitido = Erro.Proibido("auth.google_nao_permitido", "Use e-mail e senha para entrar nesta conta.");

    /// <inheritdoc />
    public async Task<Result<InicioGoogle>> Iniciar(string navegador, string? convite, CancellationToken ct = default)
    {
        if (!options.Value.Habilitado)
            return Desligado;
        if (convite?.Length > 2048)
            return Erro.Validacao("auth.google_convite_invalido", "Abra o convite novamente.");
        var token = NovoToken();
        var nonce = NovoToken();
        await tentativas.Adicionar(
            new TentativaGoogle
            {
                TokenHash = Hash(token),
                NavegadorHash = Hash(navegador),
                Nonce = nonce,
                ExpiraEm = DateTime.UtcNow.AddMinutes(10),
                Convite = convite,
            },
            ct
        );
        await unidade.SalvarAsync(ct);
        return new InicioGoogle(token, nonce);
    }

    /// <inheritdoc />
    public Task<Result<ResultadoGoogle>> Entrar(string tentativa, string credencial, string navegador, string? ip, CancellationToken ct = default) =>
        unidade.EmTransacaoAsync(
            async token =>
            {
                if (!options.Value.Habilitado)
                    return Result.Falha<ResultadoGoogle>(Desligado);
                var contexto = await Obter(tentativa, navegador, "entrada", token);
                if (contexto is null)
                    return Result.Falha<ResultadoGoogle>(ContextoInvalido);
                var identidade = await validador.Validar(credencial, contexto.Nonce, token);
                if (identidade.Falhou)
                    return Result.Falha<ResultadoGoogle>(identidade.Erros);
                var google = identidade.Valor;
                await tentativas.TravarIdentidade("google:" + google.Sub, token);
                await tentativas.TravarIdentidade("email:" + usuarios.NormalizeEmail(google.Email), token);
                var usuario = await usuarios.FindByLoginAsync("Google", google.Sub);
                if (usuario is not null)
                {
                    var entrada = await auth.FinalizarEntrada(usuario.Id, ip, true, token);
                    if (entrada.Falhou)
                        return Result.Falha<ResultadoGoogle>(entrada.Erros);
                    contexto.ConsumidaEm = DateTime.UtcNow;
                    return Result.Ok(new ResultadoGoogle(entrada.Valor.Sessao, null, null));
                }
                usuario = await usuarios.FindByEmailAsync(google.Email);
                if (
                    usuario is not null
                    && (!usuario.Ativo || usuario.AnonimizadoEm is not null || await usuarios.IsInRoleAsync(usuario, PerfisPadrao.Administrador))
                )
                    return Result.Falha<ResultadoGoogle>(NaoPermitido);
                if (usuario is { EmailConfirmed: true } && google.AutoridadeSobreEmail)
                {
                    var adicao = await usuarios.AddLoginAsync(usuario, new UserLoginInfo("Google", google.Sub, "Google"));
                    if (!adicao.Succeeded)
                        return Result.Falha<ResultadoGoogle>(ContextoInvalido);
                    var entrada = await auth.FinalizarEntrada(usuario.Id, ip, true, token);
                    if (entrada.Falhou)
                        return Result.Falha<ResultadoGoogle>(entrada.Erros);
                    contexto.ConsumidaEm = DateTime.UtcNow;
                    return Result.Ok(new ResultadoGoogle(entrada.Valor.Sessao, null, null));
                }
                var proximo = NovoToken();
                contexto.TokenHash = Hash(proximo);
                contexto.Nonce = string.Empty;
                contexto.Etapa = usuario is null ? "cadastro" : "vinculo";
                contexto.Sub = google.Sub;
                contexto.Email = google.Email;
                contexto.Nome = google.Nome;
                contexto.AutoridadeSobreEmail = google.AutoridadeSobreEmail;
                contexto.UsuarioId = usuario?.Id;
                contexto.SecurityStamp = usuario?.SecurityStamp;
                contexto.ExigeRecuperacao = usuario is { EmailConfirmed: false } && await usuarios.HasPasswordAsync(usuario);
                contexto.ExpiraEm = DateTime.UtcNow.AddMinutes(15);
                return Result.Ok(
                    new ResultadoGoogle(
                        null,
                        new ContextoGoogle(proximo, google.Nome, google.Email, contexto.ExigeRecuperacao),
                        usuario is null
                            ? Erro.Conflito("auth.cadastro_necessario", "Conclua seu cadastro para entrar.")
                            : Erro.Conflito("auth.google_vinculo_necessario", "Confirme sua conta para vincular Google.")
                    )
                );
            },
            ct
        );

    /// <inheritdoc />
    public Task<Result<ParDeTokens>> Cadastrar(CadastroGoogle dados, string navegador, OrigemDoAceite origem, CancellationToken ct = default) =>
        unidade.EmTransacaoAsync(
            async token =>
            {
                if (!options.Value.Habilitado)
                    return Result.Falha<ParDeTokens>(Desligado);
                var contexto = await Obter(dados.Contexto, navegador, "cadastro", token);
                if (contexto is null)
                    return Result.Falha<ParDeTokens>(ContextoInvalido);
                await tentativas.TravarIdentidade("google:" + contexto.Sub, token);
                await tentativas.TravarIdentidade("email:" + usuarios.NormalizeEmail(contexto.Email!), token);
                if (await usuarios.FindByLoginAsync("Google", contexto.Sub!) is not null)
                    return Result.Falha<ParDeTokens>(ContextoInvalido);
                var criado = await auth.RegistrarComGoogle(
                    new RegistroComGoogle(
                        dados.Nome,
                        contexto.Email!,
                        contexto.Sub!,
                        contexto.AutoridadeSobreEmail,
                        dados.Aceites,
                        dados.ReceberComunicacaoDoKapa
                    ),
                    origem,
                    token
                );
                if (criado.Falhou)
                    return criado;
                contexto.ConsumidaEm = DateTime.UtcNow;
                return criado;
            },
            ct
        );

    /// <inheritdoc />
    public Task<Result> SolicitarVinculo(string contexto, string navegador, CancellationToken ct = default) =>
        unidade.EmTransacaoAsync(
            async token =>
            {
                if (!options.Value.Habilitado)
                    return Result.Falha(Desligado);
                var tentativa = await Obter(contexto, navegador, "vinculo", token);
                if (tentativa is null)
                    return Result.Ok();
                var usuario = await usuarios.FindByIdAsync(tentativa.UsuarioId.ToString()!);
                if (
                    usuario is null
                    || !usuario.Ativo
                    || usuario.AnonimizadoEm is not null
                    || await usuarios.IsInRoleAsync(usuario, PerfisPadrao.Administrador)
                )
                    return Result.Ok();
                var comprovante = NovoToken();
                tentativa.ComprovanteHash = Hash(comprovante);
                if (tentativa.ExigeRecuperacao)
                    await emails.EnfileirarRedefinicaoDeSenha(usuario, await usuarios.GeneratePasswordResetTokenAsync(usuario), token);
                await emails.EnfileirarVinculoGoogle(usuario, comprovante, tentativa.ExigeRecuperacao, tentativa.Convite, token);
                return Result.Ok();
            },
            ct
        );

    /// <inheritdoc />
    public Task<Result<ParDeTokens>> Vincular(string contexto, string comprovante, string navegador, string? ip, CancellationToken ct = default) =>
        unidade.EmTransacaoAsync(
            async token =>
            {
                if (!options.Value.Habilitado)
                    return Result.Falha<ParDeTokens>(Desligado);
                var tentativa = await Obter(contexto, navegador, "vinculo", token);
                if (tentativa is null)
                    return Result.Falha<ParDeTokens>(ContextoInvalido);
                var chave = "google-vinculo:" + tentativa.Id;
                if (limites.Bloqueada(chave, null))
                    return Result.Falha<ParDeTokens>(ContextoInvalido);
                if (
                    string.IsNullOrWhiteSpace(comprovante)
                    || comprovante.Length > 256
                    || tentativa.ComprovanteHash is null
                    || !CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(tentativa.ComprovanteHash),
                        Encoding.ASCII.GetBytes(Hash(comprovante))
                    )
                )
                {
                    limites.RegistrarFalha(chave, null);
                    return Result.Falha<ParDeTokens>(ContextoInvalido);
                }
                await tentativas.TravarIdentidade("google:" + tentativa.Sub, token);
                await tentativas.TravarIdentidade("email:" + usuarios.NormalizeEmail(tentativa.Email!), token);
                var usuario = await usuarios.FindByIdAsync(tentativa.UsuarioId.ToString()!);
                if (
                    usuario is null
                    || !usuario.Ativo
                    || usuario.AnonimizadoEm is not null
                    || await usuarios.IsInRoleAsync(usuario, PerfisPadrao.Administrador)
                )
                    return Result.Falha<ParDeTokens>(NaoPermitido);
                if (usuarios.NormalizeEmail(usuario.Email!) != usuarios.NormalizeEmail(tentativa.Email!))
                    return Result.Falha<ParDeTokens>(ContextoInvalido);
                if (tentativa.ExigeRecuperacao && usuario.SecurityStamp == tentativa.SecurityStamp)
                    return Result.Falha<ParDeTokens>(
                        Erro.Proibido("auth.google_recuperacao_necessaria", "Redefina sua senha pelo link recebido antes de vincular o Google.")
                    );
                if (await usuarios.FindByLoginAsync("Google", tentativa.Sub!) is not null)
                    return Result.Falha<ParDeTokens>(ContextoInvalido);
                if (!usuario.EmailConfirmed)
                {
                    var confirmacao = await usuarios.GenerateEmailConfirmationTokenAsync(usuario);
                    if (!(await usuarios.ConfirmEmailAsync(usuario, confirmacao)).Succeeded)
                        return Result.Falha<ParDeTokens>(ContextoInvalido);
                }
                var resultado = await usuarios.AddLoginAsync(usuario, new UserLoginInfo("Google", tentativa.Sub!, "Google"));
                if (!resultado.Succeeded)
                    return Result.Falha<ParDeTokens>(ContextoInvalido);
                var entrada = await auth.FinalizarEntrada(usuario.Id, ip, true, token);
                if (entrada.Falhou)
                    return Result.Falha<ParDeTokens>(entrada.Erros);
                tentativa.ConsumidaEm = DateTime.UtcNow;
                limites.Limpar(chave, null);
                return Result.Ok(entrada.Valor.Sessao!);
            },
            ct
        );

    private async Task<TentativaGoogle?> Obter(string token, string navegador, string etapa, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(navegador))
            return null;
        var tentativa = await tentativas.Travar(Hash(token), ct);
        return
            tentativa is not null
            && tentativa.ConsumidaEm is null
            && tentativa.ExpiraEm > DateTime.UtcNow
            && tentativa.NavegadorHash == Hash(navegador)
            && tentativa.Etapa == etapa
            ? tentativa
            : null;
    }

    /// <summary>Credencial aleatória de 256 bits, transportada apenas ao dono do fluxo.</summary>
    public static string NovoToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Hash da credencial, sem material autenticável na persistência.</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
