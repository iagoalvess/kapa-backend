using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Services;
using Backend.Business.Assinaturas.Services;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Festa.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Services;
using FluentValidation;

namespace Backend.Business.Formaturas.Services;

/// <summary>
/// Gestão dos membros da formatura selecionada.
/// </summary>
/// <param name="vinculoRepository">Vínculos entre usuário e formatura.</param>
/// <param name="perfilRepository">Cadastros, de onde saem o vínculo e o nome de quem sai.</param>
/// <param name="parcelaRepository">Parcelas, para somar e cancelar o que a saída alcança.</param>
/// <param name="adesaoRepository">Adesões, para separar Desligar de Remover.</param>
/// <param name="formaturaRepository">Formatura, para o nome da turma nos e-mails.</param>
/// <param name="eventos">Trilha de auditoria, gravada na mesma transação.</param>
/// <param name="emails">E-mails da saída.</param>
/// <param name="vagas">Quem pode virar Formando: só a turma com plano pago em vigor (Sprint 45, F2).</param>
/// <param name="papelValidator">Validador da troca de papel.</param>
/// <param name="desligamentoValidator">Validador do desligamento.</param>
/// <param name="valoresADevolver">O parcial das parcelas canceladas no desligamento (Sprint 42, decisão 3).</param>
/// <param name="emailsDePapel">O link que confirma um Presidente novo.</param>
/// <param name="confirmacao">A assinatura do link.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class MembroService(
    IVinculoRepository vinculoRepository,
    IPerfilRepository perfilRepository,
    IParcelaRepository parcelaRepository,
    IAdesaoRepository adesaoRepository,
    IFormaturaRepository formaturaRepository,
    IEventoRepository eventos,
    EmailsDeDesligamento emails,
    EmissaoDeConvites convites,
    VagasDoPlano vagas,
    IValidator<AlterarPapel> papelValidator,
    IValidator<DesligarFormando> desligamentoValidator,
    ValoresADevolver valoresADevolver,
    EmailsDePapel emailsDePapel,
    ConfirmacaoPorEmail confirmacao,
    IUnitOfWork unitOfWork
) : IMembroService
{
    private static readonly Erro SemAdesao = Erro.Conflito(
        "formatura.membro_sem_adesao",
        "Esta pessoa ainda não aderiu ao termo e não deve nada à turma. Use Remover."
    );

    private static readonly Erro JaDesligado = Erro.Conflito("formatura.membro_ja_desligado", "Esta pessoa já foi desligada da turma.");

    /// <summary>A finalidade do link que confirma um Presidente novo.</summary>
    public const string FinalidadeDaPromocao = "formatura.presidente";

    private static readonly Erro LinkInvalido = Erro.Validacao(
        "formatura.confirmacao_invalida",
        "Este link de confirmação venceu, já foi usado ou é de outra pessoa. Peça a promoção de novo na tela de membros."
    );

    private static readonly Erro UltimoPresidente = Erro.Conflito(
        "formatura.ultimo_presidente",
        "A formatura precisa de ao menos um presidente ativo. Promova outra pessoa antes."
    );

    /// <inheritdoc />
    public async Task<Result<PaginaDe<MembroDaFormatura>>> Listar(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeMembros filtro,
        CancellationToken ct = default
    ) => Result.Ok(await vinculoRepository.ListarMembros(formaturaId, paginacao.Normalizar(), filtro, ct));

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContagemDeMembros>>> Contar(Guid formaturaId, CancellationToken ct = default) =>
        Result.Ok(await vinculoRepository.ContarMembros(formaturaId, ct));

    /// <inheritdoc />
    /// <remarks>
    /// Para Presidente, só pede: o link vai ao e-mail de quem pediu, e a promoção vale em
    /// <see cref="ConfirmarPresidente"/> (revisão de segurança de 05/10/2026). Os demais papéis valem na hora.
    /// </remarks>
    public async Task<Result<AlteracaoDePapel>> AlterarPapel(
        Guid formaturaId,
        Guid usuarioId,
        AlterarPapel dados,
        Guid autorId,
        CancellationToken ct = default
    )
    {
        var validacao = papelValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<AlteracaoDePapel>(validacao.Erros);

        if (await vagas.ConferirPapel(formaturaId, dados.Papel, ct) is { } semPlanoPago)
            return semPlanoPago;

        if (dados.Papel == PapelNaFormatura.Presidente)
            return await PedirPromocao(formaturaId, usuarioId, autorId, ct);

        var aplicada = await Aplicar(formaturaId, usuarioId, dados.Papel, autorId, null, ct);

        return aplicada.Sucesso ? new AlteracaoDePapel(null) : Result.Falha<AlteracaoDePapel>(aplicada.Erros);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Vale só para quem pediu, na turma em que pediu, e enquanto o membro tiver o papel que tinha no pedido — aplicado
    /// uma vez, o mesmo link morre.
    /// </remarks>
    public async Task<Result> ConfirmarPresidente(Guid formaturaId, Guid autorId, string? token, CancellationToken ct = default)
    {
        if (
            confirmacao.Ler<PromocaoAPresidente>(FinalidadeDaPromocao, token, DateTime.UtcNow) is not { } promocao
            || promocao.FormaturaId != formaturaId
            || promocao.AutorId != autorId
        )
            return Result.Falha(LinkInvalido);

        if (await vagas.ConferirPapel(formaturaId, PapelNaFormatura.Presidente, ct) is { } semPlanoPago)
            return Result.Falha(semPlanoPago);

        return await Aplicar(formaturaId, promocao.UsuarioId, PapelNaFormatura.Presidente, autorId, promocao.Antes, ct);
    }

    /// <summary>Manda a quem pediu o link que confirma o Presidente novo, e deixa o pedido na trilha.</summary>
    private async Task<Result<AlteracaoDePapel>> PedirPromocao(Guid formaturaId, Guid usuarioId, Guid autorId, CancellationToken ct)
    {
        var alvo = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        var autor = await perfilRepository.ObterMembro(formaturaId, autorId, ct);
        var vinculo = await vinculoRepository.ObterAtivoParaEdicao(usuarioId, formaturaId, ct);

        if (alvo is null || autor is null || vinculo is null)
            return ErrosDeFormatura.MembroNaoEncontrado;

        if (vinculo.Papel == PapelNaFormatura.Presidente)
            return new AlteracaoDePapel(null);

        var token = confirmacao.Assinar(
            FinalidadeDaPromocao,
            new PromocaoAPresidente(formaturaId, autorId, usuarioId, Estado(vinculo)),
            DateTime.UtcNow
        );
        var turma = (await formaturaRepository.ObterDetalheDeTodasAsFormaturas(formaturaId, ct))?.Nome ?? string.Empty;

        await emailsDePapel.ConfirmarPresidente(autor.Email, turma, alvo.Nome, alvo.Email, token, ct);
        await eventos.Auditar(
            NomesDeAuditoria.PresidentePedido,
            autorId,
            new
            {
                formaturaId,
                membroUsuarioId = usuarioId,
                antes = new { papel = vinculo.Papel },
            },
            ct
        );
        await unitOfWork.SalvarAsync(ct);

        return new AlteracaoDePapel(AdesaoService.MascararEmail(autor.Email));
    }

    /// <summary>
    /// A impressão digital do vínculo para o link de confirmação: o papel e a última gravação, que muda a cada
    /// <c>SalvarAsync</c> e não deixa o link reviver quando o papel volta ao do pedido.
    /// </summary>
    private static string Estado(VinculoDeFormatura vinculo) => ConfirmacaoPorEmail.Impressao(new { vinculo.Papel, vinculo.AtualizadoEm });

    /// <summary>Troca o papel sob a trava dos presidentes, com a trilha na mesma transação.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Membro a alterar.</param>
    /// <param name="papel">Papel novo.</param>
    /// <param name="autorId">Quem troca.</param>
    /// <param name="estadoEsperado">O vínculo que o link de confirmação viu (<see cref="Estado"/>); outro agora, o link não vale. Nulo: sem link.</param>
    /// <param name="ct">Token de cancelamento.</param>
    private async Task<Result> Aplicar(Guid formaturaId, Guid usuarioId, string papel, Guid autorId, string? estadoEsperado, CancellationToken ct)
    {
        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var presidentes = await vinculoRepository.TravarPresidentesAtivos(formaturaId, token);
                var vinculo = await vinculoRepository.ObterAtivoParaEdicao(usuarioId, formaturaId, token);

                if (vinculo is null)
                    return Result.Falha(ErrosDeFormatura.MembroNaoEncontrado);

                if (estadoEsperado is not null && Estado(vinculo) != estadoEsperado)
                    return Result.Falha(LinkInvalido);

                if (vinculo.Papel == papel)
                    return Result.Ok();

                if (DeixariaSemPresidente(vinculo, presidentes))
                    return Result.Falha(UltimoPresidente);

                var antes = vinculo.Papel;
                vinculo.Papel = papel;

                await eventos.Auditar(
                    NomesDeAuditoria.PapelAlterado,
                    autorId,
                    new
                    {
                        formaturaId,
                        membroUsuarioId = usuarioId,
                        antes = new { papel = antes },
                        depois = new { papel = vinculo.Papel },
                    },
                    token
                );

                return Result.Ok();
            },
            ct
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Desativar, nunca apagar: parcelas, pagamentos e adesão continuam apontando para o vínculo.
    /// Prestação de contas de formatura é consultada meses depois da festa. Os convites dos pacotes
    /// caem junto: quem saiu da turma não leva ninguém (Sprint 30, decisão 5).
    /// </remarks>
    public Task<Result> Remover(Guid formaturaId, Guid usuarioId, Guid autorId, CancellationToken ct = default) =>
        unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var presidentes = await vinculoRepository.TravarPresidentesAtivos(formaturaId, token);
                var vinculo = await vinculoRepository.ObterAtivoParaEdicao(usuarioId, formaturaId, token);

                if (vinculo is null)
                    return Result.Falha(ErrosDeFormatura.MembroNaoEncontrado);

                if (DeixariaSemPresidente(vinculo, presidentes))
                    return Result.Falha(UltimoPresidente);

                vinculo.Ativo = false;
                var convitesRevogados = await convites.RevogarDosPacotes(vinculo.Id, token);

                await eventos.Auditar(
                    NomesDeAuditoria.MembroRemovido,
                    autorId,
                    new
                    {
                        formaturaId,
                        membroUsuarioId = usuarioId,
                        vinculoId = vinculo.Id,
                        vinculo.Papel,
                        convitesRevogados,
                    },
                    token
                );

                return Result.Ok();
            },
            ct
        );

    /// <inheritdoc />
    public async Task<Result<ResumoDaSaida>> ResumirSaida(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        if (membro is null)
            return Result.Falha<ResumoDaSaida>(ErrosDeFormatura.MembroNaoEncontrado);

        var hoje = DataUtils.Hoje();
        var parcelas = await parcelaRepository.ListarDoVinculo(membro.VinculoId, hoje, ct);
        var emAberto = parcelas.Where(parcela => parcela.EmAberto).ToList();
        var emAtraso = emAberto.Where(parcela => parcela.Status == StatusDaParcela.Vencida).ToList();

        return new ResumoDaSaida(
            parcelas.Sum(parcela => parcela.ValorPagoEmCentavos ?? 0),
            emAberto.Count,
            emAberto.Sum(parcela => parcela.ValorOriginalEmCentavos - (parcela.ValorPagoEmCentavos ?? 0)),
            emAtraso.Count,
            emAtraso.Sum(parcela => parcela.ValorOriginalEmCentavos - (parcela.ValorPagoEmCentavos ?? 0))
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Tudo numa transação: o vínculo, o cancelamento, o evento e os e-mails ficam os quatro, ou
    /// nenhum. O e-mail não sai da requisição — ele é enfileirado e vai junto no <c>SalvarAsync</c>
    /// da transação, o que também é o que faz "desligar duas vezes não manda o segundo e-mail"
    /// depender só da guarda de idempotência, e não de sorte.
    /// <para>
    /// O valor já pago é lido antes de cancelar: quanto entrou é fato do passado, e o cancelamento
    /// não o move (decisão 5) — mas lê-lo depois faria a soma depender da ordem das operações.
    /// </para>
    /// <para>
    /// Os convites dos pacotes são revogados com motivo (Sprint 30, decisão 5); os comprados
    /// seguem o pedido, que o desligamento não toca.
    /// </para>
    /// </remarks>
    public async Task<Result> Desligar(Guid formaturaId, Guid usuarioId, DesligarFormando dados, Guid autorId, CancellationToken ct = default)
    {
        var validacao = desligamentoValidator.Validar(dados);
        if (validacao.Falhou)
            return validacao;

        var hoje = DataUtils.Hoje();
        var nomeDaTurma = await formaturaRepository.ObterNome(formaturaId, ct) ?? string.Empty;
        var destinatarios = await vinculoRepository.ListarEmailsDaComissao(formaturaId, ct);

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var presidentes = await vinculoRepository.TravarPresidentesAtivos(formaturaId, token);
                var vinculo = await vinculoRepository.ObterParaEdicao(usuarioId, formaturaId, token);

                if (vinculo is null || (!vinculo.Ativo && !vinculo.Desligado))
                    return Result.Falha(ErrosDeFormatura.MembroNaoEncontrado);

                if (vinculo.Desligado)
                    return Result.Falha(JaDesligado);

                if (!await adesaoRepository.JaAderiuAlgumaVez(vinculo.Id, token))
                    return Result.Falha(SemAdesao);

                if (DeixariaSemPresidente(vinculo, presidentes))
                    return Result.Falha(UltimoPresidente);

                var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, token);
                var jaPago = (await parcelaRepository.ListarDoVinculo(vinculo.Id, hoje, token)).Sum(p => p.ValorPagoEmCentavos ?? 0);

                var abertas = await parcelaRepository.ListarEmAbertoDoVinculoParaEdicao(vinculo.Id, token);
                var canceladas = abertas.Where(parcela => parcela.Cancelar(hoje, dados.CancelarAtraso)).ToList();
                var cancelado = new CancelamentoDaSaida(canceladas.Count, canceladas.Sum(parcela => parcela.ValorOriginalEmCentavos));
                var aDevolver = await valoresADevolver.RegistrarParciais(canceladas, token);

                vinculo.Desligar(dados.Motivo, dados.Detalhe, DateTime.UtcNow);
                var convitesRevogados = await convites.RevogarDosPacotes(vinculo.Id, token);

                await eventos.Auditar(
                    NomesDeAuditoria.FormandoDesligado,
                    autorId,
                    new
                    {
                        formaturaId,
                        usuarioId,
                        vinculoId = vinculo.Id,
                        vinculo.Papel,
                        dados.Motivo,
                        dados.Detalhe,
                        dados.CancelarAtraso,
                        parcelasCanceladas = cancelado.Parcelas,
                        canceladoEmCentavos = cancelado.ValorEmCentavos,
                        jaPagoEmCentavos = jaPago,
                        aDevolverEmCentavos = aDevolver,
                        convitesRevogados,
                    },
                    token
                );

                if (membro is { Email.Length: > 0 })
                    await emails.Confirmacao(membro.Email, nomeDaTurma, cancelado, jaPago, token);

                await emails.Aviso(destinatarios, nomeDaTurma, membro?.Nome ?? string.Empty, dados.Motivo, cancelado, token);

                return Result.Ok();
            },
            ct
        );
    }

    /// <summary>
    /// Diz se tirar este vínculo da presidência deixaria a turma sem presidente ativo.
    /// </summary>
    /// <remarks>
    /// Turma sem presidente é turma que ninguém consegue administrar — nem o suporte, que não
    /// tem vínculo com ela.
    /// <para>
    /// A contagem vem de <see cref="IVinculoRepository.TravarPresidentesAtivos"/>, feita com os
    /// vínculos de presidente travados até o commit: dois presidentes rebaixando um ao outro ao
    /// mesmo tempo não conseguem, os dois, passar por esta checagem.
    /// </para>
    /// </remarks>
    /// <param name="vinculo">Vínculo que perderia a presidência.</param>
    /// <param name="presidentesAtivos">Presidentes ativos, contados sob trava.</param>
    private static bool DeixariaSemPresidente(VinculoDeFormatura vinculo, int presidentesAtivos) =>
        vinculo.Papel == PapelNaFormatura.Presidente && presidentesAtivos <= 1;
}
