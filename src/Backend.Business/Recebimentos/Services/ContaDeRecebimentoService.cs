using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Services;
using Backend.Business.Common;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
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
/// inteira. Por isso qualquer mudança avisa a comissão por e-mail na mesma transação, a troca do PIX
/// ou da conta de transferência avisa também a turma inteira (Sprint 22, P1, que revisou a decisão de
/// 14/09/2026 de avisar só a comissão), e tudo deixa na auditoria o antes e o depois — o evento também na mesma
/// transação, e não pela fila de analytics, que descarta quando enche. Mexer no PIX desfaz a
/// conferência; mexer nos outros meios, não, porque não é deles que o PIX de teste fala.
/// <para>
/// Conferir é recomendado, não obrigatório (decisão de 14/09/2026): a tela sugere o PIX de teste, mas
/// conta não conferida não esconde o QR de ninguém.
/// </para>
/// </remarks>
/// <param name="contaRepository">A conta da turma.</param>
/// <param name="provedor">Se o Mercado Pago está conectado — com ele, a chave PIX não sai (Sprint 25, P7).</param>
/// <param name="vinculoRepository">E-mails da comissão.</param>
/// <param name="perfilRepository">Nome de quem troca.</param>
/// <param name="formaturaRepository">Nome da turma, para o e-mail.</param>
/// <param name="emails">Aviso da troca.</param>
/// <param name="eventos">Auditoria.</param>
/// <param name="validator">Forma dos meios.</param>
/// <param name="confirmacao">O link da confirmação por e-mail.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ContaDeRecebimentoService(
    IProvedorDaTurmaRepository provedor,
    IContaDeRecebimentoRepository contaRepository,
    IVinculoRepository vinculoRepository,
    IPerfilRepository perfilRepository,
    IFormaturaRepository formaturaRepository,
    EmailsDeRecebimento emails,
    IEventoRepository eventos,
    IValidator<MeiosDaConta> validator,
    ConfirmacaoPorEmail confirmacao,
    IUnitOfWork unitOfWork,
    ILogger<ContaDeRecebimentoService> logger
) : IContaDeRecebimentoService
{
    /// <summary>Evento dos primeiros meios da turma.</summary>
    public const string EventoDeCadastro = "recebimento.conta_cadastrada";

    /// <summary>Evento da troca, com o antes e o depois — lido pela trilha de auditoria (Sprint 14).</summary>
    public const string EventoDeTroca = "recebimento.conta_alterada";

    /// <summary>Evento do pedido de troca que espera o link do e-mail — fica na trilha mesmo se nunca for confirmado.</summary>
    public const string EventoDeTrocaPedida = NomesDeAuditoria.TrocaDaContaPedida;

    /// <summary>A finalidade do link de confirmação da troca.</summary>
    public const string FinalidadeDaTroca = "recebimento.troca";

    /// <summary>
    /// Os meios que um evento de cadastro ou de troca deixou gravados no <c>depois</c>.
    /// </summary>
    /// <remarks>
    /// Dois formatos convivem na trilha: até a Sprint 18 (21/09/2026) a conta era só a chave PIX, e o
    /// <c>depois</c> é a chave solta; dali em diante é o envelope com os três meios. Registro passado
    /// não se reescreve, então quem lê entende os dois — sem isso, o recibo de um pagamento anterior à
    /// Sprint 18 não nomearia quem recebeu.
    /// </remarks>
    /// <param name="dados">Corpo do evento, como está na coluna.</param>
    /// <returns>Os meios, ou nulo se o corpo não tiver nenhum.</returns>
    public static MeiosDaConta? MeiosGravados(string dados)
    {
        if (Auditoria.Ler<MeiosDaConta>(dados, "depois") is { Habilitados.Count: > 0 } meios)
            return meios;

        return Auditoria.Ler<ChavePixDaConta>(dados, "depois") is { Chave: not null } chave ? new MeiosDaConta(chave, null, null) : null;
    }

    /// <summary>R$ 1,00: o bastante para o banco mostrar o titular, pouco o bastante para ninguém hesitar.</summary>
    public const long ValorDoTeste = 100;

    /// <summary>O identificador do PIX de teste no extrato da comissão.</summary>
    public const string IdentificadorDoTeste = "KAPATESTE";

    /// <summary>
    /// Com o Mercado Pago conectado, a chave PIX é o chão (Sprint 25, P7): é por ela que o formando paga quando
    /// o provedor não responde, e a conferência da Sprint 9 continua recebendo o que chega por fora.
    /// </summary>
    public static readonly Erro ChavePixComProvedor = Erro.Conflito(
        "recebimento.chave_pix_obrigatoria",
        "Com o Mercado Pago conectado, a chave PIX continua na conta: é por ela que o formando paga quando o Mercado Pago não responde."
    );

    private static readonly Erro SemMudanca = Erro.Conflito("recebimento.conta_sem_mudanca", "Estes dados são os mesmos da conta atual.");

    private static readonly Erro LinkInvalido = Erro.Validacao(
        "recebimento.confirmacao_invalida",
        "Este link de confirmação venceu, já foi usado ou é de outra pessoa. Peça a troca de novo na tela da conta."
    );

    private static readonly Erro SemConta = Erro.NaoEncontrado("recebimento.sem_conta", "A turma ainda não cadastrou a conta de recebimento.");

    /// <inheritdoc />
    public async Task<Result<ContaDeRecebimentoDaTurma>> Obter(CancellationToken ct = default) =>
        new ContaDeRecebimentoDaTurma(await contaRepository.ObterDetalhe(ct));

    /// <inheritdoc />
    /// <remarks>
    /// Mudar o PIX ou a conta de transferência — o primeiro cadastro inclusive — não vale na hora: o link vai ao
    /// e-mail de quem pediu, e só ele, com a sessão aberta, confirma (<see cref="Confirmar"/>). Sem isso, a senha
    /// ou o cookie roubados do presidente bastavam para a turma inteira pagar na conta de outro no mesmo segundo
    /// (revisão de segurança de 05/10/2026). Mexer só no dinheiro vale na hora, pelo mesmo motivo de não avisar a
    /// turma: não muda para onde vai nada.
    /// <para>
    /// Gravar os mesmos dados de novo é recusado — senão o clique duplo desfaria a conferência e mandaria o aviso à toa.
    /// </para>
    /// </remarks>
    public async Task<Result<GravacaoDaConta>> Gravar(Guid formaturaId, Guid usuarioId, MeiosDaConta meios, CancellationToken ct = default)
    {
        var validacao = validator.Validar(meios);
        if (validacao.Falhou)
            return Result.Falha<GravacaoDaConta>(validacao.Erros);

        if (meios.Pix is null && await provedor.ObterCredencial(ct) is not null)
            return ChavePixComProvedor;

        var conta = await contaRepository.ObterParaEdicao(ct);
        var antes = conta?.ParaMeios();
        var depois = ContaDeRecebimento.Normalizar(meios);

        if (antes == depois)
            return SemMudanca;

        if (antes?.Pix == depois.Pix && antes?.Transferencia == depois.Transferencia)
            return (await Aplicar(formaturaId, usuarioId, conta, depois, ct)).Map(detalhe => new GravacaoDaConta(detalhe, null));

        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        if (membro is null)
            return ErrosDeFormatura.MembroNaoEncontrado;

        var token = confirmacao.Assinar(FinalidadeDaTroca, new TrocaDosMeios(formaturaId, usuarioId, Estado(conta), depois), DateTime.UtcNow);
        var turma = (await formaturaRepository.ObterDetalheDeTodasAsFormaturas(formaturaId, ct))?.Nome ?? string.Empty;

        await emails.ConfirmarTroca(membro.Email, turma, antes, depois, token, ct);
        await eventos.Auditar(
            EventoDeTrocaPedida,
            usuarioId,
            new
            {
                formaturaId,
                antes,
                depois,
            },
            ct
        );
        await unitOfWork.SalvarAsync(ct);

        logger.LogWarning("Troca da conta de recebimento pedida por {UsuarioId}; espera a confirmação por e-mail.", usuarioId);

        return new GravacaoDaConta(conta is null ? null : await contaRepository.ObterDetalhe(ct), AdesaoService.MascararEmail(membro.Email));
    }

    /// <inheritdoc />
    /// <remarks>
    /// O link só vale para quem pediu, na turma em que pediu, e enquanto a conta estiver como estava no pedido —
    /// aplicado uma vez, o "antes" mudou e o mesmo link morre. A forma é conferida de novo: o Mercado Pago pode ter
    /// sido conectado no meio do caminho.
    /// </remarks>
    public async Task<Result<ContaDeRecebimentoDetalhe>> Confirmar(Guid formaturaId, Guid usuarioId, string? token, CancellationToken ct = default)
    {
        if (
            confirmacao.Ler<TrocaDosMeios>(FinalidadeDaTroca, token, DateTime.UtcNow) is not { } troca
            || troca.FormaturaId != formaturaId
            || troca.UsuarioId != usuarioId
        )
            return LinkInvalido;

        var conta = await contaRepository.ObterParaEdicao(ct);
        if (Estado(conta) != troca.Antes)
            return LinkInvalido;

        var validacao = validator.Validar(troca.Depois);
        if (validacao.Falhou)
            return Result.Falha<ContaDeRecebimentoDetalhe>(validacao.Erros);

        if (troca.Depois.Pix is null && await provedor.ObterCredencial(ct) is not null)
            return ChavePixComProvedor;

        return await Aplicar(formaturaId, usuarioId, conta, troca.Depois, ct);
    }

    /// <summary>
    /// A impressão digital da conta para o link de confirmação: os meios e a última gravação.
    /// </summary>
    /// <remarks>
    /// Só os meios deixariam o link reviver na volta ao estado do pedido (A, B, A de novo); a data da gravação muda a
    /// cada <c>SalvarAsync</c> e não volta. O custo: conferir a chave entre o pedido e o clique também mata o link.
    /// </remarks>
    private static string Estado(ContaDeRecebimento? conta) => ConfirmacaoPorEmail.Impressao(new { Meios = conta?.ParaMeios(), conta?.AtualizadoEm });

    /// <summary>
    /// Grava os meios — cadastro ou troca —, avisa e audita na mesma transação.
    /// </summary>
    /// <remarks>
    /// O e-mail à comissão e à turma sai só na troca: no primeiro cadastro não havia para onde o dinheiro ia antes.
    /// </remarks>
    private async Task<Result<ContaDeRecebimentoDetalhe>> Aplicar(
        Guid formaturaId,
        Guid usuarioId,
        ContaDeRecebimento? conta,
        MeiosDaConta depois,
        CancellationToken ct
    )
    {
        if (conta is null)
        {
            conta = new ContaDeRecebimento();
            conta.Aplicar(depois);
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

        if (!conta.Aplicar(depois))
            return SemMudanca;

        await Avisar(formaturaId, usuarioId, antes, conta.ParaMeios(), ct);
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

    /// <summary>
    /// Enfileira o aviso para cada membro ativo da comissão — quem trocou inclusive — e, se mudou para
    /// onde o dinheiro vai, para cada formando ativo.
    /// </summary>
    /// <remarks>
    /// A comissão recebe o antes e o depois: é a remoção de um meio que denuncia a troca indevida. A turma
    /// recebe só o aviso, sem dado de pagamento (Sprint 22, P1), e só quando o PIX ou a conta de
    /// transferência mudam — trocar onde encontrar quem recebe em dinheiro não desvia nada, e 80 e-mails
    /// por isso seriam o alarme que a Sprint 8 quis evitar.
    /// </remarks>
    /// <param name="formaturaId">Turma, para achar a comissão.</param>
    /// <param name="usuarioId">Quem trocou.</param>
    /// <param name="antes">Os meios como estavam.</param>
    /// <param name="depois">Os meios novos, já gravados.</param>
    /// <param name="ct">Token de cancelamento.</param>
    private async Task Avisar(Guid formaturaId, Guid usuarioId, MeiosDaConta antes, MeiosDaConta depois, CancellationToken ct)
    {
        var formatura = await formaturaRepository.ObterDetalheDeTodasAsFormaturas(formaturaId, ct);
        var nome = formatura?.Nome ?? string.Empty;
        var autor = (await perfilRepository.ObterMembro(formaturaId, usuarioId, ct))?.Nome ?? "O Presidente";

        foreach (var email in await vinculoRepository.ListarEmailsDaComissao(formaturaId, ct))
            await emails.ContaAlterada(email, nome, autor, antes, depois, ct);

        if (antes.Pix == depois.Pix && antes.Transferencia == depois.Transferencia)
            return;

        foreach (var email in await vinculoRepository.ListarEmailsDosFormandos(formaturaId, ct))
            await emails.ContaAlteradaParaATurma(email, nome, autor, ct);
    }

    /// <summary>Conta recém-gravada. Sem o nome de quem conferiu: quem o traz é a consulta com o join.</summary>
    private static ContaDeRecebimentoDetalhe Detalhar(ContaDeRecebimento conta) =>
        new(conta.ParaMeios(), conta.AtualizadoEm, conta.ConferidaEm, null);
}
