using Backend.Business.Abstractions;
using Backend.Business.Enquetes.Interfaces;
using Backend.Business.Enquetes.Models;
using FluentValidation;

namespace Backend.Business.Enquetes.Services;

/// <summary>Regras das consultas, com uma trava transacional comum a toda mutação.</summary>
public sealed class EnqueteService(IEnqueteRepository enquetes, IValidator<DadosDaEnquete> validator, IUnitOfWork unitOfWork) : IEnqueteService
{
    private static readonly Erro NaoEncontrada = Erro.NaoEncontrado("enquete.nao_encontrada", "Enquete não encontrada.");
    private static readonly Erro Encerrada = Erro.Conflito("enquete.encerrada", "Esta enquete já foi encerrada ou cancelada.");
    private static readonly Erro SemVinculo = Erro.Proibido("formatura.sem_vinculo", "Você não tem vínculo ativo com esta turma.");

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EnqueteResumo>>> Listar(Guid usuarioId, CancellationToken ct = default)
    {
        var membro = await enquetes.ObterMembro(usuarioId, ct);
        if (membro is null)
            return SemVinculo;
        var lista = await enquetes.Listar(membro.VinculoId, ct);
        return Result.Ok<IReadOnlyList<EnqueteResumo>>(
            lista.Select(x => Resumir(x, membro)).OrderBy(x => x.Situacao != "Aberta").ThenBy(x => x.EncerraEm).ToList()
        );
    }

    /// <inheritdoc />
    public async Task<Result<EnqueteDetalhe>> Obter(Guid id, Guid usuarioId, CancellationToken ct = default)
    {
        var membro = await enquetes.ObterMembro(usuarioId, ct);
        if (membro is null)
            return SemVinculo;
        var leitura = await enquetes.Obter(id, membro.VinculoId, ct);
        if (leitura is null)
            return NaoEncontrada;
        var resumo = Resumir(leitura, membro);
        var mostrarResultado = resumo.Situacao != "Cancelada" || membro.Gestao;
        var opcoes = await enquetes.Resultados(id, leitura.Respondentes, ct);
        if (!mostrarResultado)
            opcoes = opcoes.Select(o => o with { Respostas = 0, Percentual = 0 }).ToList();
        return new EnqueteDetalhe(resumo, opcoes, membro.Gestao ? await enquetes.Participantes(id, ct) : null);
    }

    /// <inheritdoc />
    public async Task<Result<EnqueteDetalhe>> Criar(DadosDaEnquete dados, Guid usuarioId, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<EnqueteDetalhe>(validacao.Erros);
        var agora = DateTime.UtcNow;
        if (dados.EncerraEm < agora.AddHours(1) || dados.EncerraEm > agora.AddDays(60))
            return PrazoInvalido();
        var enquete = Enquete.Nova(dados, usuarioId, await enquetes.ContarAptos(ct));
        await enquetes.Adicionar(enquete, ct);
        await unitOfWork.SalvarAsync(ct);
        return await Obter(enquete.Id, usuarioId, ct);
    }

    /// <inheritdoc />
    public async Task<Result<EnqueteDetalhe>> Atualizar(Guid id, DadosDaEnquete dados, Guid usuarioId, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<EnqueteDetalhe>(validacao.Erros);
        var resultado = await Mutar(
            id,
            async (enquete, token) =>
            {
                if (dados.EncerraEm <= DateTime.UtcNow || dados.EncerraEm > enquete.CriadoEm.AddDays(60))
                    return Result.Falha(PrazoInvalido());
                var regrasMudaram =
                    enquete.Pergunta != dados.Pergunta.Trim()
                    || enquete.Tipo != dados.Tipo
                    || enquete.MaximoDeEscolhas != dados.MaximoDeEscolhas
                    || enquete.QuorumMinimoPercentual != dados.QuorumMinimoPercentual
                    || !enquete.Opcoes.OrderBy(o => o.Ordem).Select(o => o.Texto).SequenceEqual(dados.Opcoes.Select(o => o.Trim()));
                if (enquete.PrimeiraRespostaEm is not null && (regrasMudaram || dados.EncerraEm < enquete.EncerraEm))
                    return Result.Falha(
                        Erro.Conflito(
                            "enquete.opcoes_travadas",
                            "Depois da primeira resposta, apenas a descrição e o adiamento do prazo podem mudar."
                        )
                    );
                var antigas = enquete.Opcoes.ToList();
                enquete.Aplicar(dados);
                if (antigas.Count > 0 && !ReferenceEquals(antigas[0], enquete.Opcoes[0]))
                {
                    enquetes.RemoverOpcoes(antigas);
                    enquetes.AdicionarOpcoes(enquete.Opcoes);
                }
                await Task.CompletedTask;
                return Result.Ok();
            },
            ct
        );
        return resultado.Falhou ? Result.Falha<EnqueteDetalhe>(resultado.Erros) : await Obter(id, usuarioId, ct);
    }

    /// <inheritdoc />
    public Task<Result> Encerrar(Guid id, CancellationToken ct = default) =>
        Mutar(
            id,
            (e, _) =>
            {
                e.Encerrar(DateTime.UtcNow);
                return Task.FromResult(Result.Ok());
            },
            ct
        );

    /// <inheritdoc />
    public Task<Result> Cancelar(Guid id, CancellationToken ct = default) =>
        unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var enquete = await enquetes.Travar(id, token);
                if (enquete is null)
                    return Result.Falha(NaoEncontrada);
                enquete.Cancelar(DateTime.UtcNow);
                return Result.Ok();
            },
            ct
        );

    /// <inheritdoc />
    public Task<Result> Responder(Guid id, Guid usuarioId, IReadOnlyList<Guid> opcoes, CancellationToken ct = default) =>
        Mutar(
            id,
            async (enquete, token) =>
            {
                var membro = await enquetes.ObterMembro(usuarioId, token);
                if (Conferir(membro) is { } erro)
                    return Result.Falha(erro);
                if (opcoes.Count == 0 || opcoes.Distinct().Count() != opcoes.Count)
                    return Result.Falha(Erro.Validacao("enquete.resposta_invalida", "Escolha opções diferentes para responder."));
                if (opcoes.Any(idOpcao => enquete.Opcoes.All(o => o.Id != idOpcao)))
                    return Result.Falha(Erro.Validacao("enquete.opcao_de_outra_enquete", "Escolha somente opções desta enquete."));
                if (opcoes.Count > (enquete.Tipo == TipoDeEnquete.UnicaEscolha ? 1 : enquete.MaximoDeEscolhas!.Value))
                    return Result.Falha(Erro.Validacao("enquete.escolhas_demais", "Você marcou mais opções que o limite da enquete."));
                var resposta = await enquetes.ObterResposta(id, membro!.VinculoId, token);
                if (resposta is null)
                {
                    resposta = new RespostaDaEnquete { EnqueteId = id, VinculoId = membro.VinculoId };
                    await enquetes.AdicionarResposta(resposta, token);
                }
                else
                    enquetes.RemoverEscolhas(resposta.Escolhas.ToList());
                resposta.RespondidaEm = DateTime.UtcNow;
                resposta.Escolhas = opcoes.Select(opcaoId => new EscolhaDaResposta { RespostaId = resposta.Id, OpcaoId = opcaoId }).ToList();
                enquetes.AdicionarEscolhas(resposta.Escolhas);
                enquete.RegistrarResposta(resposta.RespondidaEm);
                return Result.Ok();
            },
            ct
        );

    /// <inheritdoc />
    public Task<Result> Retirar(Guid id, Guid usuarioId, CancellationToken ct = default) =>
        Mutar(
            id,
            async (_, token) =>
            {
                var membro = await enquetes.ObterMembro(usuarioId, token);
                if (membro is null)
                    return Result.Falha(SemVinculo);
                var resposta = await enquetes.ObterResposta(id, membro.VinculoId, token);
                if (resposta is not null)
                    enquetes.RemoverResposta(resposta);
                return Result.Ok();
            },
            ct
        );

    /// <summary>A mesma linha protege voto, edição, retirada e encerramento.</summary>
    private Task<Result> Mutar(Guid id, Func<Enquete, CancellationToken, Task<Result>> mutacao, CancellationToken ct) =>
        unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var enquete = await enquetes.Travar(id, token);
                if (enquete is null)
                    return Result.Falha(NaoEncontrada);
                if (enquete.Situacao(DateTime.UtcNow) != "Aberta")
                    return Result.Falha(Encerrada);
                return await mutacao(enquete, token);
            },
            ct
        );

    /// <summary>Requisitos de votação iguais para formando e comissão.</summary>
    private static Erro? Conferir(MembroParaEnquete? membro) =>
        membro is null ? SemVinculo
        : !membro.EmailConfirmado ? Erro.Proibido("enquete.email_nao_confirmado", "Confirme seu e-mail para responder.")
        : !membro.Aderiu ? Erro.Proibido("adesao.obrigatoria", "Aceite o termo da turma para responder.")
        : null;

    /// <summary>Resumo sem escolhas de terceiros e com quórum contado por pessoa.</summary>
    private static EnqueteResumo Resumir(EnqueteComContagem leitura, MembroParaEnquete membro)
    {
        var e = leitura.Enquete;
        var situacao = e.Situacao(DateTime.UtcNow);
        var erro = Conferir(membro);
        var total = Math.Max(e.TotalAptosNaPublicacao, leitura.Respondentes);
        var respostas = situacao == "Cancelada" && !membro.Gestao ? 0 : leitura.Respondentes;
        return new EnqueteResumo(
            e.Id,
            e.Pergunta,
            e.Descricao,
            e.Tipo,
            e.MaximoDeEscolhas,
            e.QuorumMinimoPercentual,
            total,
            respostas,
            total > 0 && respostas * 100L >= total * (long)e.QuorumMinimoPercentual,
            situacao,
            e.CriadoEm,
            e.EncerraEm,
            e.EncerradaEm,
            e.CanceladaEm,
            e.PrimeiraRespostaEm is not null,
            leitura.MinhaResposta,
            situacao == "Aberta" && erro is null,
            erro?.Mensagem
        );
    }

    /// <summary>Prazo fora da janela aceita pela primeira versão.</summary>
    private static Erro PrazoInvalido() => Erro.Validacao("enquete.prazo_invalido", "O prazo deve ficar entre 1 hora e 60 dias da publicação.");
}
