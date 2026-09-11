using Backend.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace Backend.UnitTests.Api;

/// <summary>
/// Trava o mapeamento de exceção para status HTTP.
/// </summary>
/// <remarks>
/// O caso que motiva este teste é a corrida: o service confere "já existe?" antes de gravar, mas
/// entre a consulta e o <c>INSERT</c> outra requisição pode ter gravado. Quem recusa então é a
/// restrição do banco — e sem tradução o usuário recebe "erro inesperado" para um conflito
/// perfeitamente compreensível.
/// </remarks>
public sealed class ClassificadorDeExcecaoTests
{
    private static PostgresException Postgres(string sqlState) => new("erro", "ERROR", "ERROR", sqlState);

    private static DbUpdateException Gravacao(string sqlState) => new("falha ao gravar", Postgres(sqlState));

    [Fact]
    public void Violacao_de_unicidade_vira_409_e_nao_500()
    {
        var (status, titulo) = ClassificadorDeExcecao.Classificar(Gravacao(PostgresErrorCodes.UniqueViolation));

        status.ShouldBe(StatusCodes.Status409Conflict);
        titulo.ShouldContain("Já existe");
    }

    [Fact]
    public void Violacao_de_chave_estrangeira_vira_409()
    {
        ClassificadorDeExcecao.Classificar(Gravacao(PostgresErrorCodes.ForeignKeyViolation)).Status.ShouldBe(StatusCodes.Status409Conflict);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public void Conflito_de_concorrencia_no_banco_vira_409(string sqlState)
    {
        ClassificadorDeExcecao.Classificar(Gravacao(sqlState)).Status.ShouldBe(StatusCodes.Status409Conflict);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.NotNullViolation)]
    [InlineData(PostgresErrorCodes.CheckViolation)]
    [InlineData(PostgresErrorCodes.StringDataRightTruncation)]
    [InlineData(PostgresErrorCodes.InvalidTextRepresentation)]
    public void Dado_recusado_pelo_esquema_vira_400(string sqlState)
    {
        ClassificadorDeExcecao.Classificar(Gravacao(sqlState)).Status.ShouldBe(StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// <c>DbUpdateConcurrencyException</c> é subtipo de <c>DbUpdateException</c>: se a ordem dos
    /// casos inverter, este teste falha.
    /// </summary>
    [Fact]
    public void Concorrencia_otimista_vira_409()
    {
        var (status, titulo) = ClassificadorDeExcecao.Classificar(new DbUpdateConcurrencyException("alterado por outro"));

        status.ShouldBe(StatusCodes.Status409Conflict);
        titulo.ShouldContain("alterados por outra operação");
    }

    [Fact]
    public void Excecao_do_Postgres_sem_embrulho_tambem_e_classificada()
    {
        ClassificadorDeExcecao.Classificar(Postgres(PostgresErrorCodes.UniqueViolation)).Status.ShouldBe(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void Codigo_do_Postgres_desconhecido_e_tratado_como_indisponibilidade()
    {
        ClassificadorDeExcecao.Classificar(Gravacao("XX000")).Status.ShouldBe(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public void Banco_fora_do_ar_vira_503()
    {
        ClassificadorDeExcecao.Classificar(new NpgsqlException("conexão recusada")).Status.ShouldBe(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public void Falha_de_servico_externo_vira_502()
    {
        ClassificadorDeExcecao.Classificar(new HttpRequestException("recusado")).Status.ShouldBe(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public void Requisicao_ilegivel_vira_400()
    {
        ClassificadorDeExcecao.Classificar(new BadHttpRequestException("corpo inválido")).Status.ShouldBe(StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Bug de programação não vira 400 nem 409: vira 500, que é o que sinaliza "isto é nosso".
    /// </summary>
    [Fact]
    public void Excecao_desconhecida_continua_500()
    {
        var (status, titulo) = ClassificadorDeExcecao.Classificar(new InvalidOperationException("bug"));

        status.ShouldBe(StatusCodes.Status500InternalServerError);
        titulo.ShouldBe("Ocorreu um erro inesperado.");
    }

    /// <summary>
    /// O nome da restrição violada descreve o esquema do banco, e esquema de banco não é
    /// informação para quem está do outro lado de uma API pública.
    /// </summary>
    [Fact]
    public void A_mensagem_nao_vaza_detalhe_do_esquema()
    {
        var interna = new PostgresException("duplicate key value violates unique constraint \"ix_usuarios_email\"", "ERROR", "ERROR", "23505");

        var (_, titulo) = ClassificadorDeExcecao.Classificar(new DbUpdateException("falhou", interna));

        titulo.ShouldNotContain("ix_usuarios");
        titulo.ShouldNotContain("constraint");
    }
}
