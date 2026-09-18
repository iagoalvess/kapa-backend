using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
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
/// <param name="papelValidator">Validador da troca de papel.</param>
/// <param name="desligamentoValidator">Validador do desligamento.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class MembroService(
    IVinculoRepository vinculoRepository,
    IPerfilRepository perfilRepository,
    IParcelaRepository parcelaRepository,
    IAdesaoRepository adesaoRepository,
    IFormaturaRepository formaturaRepository,
    IEventoRepository eventos,
    EmailsDeDesligamento emails,
    IValidator<AlterarPapel> papelValidator,
    IValidator<DesligarFormando> desligamentoValidator,
    IUnitOfWork unitOfWork
) : IMembroService
{
    private static readonly Erro MembroNaoEncontrado = Erro.NaoEncontrado("membro.nao_encontrado", "Membro não encontrado nesta formatura.");

    private static readonly Erro SemAdesao = Erro.Conflito(
        "formatura.membro_sem_adesao",
        "Esta pessoa ainda não aderiu ao termo e não deve nada à turma. Use Remover."
    );

    private static readonly Erro JaDesligado = Erro.Conflito("formatura.membro_ja_desligado", "Esta pessoa já foi desligada da turma.");

    private static readonly Erro NaoDesligado = Erro.Conflito("formatura.membro_nao_desligado", "Esta pessoa não está desligada da turma.");

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
    public async Task<Result> AlterarPapel(Guid formaturaId, Guid usuarioId, AlterarPapel dados, Guid autorId, CancellationToken ct = default)
    {
        var validacao = papelValidator.Validar(dados);
        if (validacao.Falhou)
            return validacao;

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var presidentes = await vinculoRepository.TravarPresidentesAtivos(formaturaId, token);
                var vinculo = await vinculoRepository.ObterAtivoParaEdicao(usuarioId, formaturaId, token);

                if (vinculo is null)
                    return Result.Falha(MembroNaoEncontrado);

                if (vinculo.Papel == dados.Papel)
                    return Result.Ok();

                if (DeixariaSemPresidente(vinculo, presidentes))
                    return Result.Falha(UltimoPresidente);

                var antes = vinculo.Papel;
                vinculo.Papel = dados.Papel;

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
    /// Prestação de contas de formatura é consultada meses depois da festa.
    /// </remarks>
    public Task<Result> Remover(Guid formaturaId, Guid usuarioId, Guid autorId, CancellationToken ct = default) =>
        unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var presidentes = await vinculoRepository.TravarPresidentesAtivos(formaturaId, token);
                var vinculo = await vinculoRepository.ObterAtivoParaEdicao(usuarioId, formaturaId, token);

                if (vinculo is null)
                    return Result.Falha(MembroNaoEncontrado);

                if (DeixariaSemPresidente(vinculo, presidentes))
                    return Result.Falha(UltimoPresidente);

                vinculo.Ativo = false;

                await eventos.Auditar(
                    NomesDeAuditoria.MembroRemovido,
                    autorId,
                    new
                    {
                        formaturaId,
                        membroUsuarioId = usuarioId,
                        vinculoId = vinculo.Id,
                        vinculo.Papel,
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
            return Result.Falha<ResumoDaSaida>(MembroNaoEncontrado);

        var hoje = DataUtils.Hoje();
        var parcelas = await parcelaRepository.ListarDoVinculo(membro.VinculoId, hoje, ct);
        var emAberto = parcelas.Where(parcela => parcela.EmAberto).ToList();
        var emAtraso = emAberto.Where(parcela => parcela.Status == StatusDaParcela.Vencida).ToList();

        return new ResumoDaSaida(
            membro.Nome,
            await adesaoRepository.JaAderiuAlgumaVez(membro.VinculoId, ct),
            parcelas.Sum(parcela => parcela.ValorPagoEmCentavos ?? 0),
            emAberto.Count,
            emAberto.Sum(parcela => parcela.ValorOriginalEmCentavos),
            emAtraso.Count,
            emAtraso.Sum(parcela => parcela.ValorOriginalEmCentavos)
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Tudo numa transação: o vínculo, o cancelamento, o evento e os e-mails ficam os quatro, ou
    /// nenhum. O e-mail não sai da requisição — ele é enfileirado e vai junto no <c>SalvarAsync</c>
    /// da transação, o que também é o que faz "desligar duas vezes não manda o segundo e-mail"
    /// depender só da guarda de idempotência, e não de sorte.
    /// </remarks>
    public async Task<Result> Desligar(Guid formaturaId, Guid usuarioId, DesligarFormando dados, Guid autorId, CancellationToken ct = default)
    {
        var validacao = desligamentoValidator.Validar(dados);
        if (validacao.Falhou)
            return validacao;

        var hoje = DataUtils.Hoje();
        var nomeDaTurma = (await formaturaRepository.ObterDetalhe(formaturaId, ct))?.Nome ?? string.Empty;
        var destinatarios = await vinculoRepository.ListarEmailsDaComissao(formaturaId, ct);

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var presidentes = await vinculoRepository.TravarPresidentesAtivos(formaturaId, token);
                var vinculo = await vinculoRepository.ObterParaEdicao(usuarioId, formaturaId, token);

                if (vinculo is null || (!vinculo.Ativo && !vinculo.Desligado))
                    return Result.Falha(MembroNaoEncontrado);

                if (vinculo.Desligado)
                    return Result.Falha(JaDesligado);

                if (!await adesaoRepository.JaAderiuAlgumaVez(vinculo.Id, token))
                    return Result.Falha(SemAdesao);

                if (DeixariaSemPresidente(vinculo, presidentes))
                    return Result.Falha(UltimoPresidente);

                // Antes de cancelar: quanto entrou é fato do passado, e o cancelamento não o move
                // (decisão 5) — mas lê-lo depois faria a soma depender da ordem das linhas acima.
                var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, token);
                var jaPago = (await parcelaRepository.ListarDoVinculo(vinculo.Id, hoje, token)).Sum(p => p.ValorPagoEmCentavos ?? 0);

                var abertas = await parcelaRepository.ListarEmAbertoDoVinculoParaEdicao(vinculo.Id, token);
                var canceladas = abertas.Where(parcela => parcela.Cancelar(hoje, dados.CancelarAtraso)).ToList();
                var cancelado = new CancelamentoDaSaida(canceladas.Count, canceladas.Sum(parcela => parcela.ValorOriginalEmCentavos));

                vinculo.Desligar(dados.Motivo, dados.Detalhe, DateTime.UtcNow);

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

    /// <inheritdoc />
    public Task<Result> Religar(Guid formaturaId, Guid usuarioId, Guid autorId, CancellationToken ct = default) =>
        unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var vinculo = await vinculoRepository.ObterParaEdicao(usuarioId, formaturaId, token);

                if (vinculo is null)
                    return Result.Falha(MembroNaoEncontrado);

                if (!vinculo.Desligado)
                    return Result.Falha(NaoDesligado);

                var motivo = vinculo.MotivoDoDesligamento;
                var desligadoEm = vinculo.DesligadoEm;

                vinculo.Religar();

                await eventos.Auditar(
                    NomesDeAuditoria.FormandoReligado,
                    autorId,
                    new
                    {
                        formaturaId,
                        usuarioId,
                        vinculoId = vinculo.Id,
                        desligadoEm,
                        motivo,
                    },
                    token
                );

                return Result.Ok();
            },
            ct
        );

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
