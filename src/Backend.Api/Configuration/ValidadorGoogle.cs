using Backend.Business.Abstractions;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Auth.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Api.Configuration;

/// <summary>Validação JWT com discovery oficial e cache de chaves rotativas do Google.</summary>
public sealed class ValidadorGoogle(IConfigurationManager<OpenIdConnectConfiguration> configuracao, IOptions<GoogleSettings> options)
    : IValidadorGoogle
{
    /// <inheritdoc />
    public async Task<Result<IdentidadeGoogle>> Validar(string credencial, string nonce, CancellationToken ct = default)
    {
        var invalido = Erro.NaoAutenticado("auth.google_credencial_invalida", "Não foi possível validar sua entrada com Google. Tente novamente.");
        if (string.IsNullOrWhiteSpace(credencial) || credencial.Length > 16384)
            return invalido;
        try
        {
            var config = await configuracao.GetConfigurationAsync(ct);
            var handler = new JsonWebTokenHandler { MapInboundClaims = false };
            var parametros = new TokenValidationParameters
            {
                ValidIssuers = ["accounts.google.com", "https://accounts.google.com"],
                ValidAudience = options.Value.ClientId,
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuerSigningKey = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.Zero,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            };
            var resultado = await handler.ValidateTokenAsync(credencial, parametros);
            if (!resultado.IsValid && resultado.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                configuracao.RequestRefresh();
                parametros.IssuerSigningKeys = (await configuracao.GetConfigurationAsync(ct)).SigningKeys;
                resultado = await handler.ValidateTokenAsync(credencial, parametros);
            }
            if (!resultado.IsValid)
                return invalido;
            var claims = resultado.ClaimsIdentity;
            var sub = claims.FindFirst("sub")?.Value;
            var email = claims.FindFirst("email")?.Value;
            if (
                string.IsNullOrWhiteSpace(sub)
                || sub.Length > 255
                || string.IsNullOrWhiteSpace(email)
                || email.Length > 256
                || !email.Contains('@')
                || claims.FindFirst("nonce")?.Value != nonce
            )
                return invalido;
            if (!bool.TryParse(claims.FindFirst("email_verified")?.Value, out var verificado) || !verificado)
                return Erro.Proibido("auth.google_email_nao_verificado", "Confirme seu e-mail no Google antes de continuar.");
            var nome = claims.FindFirst("name")?.Value ?? string.Empty;
            if (nome.Length > 120)
                nome = nome[..120];
            return new IdentidadeGoogle(
                sub,
                email,
                nome,
                email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(claims.FindFirst("hd")?.Value)
            );
        }
        catch (Exception erro) when (erro is HttpRequestException or IOException or InvalidOperationException)
        {
            return Erro.Indisponivel("auth.google_indisponivel", "O Google não está disponível agora. Tente novamente ou entre com e-mail.");
        }
    }
}
