using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Privacidade.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Privacidade.Services;

/// <summary>
/// O portal do titular: ver, exportar e pedir eliminação.
/// </summary>
/// <remarks>
/// Nenhum método daqui recebe <c>formaturaId</c>, e nenhum consulta dado de terceiro: o recorte é
/// sempre o <c>usuarioId</c> autenticado, atravessando as turmas dele (decisão 2 da Sprint 14).
/// <para>
/// O trabalho pesado não acontece aqui. Pedir exportação grava uma linha e volta; pedir eliminação
/// grava uma linha, avisa quem precisa ser avisado e volta. Quem gera o pacote e quem anonimiza é o
/// worker, por <see cref="ProcessamentoDePrivacidadeService"/> — juntar o dado de todas as features
/// numa requisição HTTP é exatamente o que a sprint proíbe.
/// </para>
/// </remarks>
/// <param name="privacidadeRepository">Dados do titular e a fila de solicitações.</param>
/// <param name="userManager">Confere a senha antes de aceitar uma eliminação.</param>
/// <param name="arquivoService">Onde o pacote da exportação mora.</param>
/// <param name="emails">Avisos ao titular e à comissão.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class PrivacidadeService(
    IPrivacidadeRepository privacidadeRepository,
    UserManager<Usuario> userManager,
    IArquivoService arquivoService,
    EmailsDePrivacidade emails,
    IUnitOfWork unitOfWork,
    ILogger<PrivacidadeService> logger
) : IPrivacidadeService
{
    private static readonly Erro NaoEncontrada = Erro.NaoEncontrado("privacidade.solicitacao_nao_encontrada", "Solicitação não encontrada.");

    /// <inheritdoc />
    public async Task<Result<MeusDados>> MeusDados(Guid usuarioId, CancellationToken ct = default)
    {
        var dados = await privacidadeRepository.ObterMeusDadosDeTodasAsFormaturas(usuarioId, ct);

        return dados is null ? Erro.NaoEncontrado("privacidade.titular_nao_encontrado", "Titular não encontrado.") : Result.Ok(dados);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pedido igual já pendente devolve o mesmo, sem gravar nada e sem mandar e-mail de novo. Sem
    /// isso, dois cliques no botão de eliminação viram dois avisos ao Presidente da turma — e o
    /// segundo é o que faz a comissão achar que o sistema perdeu o primeiro.
    /// </remarks>
    public async Task<Result<SolicitacaoResumo>> Solicitar(TipoDeSolicitacao tipo, Guid usuarioId, string? senha, CancellationToken ct = default)
    {
        if (tipo == TipoDeSolicitacao.Exclusao)
        {
            var senhaConferida = await ConferirSenha(usuarioId, senha);

            if (senhaConferida.Falhou)
                return Result.Falha<SolicitacaoResumo>(senhaConferida.Erros);
        }

        if (await privacidadeRepository.ObterPendente(tipo, usuarioId, ct) is { } jaPedida)
            return Resumir(jaPedida);

        var agora = DateTime.UtcNow;
        var solicitacao = SolicitacaoDePrivacidade.Nova(tipo, usuarioId, agora);

        await privacidadeRepository.Adicionar(solicitacao, ct);

        if (tipo == TipoDeSolicitacao.Exclusao)
            await AvisarDaExclusao(usuarioId, solicitacao.PrazoEm, ct);

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Solicitação de {Tipo} {SolicitacaoId} aberta pelo titular {UsuarioId}.", tipo, solicitacao.Id, usuarioId);

        return Resumir(solicitacao);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SolicitacaoResumo>>> ListarSolicitacoes(Guid usuarioId, CancellationToken ct = default) =>
        Result.Ok<IReadOnlyList<SolicitacaoResumo>>([.. (await privacidadeRepository.ListarDoTitular(usuarioId, ct)).Select(Resumir)]);

    /// <inheritdoc />
    public async Task<Result<SolicitacaoResumo>> Confirmar(Guid id, Guid usuarioId, CancellationToken ct = default) =>
        await Mudar(id, usuarioId, (solicitacao, agora) => solicitacao.Confirmar(agora), ct);

    /// <inheritdoc />
    public async Task<Result<SolicitacaoResumo>> Cancelar(Guid id, Guid usuarioId, CancellationToken ct = default) =>
        await Mudar(id, usuarioId, (solicitacao, agora) => solicitacao.Cancelar(agora), ct);

    /// <inheritdoc />
    public async Task<Result<ArquivoParaDownload>> Baixar(Guid id, Guid usuarioId, CancellationToken ct = default)
    {
        var solicitacao = await privacidadeRepository.ObterDoTitular(id, usuarioId, ct);

        if (solicitacao is null || !solicitacao.Disponivel(DateTime.UtcNow))
            return Result.Falha<ArquivoParaDownload>([NaoEncontrada]);

        return await arquivoService.Baixar(solicitacao.ArquivoId!.Value, new SolicitanteDeArquivo(usuarioId, EhAdministrador: false), ct);
    }

    /// <summary>
    /// Confere a senha da conta antes de aceitar um pedido de eliminação.
    /// </summary>
    /// <remarks>
    /// A sessão prova quem entrou, não quem está na frente da tela agora. Eliminação é irreversível
    /// e a aba fica aberta — pedir a senha de novo é a diferença entre "alguém com acesso ao
    /// computador" e "o titular".
    /// <para>
    /// A recusa é <c>NaoAutenticado</c> e não <c>Validacao</c>: o front trata os dois de formas
    /// diferentes, e senha errada não é campo mal preenchido.
    /// </para>
    /// </remarks>
    /// <param name="usuarioId">Titular.</param>
    /// <param name="senha">O que a tela digitou.</param>
    private async Task<Result> ConferirSenha(Guid usuarioId, string? senha)
    {
        if (string.IsNullOrWhiteSpace(senha))
            return Result.Falha(Erro.Validacao("privacidade.senha_obrigatoria", "Digite sua senha para confirmar.", "senha"));

        var usuario = await userManager.FindByIdAsync(usuarioId.ToString());

        if (usuario is null || !await userManager.CheckPasswordAsync(usuario, senha))
            return Result.Falha(Erro.NaoAutenticado("privacidade.senha_invalida", "Senha incorreta."));

        return Result.Ok();
    }

    /// <summary>Aplica uma transição à solicitação do titular e devolve o resumo atualizado.</summary>
    /// <param name="id">Solicitação.</param>
    /// <param name="usuarioId">Titular — o recorte que impede mexer na de terceiro.</param>
    /// <param name="transicao">A mudança de estado, que já valida se é possível.</param>
    private async Task<Result<SolicitacaoResumo>> Mudar(
        Guid id,
        Guid usuarioId,
        Func<SolicitacaoDePrivacidade, DateTime, Result> transicao,
        CancellationToken ct
    )
    {
        var solicitacao = await privacidadeRepository.ObterDoTitular(id, usuarioId, ct);

        if (solicitacao is null)
            return Result.Falha<SolicitacaoResumo>([NaoEncontrada]);

        var resultado = transicao(solicitacao, DateTime.UtcNow);

        if (resultado.Falhou)
            return Result.Falha<SolicitacaoResumo>(resultado.Erros);

        await unitOfWork.SalvarAsync(ct);

        return Resumir(solicitacao);
    }

    /// <summary>Avisa o titular e os presidentes das turmas dele.</summary>
    /// <remarks>
    /// Os presidentes recebem com a soma do que o titular deve na turma deles. Não é para eles
    /// impedirem nada — o direito não depende de estar em dia —, é para a comissão saber que a
    /// cobrança continua e que ela perdeu o nome.
    /// </remarks>
    /// <param name="usuarioId">Titular.</param>
    /// <param name="prazoEm">Quando a eliminação acontece.</param>
    private async Task AvisarDaExclusao(Guid usuarioId, DateTime prazoEm, CancellationToken ct)
    {
        var titular = await privacidadeRepository.ObterTitular(usuarioId, ct);

        if (titular is null)
            return;

        await emails.ExclusaoSolicitada(titular.Email, prazoEm, ct);

        foreach (var presidente in await privacidadeRepository.ListarPresidentesParaAviso(usuarioId, ct))
            await emails.ExclusaoParaOPresidente(presidente, titular.Nome, prazoEm, ct);
    }

    private static SolicitacaoResumo Resumir(SolicitacaoDePrivacidade solicitacao) =>
        new(
            solicitacao.Id,
            solicitacao.Tipo,
            solicitacao.Status,
            solicitacao.CriadoEm,
            solicitacao.PrazoEm,
            solicitacao.ConfirmadaEm,
            solicitacao.ConcluidaEm,
            solicitacao.ExpiraEm,
            solicitacao.Disponivel(DateTime.UtcNow),
            solicitacao.Motivo
        );
}
