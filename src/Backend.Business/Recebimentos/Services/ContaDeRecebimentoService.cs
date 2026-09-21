using Backend.Business.Abstractions;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Recebimentos.Services;

/// <summary>
/// A conta de recebimento da turma: os meios que ela aceita, o PIX de teste e a conferência.
/// </summary>
/// <remarks>
/// Trocar para onde vai o dinheiro é o ponto de fraude — quem troca desvia a mensalidade da turma
/// inteira. Por isso qualquer mudança avisa a comissão por e-mail na mesma transação (decisão de
/// 14/09/2026: só a comissão) e deixa na auditoria o antes e o depois — o evento também na mesma
/// transação, e não pela fila de analytics, que descarta quando enche. Mexer no PIX desfaz a
/// conferência; mexer nos outros meios, não, porque não é deles que o PIX de teste fala.
/// <para>
/// Conferir é recomendado, não obrigatório (decisão de 14/09/2026): a tela sugere o PIX de teste, mas
/// conta não conferida não esconde o QR de ninguém.
/// </para>
/// </remarks>
/// <param name="contaRepository">A conta da turma.</param>
/// <param name="vinculoRepository">E-mails da comissão.</param>
/// <param name="perfilRepository">Nome de quem troca.</param>
/// <param name="formaturaRepository">Nome da turma, para o e-mail.</param>
/// <param name="emails">Aviso da troca.</param>
/// <param name="eventos">Auditoria.</param>
/// <param name="validator">Forma dos meios.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ContaDeRecebimentoService(
    IContaDeRecebimentoRepository contaRepository,
    IVinculoRepository vinculoRepository,
    IPerfilRepository perfilRepository,
    IFormaturaRepository formaturaRepository,
    EmailsDeRecebimento emails,
    IEventoRepository eventos,
    IValidator<MeiosDaConta> validator,
    IUnitOfWork unitOfWork,
    ILogger<ContaDeRecebimentoService> logger
) : IContaDeRecebimentoService
{
    /// <summary>Evento dos primeiros meios da turma.</summary>
    public const string EventoDeCadastro = "recebimento.conta_cadastrada";

    /// <summary>Evento da troca, com o antes e o depois — lido pela trilha de auditoria (Sprint 14).</summary>
    public const string EventoDeTroca = "recebimento.conta_alterada";

    /// <summary>R$ 1,00: o bastante para o banco mostrar o titular, pouco o bastante para ninguém hesitar.</summary>
    public const long ValorDoTeste = 100;

    /// <summary>O identificador do PIX de teste no extrato da comissão.</summary>
    public const string IdentificadorDoTeste = "KAPATESTE";

    private static readonly Erro SemConta = Erro.NaoEncontrado("recebimento.sem_conta", "A turma ainda não cadastrou a conta de recebimento.");

    /// <inheritdoc />
    public async Task<Result<ContaDeRecebimentoDaTurma>> Obter(CancellationToken ct = default) =>
        new ContaDeRecebimentoDaTurma(await contaRepository.ObterDetalhe(ct));

    /// <inheritdoc />
    /// <remarks>
    /// O e-mail sai só na troca: na primeira gravação não há o que desviar. Gravar os mesmos dados de
    /// novo é recusado — senão o clique duplo desfaria a conferência e mandaria o aviso à toa.
    /// </remarks>
    public async Task<Result<ContaDeRecebimentoDetalhe>> Gravar(Guid formaturaId, Guid usuarioId, MeiosDaConta meios, CancellationToken ct = default)
    {
        var validacao = validator.Validar(meios);
        if (validacao.Falhou)
            return Result.Falha<ContaDeRecebimentoDetalhe>(validacao.Erros);

        var conta = await contaRepository.ObterParaEdicao(ct);

        if (conta is null)
        {
            conta = new ContaDeRecebimento();
            conta.Aplicar(meios);
            await contaRepository.Adicionar(conta, ct);
            await eventos.Auditar(EventoDeCadastro, usuarioId, new { formaturaId, depois = conta.ParaMeios() }, ct);
            await unitOfWork.SalvarAsync(ct);

            logger.LogInformation(
                "Conta de recebimento {ContaId} cadastrada por {UsuarioId} com {Meios} meios.",
                conta.Id,
                usuarioId,
                conta.ParaMeios().Habilitados.Count
            );

            return Detalhar(conta);
        }

        var antes = conta.ParaMeios();

        if (!conta.Aplicar(meios))
            return Erro.Conflito("recebimento.conta_sem_mudanca", "Estes dados são os mesmos da conta atual.");

        await AvisarComissao(formaturaId, usuarioId, antes, conta.ParaMeios(), ct);
        await eventos.Auditar(
            EventoDeTroca,
            usuarioId,
            new
            {
                formaturaId,
                antes,
                depois = conta.ParaMeios(),
            },
            ct
        );
        await unitOfWork.SalvarAsync(ct);

        logger.LogWarning(
            "Conta de recebimento {ContaId} alterada por {UsuarioId}; segue conferida: {Conferida}.",
            conta.Id,
            usuarioId,
            conta.Conferida
        );

        return Detalhar(conta);
    }

    /// <inheritdoc />
    public async Task<Result<PixDeTeste>> GerarPixDeTeste(CancellationToken ct = default)
    {
        var conta = await contaRepository.ObterDetalhe(ct);
        if (conta is null)
            return SemConta;

        if (conta.Meios.Pix is not { } pix)
            return Erro.Conflito("recebimento.sem_chave_pix", "Esta turma não aceita PIX: não há chave para testar.");

        return new PixDeTeste(BrCode.Montar(pix.Chave, pix.NomeDoTitular, pix.Cidade, ValorDoTeste, IdentificadorDoTeste), ValorDoTeste);
    }

    /// <inheritdoc />
    public async Task<Result<ContaDeRecebimentoDetalhe>> Conferir(Guid usuarioId, CancellationToken ct = default)
    {
        var conta = await contaRepository.ObterParaEdicao(ct);
        if (conta is null)
            return SemConta;

        var conferencia = conta.Conferir(usuarioId, DateTime.UtcNow);
        if (conferencia.Falhou)
            return Result.Falha<ContaDeRecebimentoDetalhe>(conferencia.Erros);

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Conta de recebimento {ContaId} conferida por {UsuarioId}.", conta.Id, usuarioId);

        return (await contaRepository.ObterDetalhe(ct))!;
    }

    /// <summary>Enfileira o aviso para cada membro ativo da comissão — quem trocou inclusive.</summary>
    /// <remarks>Leva o antes e o depois: é a remoção de um meio que denuncia a troca indevida.</remarks>
    /// <param name="formaturaId">Turma, para achar a comissão.</param>
    /// <param name="usuarioId">Quem trocou.</param>
    /// <param name="antes">Os meios como estavam.</param>
    /// <param name="depois">Os meios novos, já gravados.</param>
    /// <param name="ct">Token de cancelamento.</param>
    private async Task AvisarComissao(Guid formaturaId, Guid usuarioId, MeiosDaConta antes, MeiosDaConta depois, CancellationToken ct)
    {
        var formatura = await formaturaRepository.ObterDetalhe(formaturaId, ct);
        var autor = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);

        foreach (var email in await vinculoRepository.ListarEmailsDaComissao(formaturaId, ct))
            await emails.ContaAlterada(email, formatura?.Nome ?? string.Empty, autor?.Nome ?? "O Presidente", antes, depois, ct);
    }

    /// <summary>Conta recém-gravada. Sem o nome de quem conferiu: quem o traz é a consulta com o join.</summary>
    private static ContaDeRecebimentoDetalhe Detalhar(ContaDeRecebimento conta) =>
        new(conta.ParaMeios(), conta.AtualizadoEm, conta.ConferidaEm, null);
}
