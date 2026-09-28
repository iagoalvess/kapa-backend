using Backend.Business.Abstractions;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Admin.Models;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Services;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Common.Texto;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Usuarios.Interfaces;
using Backend.Business.Usuarios.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Admin.Services;

/// <summary>
/// Operações do painel administrativo e do painel de suporte.
/// </summary>
/// <remarks>
/// O painel de suporte é a fatia da Sprint 16 sem a qual não há como atender ninguém: no dia
/// seguinte ao go-live alguém escreve "paguei e a turma não ativou", e a alternativa a estas telas
/// é abrir o banco de produção e rodar SQL na mão — num banco com CPF criptografado, tabelas
/// append-only e dinheiro dentro.
/// <para>
/// As quatro regras que o tornam seguro estão no código, e não no combinado:
/// </para>
/// <list type="number">
/// <item>toda ação grava evento de auditoria com autor e alvo, na mesma transação;</item>
/// <item>CPF sai mascarado, como sai para a Gestão — quem atende não precisa do número inteiro;</item>
/// <item>
/// <b>não existe "entrar como"</b>: nenhum método daqui emite sessão de outra pessoa, e é por isso
/// que a redefinição de senha é um e-mail ao dono, e não uma senha escolhida pelo atendente;
/// </item>
/// <item>
/// tudo o mais é leitura — não há aqui editar cadastro, dar baixa em parcela nem mexer em dinheiro
/// de turma. Isso tem tela própria, com o papel certo e a trilha certa.
/// </item>
/// </list>
/// </remarks>
/// <param name="adminRepository">Consultas agregadas e as leituras do suporte.</param>
/// <param name="assinaturaRepository">A licença da turma, para a ativação manual.</param>
/// <param name="formaturaRepository">A turma, para a ativação manual.</param>
/// <param name="vinculoRepository">Presidentes, destinatários do aviso de ativação.</param>
/// <param name="usuarioRepository">A conta alvo das ações de acesso.</param>
/// <param name="contaService">Reenvio de confirmação e redefinição de senha, os mesmos que o usuário pede.</param>
/// <param name="emails">E-mails da assinatura.</param>
/// <param name="eventos">Trilha de auditoria.</param>
/// <param name="userManager">API do Identity, para levantar o bloqueio por tentativas.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class AdminService(
    IAdminRepository adminRepository,
    IAssinaturaRepository assinaturaRepository,
    IFormaturaRepository formaturaRepository,
    IVinculoRepository vinculoRepository,
    IUsuarioRepository usuarioRepository,
    IContaService contaService,
    EmailsDeAssinatura emails,
    IEventoRepository eventos,
    UserManager<Usuario> userManager,
    IUnitOfWork unitOfWork,
    ILogger<AdminService> logger
) : IAdminService
{
    /// <summary>
    /// Piso do termo de busca.
    /// </summary>
    /// <remarks>
    /// Duas letras varreriam as duas maiores tabelas do banco a cada tecla digitada e devolveriam a
    /// lista inteira — que não ajuda quem procura e é um vazamento com outro nome.
    /// </remarks>
    private const int MinimoDoTermo = 3;

    /// <summary>Teto de cada lista da busca.</summary>
    /// <remarks>
    /// <c>ponytail:</c> sem paginação. Quem atende refina o termo; lista de vinte é para ler, não
    /// para percorrer. Paginar quando o suporte passar a procurar por sobrenome comum.
    /// </remarks>
    private const int LimiteDaBusca = 20;

    private static readonly Erro TurmaNaoEncontrada = Erro.NaoEncontrado("suporte.turma_nao_encontrada", "Turma não encontrada.");

    private static readonly Erro ContaNaoEncontrada = Erro.NaoEncontrado("suporte.conta_nao_encontrada", "Conta não encontrada.");

    /// <summary>A janela de "cadastros recentes" do painel.</summary>
    private const int DiasDeCadastroRecente = 30;

    /// <inheritdoc />
    public async Task<Result<ResumoAdmin>> ObterResumo(CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        return Result.Ok(await adminRepository.ObterResumo(agora, agora.AddDays(-DiasDeCadastroRecente), ct));
    }

    /// <inheritdoc />
    public async Task<Result<ResultadoDaBusca>> Buscar(string? termo, CancellationToken ct = default)
    {
        var limpo = termo?.Trim() ?? string.Empty;

        if (limpo.Length < MinimoDoTermo)
            return Result.Ok(new ResultadoDaBusca([], []));

        return Result.Ok(
            new ResultadoDaBusca(
                await adminRepository.BuscarTurmasDeTodasAsFormaturas(limpo, LimiteDaBusca, ct),
                await adminRepository.BuscarUsuariosDeTodasAsFormaturas(limpo, LimiteDaBusca, ct)
            )
        );
    }

    /// <inheritdoc />
    public async Task<Result<TurmaNoSuporte>> ObterTurma(Guid formaturaId, CancellationToken ct = default) =>
        await adminRepository.ObterTurmaDeTodasAsFormaturas(formaturaId, ct) is { } turma ? Result.Ok(turma) : TurmaNaoEncontrada;

    /// <inheritdoc />
    public async Task<Result<UsuarioNoSuporte>> ObterUsuario(Guid usuarioId, CancellationToken ct = default) =>
        await adminRepository.ObterUsuarioDeTodasAsFormaturas(usuarioId, ct) is { } conta ? Result.Ok(conta) : ContaNaoEncontrada;

    /// <inheritdoc />
    /// <remarks>
    /// <c>Pendente</c> vira ativa pelo mesmo caminho do webhook (<c>ConfirmarPagamento</c>);
    /// <c>Vencida</c> ganha um ciclo novo (<c>Renovar</c>). São os dois estados em que a turma pagou
    /// e o sistema não soube. Turma já ativa responde sucesso sem gravar evento nem mandar e-mail: a
    /// ação é idempotente porque quem clica está justamente em dúvida sobre o estado dela.
    /// </remarks>
    public Task<Result<TurmaNoSuporte>> AtivarAssinatura(Guid formaturaId, Guid autorId, CancellationToken ct = default) =>
        unitOfWork.EmTransacaoAsync<Result<TurmaNoSuporte>>(
            async token =>
            {
                var formatura = await formaturaRepository.ObterParaEdicao(formaturaId, token);

                if (formatura is null)
                    return TurmaNaoEncontrada;

                var assinatura = await assinaturaRepository.ObterMaisRecenteParaEdicaoDeTodasAsFormaturas(formaturaId, token);

                if (assinatura is null)
                    return Erro.Conflito(
                        "suporte.assinatura_inexistente",
                        "Esta turma nunca contratou um plano. Peça à comissão que escolha um na tela de planos."
                    );

                var assinaturaAntes = assinatura.Status;
                var turmaAntes = formatura.Status;
                var agora = DateTime.UtcNow;
                var ciclo = (await assinaturaRepository.ObterPlano(assinatura.PlanoId, token))?.Ciclo ?? CicloDeCobranca.Mensal;

                var efeito = assinatura.Status switch
                {
                    StatusDaAssinatura.Pendente => assinatura.ConfirmarPagamento(agora, ciclo),
                    StatusDaAssinatura.Vencida => assinatura.Renovar(agora, ciclo),
                    _ => Result.Ok(),
                };

                if (efeito.Falhou)
                    return Result.Falha<TurmaNoSuporte>(efeito.Erros);

                var ativacao = formatura.AtivarPorPagamento();

                if (ativacao.Falhou)
                    return Result.Falha<TurmaNoSuporte>(ativacao.Erros);

                if (assinaturaAntes != assinatura.Status || turmaAntes != formatura.Status)
                {
                    await eventos.Auditar(
                        NomesDeAuditoria.SuporteAssinaturaAtivada,
                        autorId,
                        new
                        {
                            formaturaId,
                            assinaturaId = assinatura.Id,
                            antes = new { assinatura = assinaturaAntes, turma = turmaAntes },
                            depois = new { assinatura = assinatura.Status, turma = formatura.Status },
                        },
                        token
                    );

                    if (assinatura.VigenteAte is { } vigenteAte)
                        await emails.BoasVindas(formatura, await vinculoRepository.ListarEmailsDosPresidentes(formaturaId, token), vigenteAte, token);

                    logger.LogWarning("Suporte ativou à mão a assinatura da formatura {FormaturaId}.", formaturaId);
                }

                await unitOfWork.SalvarAsync(token);

                return await adminRepository.ObterTurmaDeTodasAsFormaturas(formaturaId, token) is { } turma ? Result.Ok(turma) : TurmaNaoEncontrada;
            },
            ct
        );

    /// <inheritdoc />
    public Task<Result> ReenviarConfirmacao(Guid usuarioId, Guid autorId, CancellationToken ct = default) =>
        AcaoNaConta(
            usuarioId,
            autorId,
            NomesDeAuditoria.SuporteConfirmacaoReenviada,
            (conta, token) => contaService.ReenviarConfirmacao(new PedidoPorEmail(conta.Email), token),
            ct
        );

    /// <inheritdoc />
    public Task<Result> DispararRedefinicaoDeSenha(Guid usuarioId, Guid autorId, CancellationToken ct = default) =>
        AcaoNaConta(
            usuarioId,
            autorId,
            NomesDeAuditoria.SuporteRedefinicaoDisparada,
            (conta, token) => contaService.SolicitarRedefinicaoDeSenha(new PedidoPorEmail(conta.Email), token),
            ct
        );

    /// <inheritdoc />
    public Task<Result> Desbloquear(Guid usuarioId, Guid autorId, CancellationToken ct = default) =>
        AcaoNaConta(usuarioId, autorId, NomesDeAuditoria.SuporteContaDesbloqueada, Levantar, ct);

    /// <summary>
    /// A forma comum das três ações de conta: acha a conta, executa, audita e grava — tudo junto.
    /// </summary>
    /// <remarks>
    /// O evento vai <b>sem</b> <c>formaturaId</c>, como o <c>auth.bloqueio_por_tentativas</c>: são
    /// ações sobre a conta, e a pessoa pode estar em nenhuma turma ou em três. Ficam na retenção
    /// longa e fora da trilha de qualquer turma, que é onde elas não significariam nada.
    /// <para>
    /// O e-mail entra no corpo <b>mascarado</b>. A trilha precisa dizer sobre quem foi a ação, e o
    /// <c>usuarioId</c> já diz; o endereço inteiro ali transformaria a tabela de auditoria numa
    /// segunda lista de e-mails, com retenção de cinco anos.
    /// </para>
    /// </remarks>
    /// <param name="usuarioId">Conta alvo.</param>
    /// <param name="autorId">Quem do suporte executou.</param>
    /// <param name="nomeDoEvento">Nome estável em <see cref="NomesDeAuditoria"/>.</param>
    /// <param name="executar">O que fazer com a conta.</param>
    private Task<Result> AcaoNaConta(
        Guid usuarioId,
        Guid autorId,
        string nomeDoEvento,
        Func<UsuarioDetalhe, CancellationToken, Task<Result>> executar,
        CancellationToken ct
    ) =>
        unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var conta = await usuarioRepository.ObterDetalhe(usuarioId, token);

                if (conta is null)
                    return Result.Falha(ContaNaoEncontrada);

                var resultado = await executar(conta, token);

                if (resultado.Falhou)
                    return resultado;

                await eventos.Auditar(nomeDoEvento, autorId, new { usuarioId, email = TextoUtils.MascararEmail(conta.Email) }, token);

                await unitOfWork.SalvarAsync(token);

                return Result.Ok();
            },
            ct
        );

    /// <summary>Levanta o bloqueio por tentativas de senha e zera o contador.</summary>
    /// <remarks>
    /// O <c>UserManager</c> persiste por conta própria — é a exceção documentada do
    /// <c>IUnitOfWork</c> —, e por isso a chamada acontece dentro da transação de
    /// <see cref="AcaoNaConta"/>: sem ela, o desbloqueio ficaria gravado e o evento não.
    /// <para>
    /// Desbloquear <b>não</b> reativa conta desativada: são coisas diferentes. Bloqueio é a defesa
    /// contra força bruta e expira sozinho; desativar é decisão administrativa, e se desfaz na
    /// gestão de usuários, que tem tela própria.
    /// </para>
    /// </remarks>
    /// <param name="conta">Conta alvo.</param>
    private async Task<Result> Levantar(UsuarioDetalhe conta, CancellationToken ct)
    {
        var usuario = await userManager.FindByIdAsync(conta.Id.ToString());

        if (usuario is null)
            return Result.Falha(ContaNaoEncontrada);

        await userManager.SetLockoutEndDateAsync(usuario, null);
        await userManager.ResetAccessFailedCountAsync(usuario);

        return Result.Ok();
    }
}
