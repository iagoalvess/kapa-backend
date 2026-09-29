using Backend.Business.Abstractions;
using Backend.Business.Common.Texto;
using Backend.Business.Legal.Models;
using Backend.Business.Marketing.Interfaces;
using Backend.Business.Marketing.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Marketing.Services;

/// <summary>
/// A preferência "Receber novidades do Kapa": ligar, desligar e sair pelo link do e-mail.
/// </summary>
/// <param name="repositorio">Conta e histórico.</param>
/// <param name="link">Conferência do token do descadastro.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ComunicacaoDoKapaService(
    IComunicacaoDoKapaRepository repositorio,
    LinkDeDescadastro link,
    IUnitOfWork unitOfWork,
    ILogger<ComunicacaoDoKapaService> logger
) : IComunicacaoDoKapaService
{
    /// <summary>Tamanho da coluna de User-Agent; o cabeçalho é escrito pelo cliente e não tem teto.</summary>
    private const int TamanhoMaximoDoUserAgent = 512;

    /// <inheritdoc />
    public async Task<Result> DefinirPreferencia(Guid usuarioId, bool receber, string origem, OrigemDoAceite de, CancellationToken ct = default)
    {
        var usuario = await repositorio.ObterUsuario(usuarioId, ct);

        if (usuario is null)
            return Result.Falha(ErrosDeUsuario.UsuarioNaoEncontrado);

        if (usuario.ReceberComunicacaoDoKapa == receber)
            return Result.Ok();

        usuario.ReceberComunicacaoDoKapa = receber;

        await repositorio.Adicionar(
            new ConsentimentoDeMarketing
            {
                UsuarioId = usuarioId,
                Aceito = receber,
                Origem = origem,
                VersaoDoTexto = TextoDoConsentimentoDeMarketing.Versao,
                RegistradoEm = DateTime.UtcNow,
                EnderecoIp = de.EnderecoIp ?? string.Empty,
                UserAgent = TextoUtils.Truncar(de.UserAgent, TamanhoMaximoDoUserAgent) ?? string.Empty,
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result> Descadastrar(string? token, OrigemDoAceite de, CancellationToken ct = default)
    {
        if (link.Conferir(token, DateTime.UtcNow) is not { } usuarioId)
        {
            logger.LogInformation("Descadastro com token inválido ou vencido: nada mudou.");
            return Result.Ok();
        }

        var resultado = await DefinirPreferencia(usuarioId, receber: false, OrigemDoConsentimentoDeMarketing.DescadastroPeloEmail, de, ct);

        if (resultado.Falhou)
            logger.LogWarning("Descadastro com token válido de uma conta que não existe mais: {UsuarioId}.", usuarioId);

        return Result.Ok();
    }
}
