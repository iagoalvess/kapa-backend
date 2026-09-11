using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Api.Middleware;

/// <summary>
/// Traduz uma exceção não tratada em status HTTP e mensagem para o usuário.
/// </summary>
/// <remarks>
/// É a rede de segurança para o que a camada de negócio não previu. Falha de negócio esperada
/// vem como <c>Result</c> e nunca chega aqui.
/// <para>
/// O caso que mais importa é a **corrida**: o service confere "já existe um usuário com este
/// e-mail?" antes de gravar, mas entre a consulta e o <c>INSERT</c> outra requisição pode ter
/// gravado. Aí quem recusa é a restrição do banco, e sem esta tradução o usuário recebe
/// "erro inesperado" para um conflito perfeitamente compreensível.
/// </para>
/// <para>
/// As mensagens são genéricas de propósito. O nome da restrição violada descreve o esquema do
/// banco, e esquema de banco não é informação para quem está do outro lado de uma API pública.
/// O detalhe técnico vai para o log.
/// </para>
/// </remarks>
public static class ClassificadorDeExcecao
{
    private const string Indisponivel = "O serviço está temporariamente indisponível. Tente novamente em instantes.";

    private const string Concorrencia = "Estes dados foram alterados por outra operação. Recarregue e tente novamente.";

    /// <summary>Classifica a exceção.</summary>
    /// <remarks>
    /// A ordem dos casos importa: <c>DbUpdateConcurrencyException</c> é subtipo de
    /// <c>DbUpdateException</c> e <c>PostgresException</c> é subtipo de <c>NpgsqlException</c> —
    /// o mais específico precisa vir primeiro, ou nunca é alcançado.
    /// </remarks>
    /// <param name="exception">Exceção capturada.</param>
    /// <returns>Status HTTP e título a devolver.</returns>
    public static (int Status, string Titulo) Classificar(Exception exception) =>
        exception switch
        {
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, Concorrencia),

            DbUpdateException atualizacao when atualizacao.InnerException is PostgresException postgres => ClassificarPostgres(postgres),

            PostgresException postgres => ClassificarPostgres(postgres),

            NpgsqlException or TimeoutException => (StatusCodes.Status503ServiceUnavailable, Indisponivel),

            HttpRequestException => (StatusCodes.Status502BadGateway, "Um serviço externo respondeu de forma inesperada."),

            BadHttpRequestException => (StatusCodes.Status400BadRequest, "A requisição não pôde ser lida."),

            _ => (StatusCodes.Status500InternalServerError, "Ocorreu um erro inesperado."),
        };

    /// <summary>
    /// Classifica pelo <c>SQLSTATE</c>, o código padronizado do PostgreSQL.
    /// </summary>
    /// <remarks>
    /// Pelo código, e não pela mensagem: a mensagem muda com a versão do servidor e com o idioma
    /// configurado nele, o <c>SQLSTATE</c> não.
    /// </remarks>
    private static (int Status, string Titulo) ClassificarPostgres(PostgresException postgres) =>
        postgres.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation => (StatusCodes.Status409Conflict, "Já existe um registro com estes dados."),

            PostgresErrorCodes.ForeignKeyViolation => (
                StatusCodes.Status409Conflict,
                "Este registro está vinculado a outro e não pode ser alterado ou removido."
            ),

            PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure => (StatusCodes.Status409Conflict, Concorrencia),

            PostgresErrorCodes.NotNullViolation
            or PostgresErrorCodes.CheckViolation
            or PostgresErrorCodes.StringDataRightTruncation
            or PostgresErrorCodes.InvalidTextRepresentation => (StatusCodes.Status400BadRequest, "Os dados enviados são inválidos."),

            _ => (StatusCodes.Status503ServiceUnavailable, Indisponivel),
        };
}
