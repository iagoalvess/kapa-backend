using Backend.Business.Abstractions;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Loja.Interfaces;
using Backend.Business.MercadoPago.Interfaces;
using Backend.Business.MercadoPago.Settings;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.Recebimentos.Services;

/// <summary>
/// O Mercado Pago da turma: a conexão por OAuth, a desconexão e a renovação (Sprint 25, Parte A).
/// </summary>
/// <remarks>
/// Conectar é trocar para onde vai o dinheiro do PIX automático — o mesmo ponto de fraude da troca de
/// chave. Por isso só o Presidente conecta (P3), a comissão recebe e-mail na mesma transação e a
/// auditoria guarda a conta que entrou. Nunca o token: ele não sai do banco, nem para o log.
/// <para>
/// Desconectar não cancela o que já foi emitido: o PIX dinâmico vence no fim do dia, e quem já tem o QR
/// na mão paga nele. <c>ponytail:</c> sem a credencial, esse resto não é conciliado sozinho — cai na
/// conferência da Sprint 9, que é a rede para o que chega por fora. Guardar credenciais antigas só para
/// fechar a janela de um dia não compensa o segredo a mais em repouso.
/// </para>
/// </remarks>
/// <param name="repositorio">Credencial da turma.</param>
/// <param name="contas">A conta de recebimento — conectar exige a chave PIX (P7).</param>
/// <param name="mercadoPago">A API.</param>
/// <param name="options">A aplicação do Kapa.</param>
/// <param name="escopo">A formatura do processamento — o retorno do OAuth chega sem sessão.</param>
/// <param name="perfilRepository">Quem autoriza, e com que papel.</param>
/// <param name="vinculoRepository">E-mails da comissão.</param>
/// <param name="formaturaRepository">Nome da turma, para o e-mail.</param>
/// <param name="emails">Aviso à comissão.</param>
/// <param name="eventos">Auditoria.</param>
/// <param name="cartaoValidator">Forma da configuração do cartão.</param>
/// <param name="informes">Avisos pendentes — trocar para o automático exige a fila vazia.</param>
/// <param name="loja">Os itens à venda na loja pública — ela só vende pelo Mercado Pago, e desconectar a deixaria sem pagamento.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ProvedorDaTurmaService(
    IProvedorDaTurmaRepository repositorio,
    IContaDeRecebimentoRepository contas,
    IMercadoPago mercadoPago,
    IOptions<MercadoPagoSettings> options,
    FormaturaDoProcessamento escopo,
    IPerfilRepository perfilRepository,
    IVinculoRepository vinculoRepository,
    IFormaturaRepository formaturaRepository,
    EmailsDeRecebimento emails,
    IEventoRepository eventos,
    IValidator<ConfiguracaoDoCartao> cartaoValidator,
    IInformeRepository informes,
    ICompraDeConviteRepository loja,
    IUnitOfWork unitOfWork,
    ILogger<ProvedorDaTurmaService> logger
) : IProvedorDaTurmaService
{
    /// <summary>Evento da conexão — e da troca, que é conectar de novo.</summary>
    public const string EventoDeConexao = "recebimento.provedor_conectado";

    /// <summary>Evento da desconexão.</summary>
    public const string EventoDeDesconexao = "recebimento.provedor_desconectado";

    /// <summary>Evento de ligar, desligar ou mudar a taxa do cartão (Sprint 39).</summary>
    public const string EventoDoCartao = "recebimento.cartao_configurado";

    /// <summary>Evento da troca entre a cobrança manual e a automática (29/09/2026).</summary>
    public const string EventoDoModo = "recebimento.modo_de_cobranca";

    private static readonly Erro NaoConectado = Erro.NaoEncontrado("recebimento.provedor_nao_conectado", "A turma não tem o Mercado Pago conectado.");

    private static readonly Erro RetornoInvalido = Erro.Validacao(
        "recebimento.retorno_invalido",
        "Este link de autorização venceu ou não é válido. Volte ao Kapa e clique em Conectar de novo."
    );

    private readonly MercadoPagoSettings _config = options.Value;

    /// <inheritdoc />
    public async Task<Result<ProvedorDaTurma>> Obter(CancellationToken ct = default) => new ProvedorDaTurma(await repositorio.ObterConexao(ct));

    /// <inheritdoc />
    public async Task<Result<AutorizacaoDoProvedor>> IniciarConexao(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        if (!_config.Ligado)
            return Erro.Conflito("recebimento.provedor_desligado", "A conexão com o Mercado Pago ainda não está disponível.");

        if (await contas.ObterDetalhe(ct) is not { Meios.Pix: not null })
            return Erro.Conflito(
                "recebimento.chave_pix_obrigatoria",
                "Cadastre a chave PIX da turma antes de conectar o Mercado Pago: é por ela que o formando paga se a turma voltar à cobrança manual."
            );

        return new AutorizacaoDoProvedor(
            mercadoPago.UrlDeAutorizacao(EstadoDaConexao.Assinar(formaturaId, usuarioId, DateTime.UtcNow, _config.ClientSecret))
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// O presidente pode ter perdido o papel entre o clique e o retorno — confere de novo. Conta de teste
    /// do Mercado Pago é aceita: é assim que o sandbox se prova, e ela nunca emite cobrança de verdade.
    /// </remarks>
    public async Task<Result<ProvedorConectado>> ConcluirConexao(string? codigo, string? state, CancellationToken ct = default)
    {
        if (!_config.Ligado || string.IsNullOrWhiteSpace(codigo) || EstadoDaConexao.Ler(state, DateTime.UtcNow, _config.ClientSecret) is not { } lido)
            return RetornoInvalido;

        var (formaturaId, usuarioId) = lido;
        escopo.Apontar(formaturaId);

        if (await perfilRepository.ObterMembro(formaturaId, usuarioId, ct) is not { Papel: PapelNaFormatura.Presidente } presidente)
            return Erro.Proibido("recebimento.somente_presidente", "Só o Presidente conecta o Mercado Pago da turma.");

        var tokens = await mercadoPago.Autorizar(codigo, ct);
        if (tokens.Falhou)
            return Result.Falha<ProvedorConectado>(tokens.Erros);

        var conta = await mercadoPago.ConsultarConta(tokens.Valor.AccessToken, ct);
        if (conta.Falhou)
            return Result.Falha<ProvedorConectado>(conta.Erros);

        var credencial = await repositorio.ObterCredencial(ct);

        if (credencial is null)
        {
            credencial = new CredencialDeProvedor();
            await repositorio.AdicionarCredencial(credencial, ct);
        }

        credencial.Conectar(
            tokens.Valor.AccessToken,
            tokens.Valor.RefreshToken,
            tokens.Valor.ExpiraEm,
            conta.Valor.Id,
            conta.Valor.Nome,
            usuarioId,
            tokens.Valor.ChavePublica
        );

        await Avisar(formaturaId, presidente.Nome, conta.Valor.Nome, ct);
        await eventos.Auditar(
            EventoDeConexao,
            usuarioId,
            new
            {
                formaturaId,
                conta = conta.Valor.Nome,
                idNoProvedor = conta.Valor.Id,
                teste = conta.Valor.Teste,
            },
            ct
        );
        await unitOfWork.SalvarAsync(ct);

        logger.LogWarning("Mercado Pago conectado à formatura {FormaturaId} por {UsuarioId}.", formaturaId, usuarioId);

        return new ProvedorConectado(
            conta.Valor.Nome,
            credencial.AtualizadoEm,
            presidente.Nome,
            new CartaoDaTurma(credencial.ChavePublica is not null, credencial.CartaoLigadoEm, null, credencial.TaxaDoCartaoRepassada),
            credencial.CobrancaAutomaticaEm
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ligar é aceitar a taxa e o risco de contestação (P2 e P4) — a tela mostra os dois antes —, e fica na auditoria
    /// com quem ligou. A comissão não recebe e-mail: o dinheiro continua indo para a mesma conta.
    /// </remarks>
    public async Task<Result<ProvedorDaTurma>> ConfigurarCartao(
        Guid formaturaId,
        Guid usuarioId,
        ConfiguracaoDoCartao dados,
        CancellationToken ct = default
    )
    {
        var validacao = cartaoValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ProvedorDaTurma>(validacao.Erros);

        var credencial = await repositorio.ObterCredencial(ct);
        if (credencial is null)
            return NaoConectado;

        if (dados.Ligado && credencial.ChavePublica is null)
            return Erro.Conflito(
                "recebimento.reconectar_para_cartao",
                "Esta conexão é anterior ao cartão. Clique em Trocar de conta e autorize a mesma conta de novo para ligar o cartão."
            );

        if (dados.Ligado)
            credencial.LigarCartao(dados.TaxaRepassada, usuarioId, DateTime.UtcNow);
        else
            credencial.DesligarCartao();

        await eventos.Auditar(
            EventoDoCartao,
            usuarioId,
            new
            {
                formaturaId,
                dados.Ligado,
                dados.TaxaRepassada,
            },
            ct
        );
        await unitOfWork.SalvarAsync(ct);

        logger.LogWarning(
            "Cartão {Situacao} na formatura {FormaturaId} por {UsuarioId}.",
            dados.Ligado ? "ligado" : "desligado",
            formaturaId,
            usuarioId
        );

        return await Obter(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A troca só vale para o que vem depois, e por isso espera o que está no meio do caminho: indo para o automático,
    /// a fila de avisos precisa estar vazia — o aviso que ficasse lá não teria mais quem o fizesse andar pela tela do
    /// formando; voltando ao manual, nenhum PIX do Mercado Pago de parcela pode estar aceitando pagamento — ele vale
    /// até o fim do dia, e o formando que já tem o QR pagaria enquanto a tela oferece a chave da comissão.
    /// </remarks>
    public async Task<Result<ProvedorDaTurma>> ConfigurarCobranca(
        Guid formaturaId,
        Guid usuarioId,
        ModoDeCobranca modo,
        CancellationToken ct = default
    )
    {
        var credencial = await repositorio.ObterCredencial(ct);
        if (credencial is null)
            return NaoConectado;

        if (credencial.CobrancaAutomatica == modo.Automatica)
            return await Obter(ct);

        if (modo.Automatica && await informes.ContarPendentes(ct) is > 0 and var pendentes)
            return Erro.Conflito(
                "recebimento.avisos_pendentes",
                $"Há {pendentes} aviso(s) de pagamento esperando conferência. Confirme ou recuse na Conferência antes de trocar o modo."
            );

        if (!modo.Automatica && await repositorio.ContarPixDeParcelaEmAberto(DateTime.UtcNow, ct) is > 0 and var emAberto)
            return Erro.Conflito(
                "recebimento.pix_em_aberto",
                $"Há {emAberto} PIX do Mercado Pago que formandos ainda podem pagar hoje. Eles vencem à meia-noite: troque o modo amanhã."
            );

        credencial.DefinirCobrancaAutomatica(modo.Automatica, DateTime.UtcNow);

        await eventos.Auditar(EventoDoModo, usuarioId, new { formaturaId, modo.Automatica }, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogWarning(
            "Cobrança {Modo} na formatura {FormaturaId} por {UsuarioId}.",
            modo.Automatica ? "automática" : "manual",
            formaturaId,
            usuarioId
        );

        return await Obter(ct);
    }

    /// <inheritdoc />
    public async Task<Result> Desconectar(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var credencial = await repositorio.ObterCredencial(ct);
        if (credencial is null)
            return Result.Falha(NaoConectado);

        if (credencial.CobrancaAutomatica)
            return Result.Falha(
                Erro.Conflito(
                    "recebimento.cobranca_automatica_ligada",
                    "A turma cobra pelo Mercado Pago. Troque para a cobrança manual antes de desconectar, senão os formandos ficam sem ter como pagar."
                )
            );

        if (await loja.ListarItensDaLoja(ct) is { Count: > 0 } aVenda)
            return Result.Falha(
                Erro.Conflito(
                    "recebimento.loja_aberta",
                    $"A loja pública tem {aVenda.Count} item(ns) à venda, e ela só vende pelo Mercado Pago. Encerre as vendas da loja antes de desconectar."
                )
            );

        repositorio.RemoverCredencial(credencial);

        var autor = (await perfilRepository.ObterMembro(formaturaId, usuarioId, ct))?.Nome ?? "O Presidente";
        await Avisar(formaturaId, autor, null, ct);
        await eventos.Auditar(EventoDeDesconexao, usuarioId, new { formaturaId, conta = credencial.ContaNoProvedor }, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogWarning("Mercado Pago desconectado da formatura {FormaturaId} por {UsuarioId}.", formaturaId, usuarioId);

        return Result.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Renovação recusada — o dono revogou o acesso no Mercado Pago — não apaga a credencial: o PIX
    /// dinâmico falha até o presidente conectar de novo — na cobrança manual a tela nem o usa; na automática ela pede
    /// para tentar depois, e a tesouraria pode voltar ao manual —, e o log conta por quê.
    /// </remarks>
    public async Task<Result> Renovar(CancellationToken ct = default)
    {
        var credencial = await repositorio.ObterCredencial(ct);
        if (credencial is null)
            return Result.Ok();

        var tokens = await mercadoPago.Renovar(credencial.RefreshToken, ct);
        if (tokens.Falhou)
            return Result.Falha(tokens.Erros);

        credencial.Renovar(tokens.Valor.AccessToken, tokens.Valor.RefreshToken, tokens.Valor.ExpiraEm);
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <summary>E-mail para cada membro ativo da comissão, na mesma transação.</summary>
    private async Task Avisar(Guid formaturaId, string autor, string? conta, CancellationToken ct)
    {
        var nome = (await formaturaRepository.ObterDetalheDeTodasAsFormaturas(formaturaId, ct))?.Nome ?? string.Empty;

        foreach (var email in await vinculoRepository.ListarEmailsDaComissao(formaturaId, ct))
            await emails.ProvedorAlterado(email, nome, autor, conta, ct);
    }
}
