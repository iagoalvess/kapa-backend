using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Festa.Services;

/// <summary>
/// A portaria terceirizada: a Gestão libera quem fica na porta por um link, sem conta e sem vaga (15/10/2026).
/// </summary>
/// <remarks>
/// O link vale uma vez. Quem o abre ganha uma credencial presa ao evento, que expira no fim da janela de
/// validação e é conferida no banco a cada requisição — cortar o acesso vale na hora, não quando o token vencer.
/// </remarks>
/// <param name="acessos">Acessos da portaria.</param>
/// <param name="agenda">O evento de cada acesso.</param>
/// <param name="escopo">A turma do link, apontada na ativação, que chega sem sessão.</param>
/// <param name="emissor">Assina a credencial.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class AcessosDaPortaria(
    IAcessoDaPortariaRepository acessos,
    IEventoDaTurmaRepository agenda,
    FormaturaDoProcessamento escopo,
    IEmissorDaPortaria emissor,
    IUnitOfWork unitOfWork,
    ILogger<AcessosDaPortaria> logger
) : IAcessosDaPortariaService
{
    private const int TamanhoDoNome = 60;

    private static readonly Erro EventoNaoEncontrado = Erro.NaoEncontrado("agenda.evento_nao_encontrado", "Evento não encontrado na agenda.");

    private static readonly Erro AcessoNaoEncontrado = Erro.NaoEncontrado("festa.acesso_nao_encontrado", "Acesso da portaria não encontrado.");

    private static readonly Erro LinkInvalido = Erro.NaoEncontrado(
        "festa.link_da_portaria_invalido",
        "Este link não vale mais. Peça um link novo à comissão da formatura."
    );

    private static readonly Erro EventoEncerrado = Erro.Conflito("festa.evento_encerrado", "A validação deste evento já fechou.");

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AcessoNaLista>>> Listar(Guid eventoId, CancellationToken ct = default)
    {
        if (await Evento(eventoId, ct) is not { } evento)
            return EventoNaoEncontrado;

        if (DateTime.UtcNow > evento.JanelaFechaEmUtc)
            return Array.Empty<AcessoNaLista>();

        return (await acessos.Listar(eventoId, ct)).Select(NaLista).ToList();
    }

    /// <inheritdoc />
    public async Task<Result<AcessoNaLista>> Liberar(Guid eventoId, string? nome, Guid usuarioId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nome))
            return Erro.Validacao("festa.nome_obrigatorio", "Informe o nome de quem vai ficar na porta.", campo: "nome");

        if (nome.Trim().Length > TamanhoDoNome)
            return Erro.Validacao("festa.nome_longo", $"Use até {TamanhoDoNome} caracteres.", campo: "nome");

        if (await Evento(eventoId, ct) is not { } evento)
            return EventoNaoEncontrado;

        var agora = DateTime.UtcNow;

        if (agora > evento.JanelaFechaEmUtc)
            return EventoEncerrado;

        var acesso = AcessoDaPortaria.Novo(eventoId, nome, usuarioId);
        await acessos.Adicionar(acesso, ct);
        await unitOfWork.SalvarAsync(ct);

        return NaLista(acesso);
    }

    /// <inheritdoc />
    public async Task<Result<AcessoNaLista>> NovoLink(Guid id, Guid usuarioId, CancellationToken ct = default)
    {
        if (await acessos.ObterParaEdicao(id, ct) is not { } antigo)
            return AcessoNaoEncontrado;

        var evento = await Evento(antigo.EventoId, ct) ?? throw new InvalidOperationException($"Acesso sem o evento {antigo.EventoId}.");
        var agora = DateTime.UtcNow;

        if (agora > evento.JanelaFechaEmUtc)
            return EventoEncerrado;

        antigo.Revogar(agora);

        var novo = AcessoDaPortaria.Novo(antigo.EventoId, antigo.Nome, usuarioId);
        await acessos.Adicionar(novo, ct);
        await unitOfWork.SalvarAsync(ct);

        return NaLista(novo);
    }

    /// <inheritdoc />
    public async Task<Result> Revogar(Guid id, CancellationToken ct = default)
    {
        if (await acessos.ObterParaEdicao(id, ct) is not { } acesso)
            return Result.Falha(AcessoNaoEncontrado);

        acesso.Revogar(DateTime.UtcNow);
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Dois celulares abrindo o mesmo link no mesmo segundo: o <c>xmin</c> da linha deixa um só gravar, e o outro
    /// recebe o 409 de concorrência.
    /// </remarks>
    public async Task<Result<CredencialDaPortaria>> Ativar(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || await acessos.ObterPeloTokenDeTodasAsFormaturas(token.Trim(), ct) is not { } acesso)
            return LinkInvalido;

        escopo.Apontar(acesso.FormaturaId);

        var agora = DateTime.UtcNow;

        if (await Evento(acesso.EventoId, ct) is not { } evento || agora > evento.JanelaFechaEmUtc)
            return LinkInvalido;

        acesso.Ativar(agora);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Acesso da portaria {AcessoId} ativado no evento {EventoId}.", acesso.Id, acesso.EventoId);

        var credencial = emissor.Emitir(acesso.Id, acesso.Nome, acesso.FormaturaId, acesso.EventoId, evento.JanelaFechaEmUtc);

        return new CredencialDaPortaria(acesso.Id, acesso.Nome, credencial, evento.JanelaFechaEmUtc, evento);
    }

    private static AcessoNaLista NaLista(AcessoDaPortaria acesso)
    {
        var situacao = acesso.AtivadoEm is not null ? SituacaoDoAcesso.Ativo : SituacaoDoAcesso.Aguardando;

        return new AcessoNaLista(
            acesso.Id,
            acesso.Nome,
            situacao,
            situacao is SituacaoDoAcesso.Aguardando ? acesso.Token : null,
            acesso.AtivadoEm,
            acesso.CriadoEm
        );
    }

    private async Task<EventoDoConvite?> Evento(Guid eventoId, CancellationToken ct) =>
        await agenda.Obter(eventoId, ct) is { } evento ? EmissaoDeConvites.ParaConvite(evento) : null;
}
