using Backend.Business.Abstractions;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Admin.Models;
using Backend.Business.Arquivos.Models;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Services;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Texto;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Relatorios.Models;
using Backend.Business.Relatorios.Services;
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
/// <param name="provedor">O PSP da licença, para o estorno.</param>
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
    IProvedorDeAssinatura provedor,
    IUnitOfWork unitOfWork,
    ILogger<AdminService> logger
) : IAdminService
{
    /// <summary>A janela da desistência com reembolso integral: 7 dias do pagamento (Termos, seção 7; art. 49 do CDC).</summary>
    private const int DiasDeDesistencia = 7;

    /// <summary>O período do analytics quando a tela não diz qual: os últimos 30 dias, hoje inclusive.</summary>
    private const int DiasDoPeriodoPadrao = 30;

    /// <summary>Teto do período do analytics.</summary>
    /// <remarks>
    /// Um trimestre. O ranking de uso conta pessoas e turmas distintas na maior tabela do banco, e o custo cresce com
    /// o período: num clone com 3 milhões de eventos, 30 dias levam ~300 ms e um ano, 3,4 s (Sprint 44, critério 6).
    /// As pílulas da tela vão até 31 dias; prazo mais longo é pergunta para a série mensal.
    /// </remarks>
    private const int MaximoDeDiasDoPeriodo = 92;

    /// <summary>Teto da série mensal.</summary>
    private const int MaximoDeMeses = 24;

    private static readonly Erro TurmaNaoEncontrada = Erro.NaoEncontrado("suporte.turma_nao_encontrada", "Turma não encontrada.");

    private static readonly Erro ContaNaoEncontrada = Erro.NaoEncontrado("suporte.conta_nao_encontrada", "Conta não encontrada.");

    /// <inheritdoc />
    public async Task<Result<AnalyticsDaPlataforma>> ObterAnalytics(DateOnly? de, DateOnly? ate, CancellationToken ct = default)
    {
        var fim = ate ?? DataUtils.Hoje();
        var inicio = de ?? fim.AddDays(1 - DiasDoPeriodoPadrao);

        if (inicio > fim)
            return Erro.Validacao("analytics.periodo_invertido", "O início do período precisa vir antes do fim.", campo: "de");

        if (fim.DayNumber - inicio.DayNumber >= MaximoDeDiasDoPeriodo)
            return Erro.Validacao(
                "analytics.periodo_longo",
                "Escolha um período de até 92 dias. Para prazos mais longos, use a série mensal.",
                campo: "de"
            );

        return await adminRepository.ObterAnalyticsDeTodasAsFormaturas(inicio, fim, DateTime.UtcNow, ct);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MesDaPlataforma>>> ObterSerieMensal(int meses, CancellationToken ct = default)
    {
        if (meses is < 1 or > MaximoDeMeses)
            return Erro.Validacao("analytics.meses_invalidos", $"Peça de 1 a {MaximoDeMeses} meses.", campo: "meses");

        var hoje = DataUtils.Hoje();

        return Result.Ok(
            await adminRepository.ObterSerieMensalDeTodasAsFormaturas(new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(1 - meses), meses, ct)
        );
    }

    /// <inheritdoc />
    public async Task<Result<PaginaDe<TurmaNoPainel>>> ListarTurmas(
        PaginacaoRequest paginacao,
        FiltroDeTurmasNoPainel filtro,
        CancellationToken ct = default
    ) => await adminRepository.ListarTurmasDeTodasAsFormaturas(paginacao.Normalizar(), filtro, ct);

    /// <inheritdoc />
    public async Task<Result<PaginaDe<ContaNoPainel>>> ListarContas(
        PaginacaoRequest paginacao,
        FiltroDeContasNoPainel filtro,
        CancellationToken ct = default
    ) => await adminRepository.ListarContasDeTodasAsFormaturas(paginacao.Normalizar(), filtro, DateTime.UtcNow, ct);

    /// <inheritdoc />
    public async Task<Result<PaginaDe<MembroNoSuporte>>> ListarMembros(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        CancellationToken ct = default
    ) => await adminRepository.ListarMembrosDeTodasAsFormaturas(formaturaId, paginacao.Normalizar(), ct);

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
    /// <remarks>
    /// A ordem protege o dinheiro: a renovação é cancelada no provedor <b>antes</b> do estorno — se o provedor falhar
    /// ali, nada foi devolvido e nada muda. Depois do estorno, o que resta é gravar; o aviso de recorrência cancelada
    /// que o provedor manda em seguida acerta a assinatura se a gravação falhar.
    /// <para>
    /// O proporcional é o que falta da vigência sobre o ciclo do plano. <c>ponytail:</c> mede pela vigência atual —
    /// estornar um ciclo antigo pelo proporcional devolve o que falta do ciclo corrente; o suporte estorna o último.
    /// </para>
    /// </remarks>
    public Task<Result<TurmaNoSuporte>> Estornar(
        Guid formaturaId,
        Guid cobrancaId,
        ModoDeEstorno modo,
        Guid autorId,
        CancellationToken ct = default
    ) =>
        unitOfWork.EmTransacaoAsync<Result<TurmaNoSuporte>>(
            async token =>
            {
                var cobranca = await assinaturaRepository.ObterCobrancaParaEdicaoDeTodasAsFormaturas(cobrancaId, token);
                var assinatura = cobranca is null
                    ? null
                    : await assinaturaRepository.ObterParaEdicaoDeTodasAsFormaturas(cobranca.AssinaturaId, token);

                if (cobranca is null || assinatura is null || assinatura.FormaturaId != formaturaId)
                    return Erro.NaoEncontrado("suporte.pagamento_nao_encontrado", "Pagamento não encontrado nesta turma.");

                if (cobranca is not { Situacao: SituacaoDaCobrancaDoPlano.Paga, IdDoPagamento: { } idDoPagamento, PagaEm: { } pagaEm })
                    return Erro.Conflito("estorno.cobranca_nao_paga", "Só um pagamento confirmado, e ainda não estornado, pode ser estornado.");

                var agora = DateTime.UtcNow;
                var ciclo = (await assinaturaRepository.ObterPlano(assinatura.PlanoId, token))?.Ciclo ?? CicloDeCobranca.Mensal;

                if (modo == ModoDeEstorno.Integral && pagaEm < agora.AddDays(-DiasDeDesistencia))
                    return Erro.Conflito(
                        "estorno.fora_do_prazo",
                        "O reembolso integral vale até 7 dias depois do pagamento. Depois disso, só o proporcional, nos casos dos Termos."
                    );

                var valor =
                    modo == ModoDeEstorno.Integral
                        ? cobranca.ValorEmCentavos
                        : (long)Math.Round(cobranca.ValorEmCentavos * assinatura.FracaoRestante(agora, ciclo));

                if (valor <= 0)
                    return Erro.Conflito("estorno.nada_a_devolver", "A vigência deste pagamento já acabou: não há o que devolver pelo proporcional.");

                if (assinatura.IdExterno is { } recorrencia)
                {
                    var cancelada = await provedor.Cancelar(recorrencia, token);
                    if (cancelada.Falhou)
                        return Result.Falha<TurmaNoSuporte>(cancelada.Erros);
                }

                var devolvido = await provedor.Estornar(idDoPagamento, valor, cobranca.Id, token);
                if (devolvido.Falhou)
                    return Result.Falha<TurmaNoSuporte>(devolvido.Erros);

                cobranca.Estornar(valor, agora);
                assinatura.IdExterno = null;
                assinatura.Encerrar(agora);

                var formatura = await formaturaRepository.ObterParaEdicao(formaturaId, token);
                formatura?.Transicionar(StatusDaFormatura.Suspensa);

                await eventos.Auditar(
                    NomesDeAuditoria.SuportePagamentoEstornado,
                    autorId,
                    new
                    {
                        formaturaId,
                        assinaturaId = assinatura.Id,
                        cobrancaId,
                        modo = modo.ToString(),
                        valorEmCentavos = valor,
                    },
                    token
                );

                logger.LogWarning(
                    "Suporte estornou {Valor} centavos do pagamento {CobrancaId} da formatura {FormaturaId}.",
                    valor,
                    cobrancaId,
                    formaturaId
                );

                await unitOfWork.SalvarAsync(token);

                return await adminRepository.ObterTurmaDeTodasAsFormaturas(formaturaId, token) is { } turma ? Result.Ok(turma) : TurmaNaoEncontrada;
            },
            ct
        );

    /// <inheritdoc />
    /// <remarks>O mês é o do fuso de exibição: o pagamento das 22h do dia 30 em Brasília é de setembro, ainda que em UTC já seja outubro.</remarks>
    public async Task<Result<ArquivoParaDownload>> ExportarPagamentos(int ano, int mes, CancellationToken ct = default)
    {
        if (mes is < 1 or > 12 || ano is < 2000 or > 2100)
            return Erro.Validacao("suporte.mes_invalido", "Escolha um mês válido.", campo: "mes");

        var primeiro = new DateOnly(ano, mes, 1);
        var pagamentos = await adminRepository.ListarPagamentosParaNotaDeTodasAsFormaturas(
            DataUtils.InicioDoDiaEmUtc(primeiro),
            DataUtils.InicioDoDiaEmUtc(primeiro.AddMonths(1)),
            ct
        );

        var tabela = new TabelaDoRelatorio(
            $"Pagamentos dos planos — {mes:00}/{ano}",
            $"{pagamentos.Count} pagamento(s), {FormatosBrasileiros.Reais(pagamentos.Sum(p => p.ValorEmCentavos))} — base da nota fiscal manual",
            [
                new("Data", 1, Direita: true),
                new("Turma", 2),
                new("Instituição", 2),
                new("Plano", 1),
                new("Motivo", 1),
                new("Meio", 1),
                new("Valor", 1, Direita: true),
                new("Estornado", 1, Direita: true),
                new("Tomador (Presidente)", 2),
                new("E-mail", 2),
                new("CPF", 1),
                new("Pagamento no provedor", 1),
            ],
            [
                .. pagamentos.Select(p =>
                    (IReadOnlyList<Celula>)
                        [
                            Celula.Data(DateOnly.FromDateTime(DataUtils.ParaExibicao(p.PagaEm))),
                            Celula.De(p.Turma),
                            Celula.De(p.Instituicao),
                            Celula.De(p.Plano),
                            Celula.De(p.Motivo == MotivoDaCobranca.Diferenca ? "Diferença de plano" : "Ciclo"),
                            Celula.De(MeiosDePagamento.Rotulo(p.Meio)),
                            Celula.Reais(p.ValorEmCentavos),
                            Celula.Reais(p.ValorEstornadoEmCentavos),
                            Celula.De(p.PresidenteNome),
                            Celula.De(p.PresidenteEmail),
                            Celula.De(p.PresidenteCpf is { } cpf ? FormatosBrasileiros.FormatarCpf(cpf) : null),
                            Celula.De(p.IdDoPagamento),
                        ]
                ),
            ]
        );

        return new ArquivoParaDownload(
            new MemoryStream(RelatorioEmExcel.Gerar(tabela)),
            $"pagamentos-dos-planos-{ano}-{mes:00}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        );
    }

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
