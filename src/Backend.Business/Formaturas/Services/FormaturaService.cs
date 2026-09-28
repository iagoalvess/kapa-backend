using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using FluentValidation;

namespace Backend.Business.Formaturas.Services;

/// <summary>
/// Criação, seleção e ciclo de vida da formatura.
/// </summary>
/// <param name="formaturaRepository">A formatura em si.</param>
/// <param name="vinculoRepository">Vínculos do usuário.</param>
/// <param name="assinaturaRepository">Assinatura que ainda renova, e que impede o encerramento.</param>
/// <param name="provedor">PSP, para expirar o checkout de um rascunho descartado.</param>
/// <param name="authService">Emissão da sessão com a formatura escolhida.</param>
/// <param name="dadosValidator">Validador dos dados cadastrais.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class FormaturaService(
    IFormaturaRepository formaturaRepository,
    IVinculoRepository vinculoRepository,
    IAssinaturaRepository assinaturaRepository,
    IProvedorDeAssinatura provedor,
    IAuthService authService,
    IValidator<DadosDaFormatura> dadosValidator,
    IUnitOfWork unitOfWork
) : IFormaturaService
{
    /// <summary>Mesmo código da política <c>ExigeFormaturaAtiva</c>: para o cliente, é o mesmo caso.</summary>
    private static readonly Erro Inativa = Erro.Proibido("formatura.inativa", "Esta formatura está em modo leitura e não aceita alterações.");

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FormaturaDoUsuario>>> ListarMinhas(Guid usuarioId, CancellationToken ct = default) =>
        Result.Ok(await vinculoRepository.ListarDoUsuario(usuarioId, ct));

    /// <inheritdoc />
    /// <remarks>
    /// O vínculo é conferido <b>aqui</b>, antes de qualquer token existir. É o que garante que a
    /// API só assine uma formatura que o usuário comprovadamente acessa — e por isso a claim
    /// pode ser confiada dali para a frente.
    /// <para>
    /// O vínculo é o do titular: a turma de quem foi desligado continua no seletor dele, em leitura (P5).
    /// </para>
    /// </remarks>
    public async Task<Result<ParDeTokens>> Selecionar(
        Guid usuarioId,
        Guid formaturaId,
        string refreshTokenAtual,
        string? ipDeOrigem,
        CancellationToken ct = default
    )
    {
        var vinculo = await vinculoRepository.ObterDoTitular(usuarioId, formaturaId, ct);

        if (vinculo is null)
            return Erro.Proibido("formatura.sem_vinculo", "Você não participa desta formatura.");

        return await authService.EmitirSessaoDeFormatura(
            usuarioId,
            formaturaId,
            vinculo.Papel,
            refreshTokenAtual,
            ipDeOrigem,
            vinculo.DesligadoEm,
            ct
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Formatura, vínculo de Presidente e sessão nova nascem na <b>mesma transação</b>: turma sem
    /// presidente é turma que ninguém administra, e sessão que falha depois da turma criada deixaria
    /// o usuário do lado de fora de algo que é dele. Qualquer passo que falhe desfaz os anteriores.
    /// <para>
    /// Um rascunho por usuário. "Criar formatura" é um botão que cria uma formatura; sem o limite,
    /// um clique duplo ou um teste de carga enche a base de turmas fantasma. A checagem aqui dá o
    /// código de erro; o índice único parcial em <c>FormaturaMapping</c> fecha a corrida.
    /// </para>
    /// </remarks>
    public async Task<Result<ParDeTokens>> Criar(
        Guid usuarioId,
        DadosDaFormatura dados,
        string refreshTokenAtual,
        string? ipDeOrigem,
        CancellationToken ct = default
    )
    {
        var validacao = dadosValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ParDeTokens>(validacao.Erros);

        if (await formaturaRepository.ExisteGratuitaCriadaPorDeTodasAsFormaturas(usuarioId, ct))
            return Erro.Conflito(
                "formatura.gratuita_pendente",
                "Você já tem uma turma no plano gratuito. Contrate ou descarte essa antes de criar outra."
            );

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var formatura = new Formatura { CriadoPorUsuarioId = usuarioId };
                Preencher(formatura, dados);
                formatura.NascerNoGratuito();

                await formaturaRepository.Adicionar(formatura, token);
                await vinculoRepository.Adicionar(
                    new VinculoDeFormatura
                    {
                        UsuarioId = usuarioId,
                        FormaturaId = formatura.Id,
                        Papel = PapelNaFormatura.Presidente,
                    },
                    token
                );

                return await authService.EmitirSessaoDeFormatura(
                    usuarioId,
                    formatura.Id,
                    PapelNaFormatura.Presidente,
                    refreshTokenAtual,
                    ipDeOrigem,
                    ct: token
                );
            },
            ct
        );
    }

    /// <inheritdoc />
    public async Task<Result<FormaturaDetalhe>> ObterAtual(Guid formaturaId, CancellationToken ct = default) =>
        await formaturaRepository.ObterDetalheDeTodasAsFormaturas(formaturaId, ct) is { } detalhe ? detalhe : ErrosDeFormatura.FormaturaNaoEncontrada;

    /// <inheritdoc />
    /// <remarks>Só a turma ativa edita. Suspensa, encerrada e descartada, não.</remarks>
    public async Task<Result<FormaturaDetalhe>> Atualizar(Guid formaturaId, DadosDaFormatura dados, CancellationToken ct = default)
    {
        var validacao = dadosValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<FormaturaDetalhe>(validacao.Erros);

        var formatura = await formaturaRepository.ObterParaEdicao(formaturaId, ct);

        if (formatura is null)
            return ErrosDeFormatura.FormaturaNaoEncontrada;

        if (!formatura.AceitaEdicao)
            return Inativa;

        Preencher(formatura, dados);
        await unitOfWork.SalvarAsync(ct);

        return await ObterAtual(formaturaId, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Encerrar não apaga nada: prestação de contas é consultada meses depois da festa.
    /// <para>
    /// Assinatura que ainda renova recusa com <c>formatura.assinatura_ativa</c>: o PSP seguiria
    /// cobrando uma turma encerrada, e depois de encerrada nem o cancelamento passa mais (é escrita).
    /// O Presidente cancela a renovação antes; a vigência paga continua valendo até o fim.
    /// </para>
    /// <para>
    /// ponytail: a regra "sem parcela em aberto" (<c>formatura.pendencias_em_aberto</c>) entra
    /// junto da entidade de parcela, na Sprint 6 — hoje não existe parcela para estar em aberto.
    /// </para>
    /// </remarks>
    public async Task<Result> Encerrar(Guid formaturaId, CancellationToken ct = default)
    {
        var formatura = await formaturaRepository.ObterParaEdicao(formaturaId, ct);

        if (formatura is null)
            return Result.Falha(ErrosDeFormatura.FormaturaNaoEncontrada);

        if (await assinaturaRepository.ObterDetalheDaMaisRecente(ct) is { Status: StatusDaAssinatura.Ativa })
            return Result.Falha(Erro.Conflito("formatura.assinatura_ativa", "Cancele a renovação da assinatura antes de encerrar a formatura."));

        var transicao = formatura.Transicionar(StatusDaFormatura.Encerrada);
        if (transicao.Falhou)
            return transicao;

        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Só a turma que <b>nunca contratou</b> — a que está no gratuito. Turma que já pagou encerra,
    /// não descarta, e é por isso que a condição é a ausência de assinatura e não o status: desde
    /// 18/09/2026 toda turma nasce <c>Ativa</c>, então o status sozinho não distingue mais as duas.
    /// <para>
    /// Sem isso o gratuito seria um beco sem saída: quem criasse a turma com o nome errado não
    /// conseguiria descartá-la nem criar outra, porque só se tem uma turma não paga por conta.
    /// </para>
    /// <para>
    /// O checkout em aberto é expirado no provedor <b>antes</b> de gravar: se o PSP não confirmar,
    /// nada muda, e ninguém paga depois por uma turma que não existe mais.
    /// ponytail: um pagamento que o PSP aprovar entre o clique e o cancelamento chega por webhook e
    /// só vira log (a transição Descartada → Ativa é recusada) — estorno manual. Janela de segundos.
    /// </para>
    /// </remarks>
    public async Task<Result> Descartar(Guid formaturaId, CancellationToken ct = default)
    {
        var formatura = await formaturaRepository.ObterParaEdicao(formaturaId, ct);

        if (formatura is null)
            return Result.Falha(ErrosDeFormatura.FormaturaNaoEncontrada);

        if (await assinaturaRepository.ExisteAlgumaDeTodasAsFormaturas(formaturaId, ct))
            return Result.Falha(Erro.Conflito("formatura.ja_contratada", "Esta turma já contratou um plano. Encerre a turma em vez de descartá-la."));

        var transicao = formatura.Transicionar(StatusDaFormatura.Descartada);
        if (transicao.Falhou)
            return transicao;

        if (await assinaturaRepository.ObterMaisRecenteParaEdicao(ct) is { Status: StatusDaAssinatura.Pendente, IdExterno: { } sessao })
        {
            var expirada = await provedor.Cancelar(sessao, ct);
            if (expirada.Falhou)
                return expirada;
        }

        foreach (var vinculo in await vinculoRepository.ListarAtivosParaEdicao(formaturaId, ct))
            vinculo.Ativo = false;

        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <summary>Copia os dados cadastrais para a entidade, já aparados.</summary>
    /// <param name="formatura">Formatura a preencher.</param>
    /// <param name="dados">Dados validados.</param>
    private static void Preencher(Formatura formatura, DadosDaFormatura dados)
    {
        formatura.Nome = dados.Nome.Trim();
        formatura.Instituicao = dados.Instituicao.Trim();
        formatura.Curso = dados.Curso.Trim();
        formatura.Ano = dados.Ano;
        formatura.Semestre = dados.Semestre;
    }
}
