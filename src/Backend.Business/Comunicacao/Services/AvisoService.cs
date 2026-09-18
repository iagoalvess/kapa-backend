using Backend.Business.Abstractions;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Services;
using Backend.Business.Formaturas.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Comunicacao.Services;

/// <summary>
/// O mural da turma: a comissão publica, corrige e exclui; todo membro lê.
/// </summary>
/// <remarks>
/// O papel de quem lê vem do vínculo gravado, a cada consulta — o mesmo que as políticas conferem —,
/// e não da claim do token: o membro rebaixado deixa de ver o interno na requisição seguinte.
/// </remarks>
/// <param name="avisoRepository">Avisos da turma.</param>
/// <param name="vinculoRepository">Papel de quem consulta.</param>
/// <param name="eventos">Auditoria da exclusão.</param>
/// <param name="validator">Forma do aviso.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class AvisoService(
    IAvisoRepository avisoRepository,
    IVinculoRepository vinculoRepository,
    IEventoRepository eventos,
    IValidator<DadosDoAviso> validator,
    IUnitOfWork unitOfWork,
    ILogger<AvisoService> logger
) : IAvisoService
{
    /// <summary>Evento da exclusão, com quem excluiu e o que foi excluído — lido pela trilha de auditoria (Sprint 14).</summary>
    public const string EventoDeExclusao = "comunicacao.aviso_excluido";

    /// <summary>Quantos avisos novos o balão do sino lista; o resto vira "e mais N" na contagem.</summary>
    private const int NovidadesNoSino = 5;

    private static readonly Erro NaoEncontrado = Erro.NaoEncontrado("comunicacao.aviso_nao_encontrado", "Aviso não encontrado.");

    private static readonly Erro LimiteDeFixados = Erro.Conflito(
        "comunicacao.limite_de_fixados",
        $"Já há {Aviso.LimiteDeFixados} avisos fixados. Desafixe um antes de fixar outro."
    );

    /// <inheritdoc />
    public async Task<Result<PaginaDe<AvisoResumo>>> Listar(
        Guid formaturaId,
        Guid usuarioId,
        PaginacaoRequest paginacao,
        FiltroDeAvisos filtro,
        CancellationToken ct = default
    )
    {
        var papel = await vinculoRepository.ObterPapelAtivo(usuarioId, formaturaId, ct);

        return Result.Ok(await avisoRepository.Listar(paginacao.Normalizar(), filtro, papel, ct));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Os números saem do mesmo recorte da lista: o formando lê "12 avisos" contando só os da turma,
    /// e "só comissão" vem zero para ele — a contagem não é a brecha por onde se descobre que existem
    /// avisos internos.
    /// </remarks>
    public async Task<Result<ResumoDoMural>> Resumir(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var papel = await vinculoRepository.ObterPapelAtivo(usuarioId, formaturaId, ct);

        return Result.Ok(await avisoRepository.Resumir(papel, ct));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Mesmo recorte de visibilidade do resto: o aviso interno não conta no sino de quem não o lê —
    /// um selo com "1" que não corresponde a linha nenhuma na lista é a mesma brecha, de outro jeito.
    /// <para>
    /// Sem vínculo ativo não há sino: quem foi desligado não recebe novidade de turma.
    /// </para>
    /// </remarks>
    public async Task<Result<NovidadesDoMural>> Novidades(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var vinculo = await vinculoRepository.ObterAtivoParaEdicao(usuarioId, formaturaId, ct);

        if (vinculo is null || vinculo.Desligado)
            return Result.Ok(NovidadesDoMural.Nenhuma);

        return Result.Ok(await avisoRepository.Novidades(vinculo.Papel, vinculo.MuralVistoEm, NovidadesNoSino, ct));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Quem chama é a tela do mural, ao abrir. Não é o sino que zera a si mesmo: abrir o balão é dar
    /// uma olhada, e o que marca como visto é a visita à lista, onde os avisos estão inteiros.
    /// </remarks>
    public async Task<Result> MarcarVisto(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var vinculo = await vinculoRepository.ObterAtivoParaEdicao(usuarioId, formaturaId, ct);

        if (vinculo is null)
            return Result.Ok();

        vinculo.VerMural(DateTime.UtcNow);

        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result<AvisoResumo>> Obter(Guid formaturaId, Guid usuarioId, Guid id, CancellationToken ct = default)
    {
        var papel = await vinculoRepository.ObterPapelAtivo(usuarioId, formaturaId, ct);

        return await avisoRepository.Obter(id, papel, ct) is { } aviso ? aviso : NaoEncontrado;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ponytail: a contagem dos fixados é otimista — dois fixados publicados no mesmo instante passam
    /// juntos e a turma fica com quatro, até alguém desafixar. Travar a formatura por isso serializaria
    /// o mural; se o limite precisar ser rígido, o lugar é um índice parcial com contador no banco.
    /// </remarks>
    public async Task<Result<AvisoResumo>> Publicar(Guid formaturaId, Guid usuarioId, DadosDoAviso dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<AvisoResumo>(validacao.Erros);

        if (dados.Fixado && await avisoRepository.ContarFixados(ct) >= Aviso.LimiteDeFixados)
            return LimiteDeFixados;

        var aviso = Aviso.Novo(dados, usuarioId);

        await avisoRepository.Adicionar(aviso, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Aviso {AvisoId} publicado por {UsuarioId} para {Visibilidade}.", aviso.Id, usuarioId, aviso.Visibilidade);

        return await Obter(formaturaId, usuarioId, aviso.Id, ct);
    }

    /// <inheritdoc />
    /// <remarks>Manter fixado o que já era fixado não conta de novo: só fixar um que não era esbarra no limite.</remarks>
    public async Task<Result<AvisoResumo>> Atualizar(Guid formaturaId, Guid usuarioId, Guid id, DadosDoAviso dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<AvisoResumo>(validacao.Erros);

        var aviso = await avisoRepository.ObterParaEdicao(id, ct);
        if (aviso is null)
            return NaoEncontrado;

        if (dados.Fixado && !aviso.Fixado && await avisoRepository.ContarFixados(ct) >= Aviso.LimiteDeFixados)
            return LimiteDeFixados;

        aviso.Aplicar(dados);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Aviso {AvisoId} corrigido por {UsuarioId}.", id, usuarioId);

        return await Obter(formaturaId, usuarioId, id, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A auditoria vai na mesma transação da exclusão, e não pela fila de analytics — que descarta
    /// quando enche: aviso apagado sem rastro de quem apagou é exatamente o que o critério de aceite
    /// proíbe. O título e o autor original vão junto, porque a linha deixa de existir.
    /// </remarks>
    public async Task<Result> Excluir(Guid formaturaId, Guid usuarioId, Guid id, CancellationToken ct = default)
    {
        var aviso = await avisoRepository.ObterParaEdicao(id, ct);
        if (aviso is null)
            return Result.Falha(NaoEncontrado);

        avisoRepository.Remover(aviso);
        await eventos.Auditar(
            EventoDeExclusao,
            usuarioId,
            new
            {
                formaturaId,
                avisoId = aviso.Id,
                aviso.Titulo,
                aviso.Visibilidade,
                aviso.PublicadoPorUsuarioId,
                aviso.PublicadoEm,
            },
            ct
        );
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Aviso {AvisoId} excluído por {UsuarioId}.", id, usuarioId);

        return Result.Ok();
    }
}
