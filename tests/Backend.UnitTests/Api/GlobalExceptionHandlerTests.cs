using System.Text.Json;
using Backend.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Api;

/// <summary>
/// O stack trace de Development não pode sair em <c>detail</c>: o front exibe esse campo, e a
/// exceção do banco acabava na tela do usuário.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public async Task Excecao_nao_tratada_nunca_vai_para_detail(string ambiente, bool expoeExcecao)
    {
        // Arrange
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(ambiente);
        var contexto = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var excecao = new DbUpdateException("falha ao gravar", new PostgresException("coluna não existe", "ERROR", "ERROR", "42703"));

        // Act
        await new GlobalExceptionHandler(host, NullLogger<GlobalExceptionHandler>.Instance).TryHandleAsync(
            contexto,
            excecao,
            TestContext.Current.CancellationToken
        );

        // Assert
        contexto.Response.Body.Position = 0;
        using var corpo = await JsonDocument.ParseAsync(contexto.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        corpo.RootElement.TryGetProperty("detail", out _).ShouldBeFalse();
        corpo.RootElement.GetProperty("title").GetString()!.ShouldNotContain("42703");
        corpo.RootElement.TryGetProperty("excecao", out _).ShouldBe(expoeExcecao);
    }
}
