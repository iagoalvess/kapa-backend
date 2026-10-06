using System.Security.Claims;
using Backend.Api.Middleware;
using Backend.Business.Auth.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace Backend.UnitTests.Api;

/// <summary>
/// O escopo de log precisa carregar a identidade da sessão para o agregador filtrar por usuário ou
/// por formatura sem depender de cada chamador.
/// </summary>
public sealed class EscopoDeLogTests
{
    [Fact]
    public async Task Escopo_carrega_usuario_e_formatura_das_claims()
    {
        // Arrange
        var usuarioId = Guid.CreateVersion7();
        var formaturaId = Guid.CreateVersion7();
        var contexto = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(JwtRegisteredClaimNames.Sub, usuarioId.ToString()), new Claim(TokenService.ClaimDeFormatura, formaturaId.ToString())],
                    "teste"
                )
            ),
        };
        var logger = new LoggerDeEscopo();
        var seguiu = false;
        RequestDelegate proximo = _ =>
        {
            seguiu = true;
            return Task.CompletedTask;
        };

        // Act
        await new EscopoDeLog(proximo).InvokeAsync(contexto, logger);

        // Assert
        seguiu.ShouldBeTrue();
        var escopo = logger.Escopos.ShouldHaveSingleItem();
        escopo[EscopoDeLog.ChaveDeUsuario].ShouldBe(usuarioId.ToString());
        escopo[EscopoDeLog.ChaveDeFormatura].ShouldBe(formaturaId.ToString());
        escopo.ShouldNotContainKey("TraceId");
    }

    [Fact]
    public async Task Requisicao_anonima_nao_carrega_identidade()
    {
        // Arrange
        var logger = new LoggerDeEscopo();

        var seguiu = false;

        // Act
        await new EscopoDeLog(_ =>
        {
            seguiu = true;
            return Task.CompletedTask;
        }).InvokeAsync(new DefaultHttpContext(), logger);

        // Assert
        seguiu.ShouldBeTrue();
        logger.Escopos.ShouldBeEmpty();
    }

    private sealed class LoggerDeEscopo : ILogger<EscopoDeLog>
    {
        public List<Dictionary<string, object?>> Escopos { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> campos)
                Escopos.Add(campos.ToDictionary(campo => campo.Key, campo => campo.Value));

            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) { }
    }
}
