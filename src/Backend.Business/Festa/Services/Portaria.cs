using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Arquivos.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Formaturas.Interfaces;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Festa.Services;

/// <summary>
/// A porta da festa: a lista, a busca, a validação e a sincronização da lista sem rede.
/// </summary>
/// <remarks>
/// O que o check-in confere, e em que ordem (decisão 15): a assinatura, sem banco; a turma, pelo
/// filtro global; o evento da portaria; a janela de horário (P7); a revogação e a entrada anterior,
/// na <b>mesma</b> instrução que grava. As leituras antes do <c>INSERT</c> servem para dar a mensagem
/// certa; quem garante é a instrução.
/// </remarks>
/// <param name="convites">Convites e entradas.</param>
/// <param name="agenda">O evento de cada convite.</param>
/// <param name="formaturas">Nome da turma, para a lista em PDF.</param>
/// <param name="formaturaAtual">Turma da sessão.</param>
/// <param name="codigos">Assinatura e código digitado.</param>
/// <param name="eventos">Auditoria da entrada desfeita.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class Portaria(
    IConviteDoEventoRepository convites,
    IEventoDaTurmaRepository agenda,
    IFormaturaRepository formaturas,
    IFormaturaAtual formaturaAtual,
    CodigoDoConvite codigos,
    IEventoRepository eventos,
    IUnitOfWork unitOfWork,
    ILogger<Portaria> logger
) : IPortariaService
{
    private static readonly Erro EventoNaoEncontrado = Erro.NaoEncontrado("agenda.evento_nao_encontrado", "Evento não encontrado na agenda.");

    /// <inheritdoc />
    public async Task<Result<ListaDaPortaria>> Listar(Guid? eventoId, TipoDeEvento tipo, string? busca, CancellationToken ct = default)
    {
        if (await EventoDaPortaria(eventoId, tipo, ct) is not { } evento)
            return EventoNaoEncontrado;

        var linhas = NaLista(await convites.ListarNaPortaria(evento.Id, busca, ct), evento).Select(ParaPortaria).ToList();

        return new ListaDaPortaria(
            evento,
            linhas.Count(linha => linha.Situacao is not SituacaoNaPortaria.Revogado),
            linhas.Count(linha => linha.Situacao is SituacaoNaPortaria.Validado),
            linhas.Count(linha => linha.Situacao is SituacaoNaPortaria.SemTitular),
            evento.JanelaAberta(DateTime.UtcNow),
            DateTime.UtcNow,
            linhas
        );
    }

    /// <inheritdoc />
    public async Task<Result<ArquivoParaDownload>> ListaEmPdf(Guid? eventoId, TipoDeEvento tipo, CancellationToken ct = default)
    {
        if (await EventoDaPortaria(eventoId, tipo, ct) is not { } evento)
            return EventoNaoEncontrado;

        var linhas = NaLista(await convites.ListarNaPortaria(evento.Id, null, ct), evento).ToList();
        var turma = formaturaAtual.Id is { } id ? await formaturas.ObterNome(id, ct) ?? string.Empty : string.Empty;
        var pdf = ConviteEmPdf.Lista(turma, evento, linhas, ParaPortaria);

        return new ArquivoParaDownload(
            new MemoryStream(pdf),
            $"lista-da-portaria-{evento.Data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf",
            "application/pdf"
        );
    }

    /// <inheritdoc />
    public async Task<Result<ConsultaNaPortaria>> Consultar(string codigo, CancellationToken ct = default)
    {
        if (await Ler(codigo, ct) is not { } gravado)
            return ErrosDoConvite.NaoEncontrado;

        var evento = await Evento(gravado.Convite.EventoId, ct);

        return new ConsultaNaPortaria(ParaPortaria(gravado), evento, evento.JanelaAberta(DateTime.UtcNow));
    }

    /// <inheritdoc />
    /// <remarks>
    /// O toque duplo do mesário é o check-in concorrente: a segunda chamada devolve o "já validado"
    /// por ele mesmo, e a tela, que recebe o autor no corpo, trata como sucesso (decisão 15).
    /// </remarks>
    public async Task<Result<EntradaNaPortaria>> ValidarEntrada(string codigo, Guid? eventoId, Guid usuarioId, CancellationToken ct = default)
    {
        if (await Ler(codigo, ct) is not { } gravado)
            return ErrosDoConvite.NaoEncontrado;

        var convite = gravado.Convite;

        if (eventoId is { } daPortaria && daPortaria != convite.EventoId)
            return ErrosDoConvite.OutroEvento;

        var evento = await Evento(convite.EventoId, ct);
        var agora = DateTime.UtcNow;

        if (!evento.JanelaAberta(agora))
            return ForaDaJanela(evento, agora);

        if (!convite.Valido)
            return ErrosDoConvite.Revogado(convite.MotivoDaRevogacao);

        if (gravado.Preso)
            return ErrosDoConvite.Preso;

        if (!convite.Nomeado)
            return ErrosDoConvite.SemTitular;

        if (await convites.RegistrarEntrada(convite.Id, convite.EventoId, usuarioId, agora, null, ct))
        {
            logger.LogInformation("Entrada do convite {ConviteId} validada por {UsuarioId}.", convite.Id, usuarioId);

            return await convites.ObterEntradaAtiva(convite.Id, ct) is { } entrada ? entrada : ErrosDoConvite.NaoEncontrado;
        }

        return await PorQueNaoEntrou(convite.Codigo, ct);
    }

    /// <inheritdoc />
    public async Task<Result> Desfazer(Guid checkInId, Guid usuarioId, CancellationToken ct = default)
    {
        var naoEncontrada = Erro.NaoEncontrado("festa.entrada_nao_encontrada", "Entrada não encontrada.");

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var checkIn = await convites.ObterCheckInParaEdicao(checkInId, token);
                if (checkIn is null || await convites.Travar(checkIn.ConviteId, token) is not { } convite)
                    return Result.Falha(naoEncontrada);

                var evento = await Evento(convite.EventoId, token);
                var agora = DateTime.UtcNow;

                if (!evento.JanelaAberta(agora))
                    return Result.Falha(ForaDaJanela(evento, agora));

                if (!checkIn.Desfazer(usuarioId, agora))
                    return Result.Ok();

                await eventos.Auditar(
                    NomesDeAuditoria.EntradaDesfeita,
                    usuarioId,
                    new
                    {
                        formaturaId = convite.FormaturaId,
                        conviteId = convite.Id,
                        checkInId,
                        validadoEm = checkIn.ValidadoEm,
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);

                return Result.Ok();
            },
            ct
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uma entrada por vez, cada uma com a hora do aparelho. A que encontra o convite já dentro não cai
    /// no <c>ON CONFLICT DO NOTHING</c> em silêncio: vira a linha de tentativa repetida. Código que não
    /// existe aqui, revogado ou de outro evento conta como recusado — o aparelho mostrava uma lista
    /// velha, e é o risco aceito do modo degradado.
    /// </remarks>
    public async Task<Result<ResultadoDaSincronizacao>> Sincronizar(
        IReadOnlyList<EntradaSemRede> entradas,
        Guid usuarioId,
        CancellationToken ct = default
    )
    {
        if (entradas.Count > 2000)
            return Erro.Validacao("festa.sincronizacao_grande", "Envie no máximo 2.000 entradas por vez.", campo: "entradas");

        var agora = DateTime.UtcNow;
        var (validadas, repetidas, recusadas) = (0, 0, 0);

        foreach (var entrada in entradas)
        {
            if (await Ler(entrada.Codigo, ct) is not { Convite.Valido: true, Preso: false } gravado)
            {
                recusadas++;
                continue;
            }

            var marcada =
                entrada.ValidadoEm.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(entrada.ValidadoEm, DateTimeKind.Utc)
                    : entrada.ValidadoEm.ToUniversalTime();
            var quando = marcada > agora ? agora : marcada;
            var aparelho = entrada.Aparelho?.Trim() is { Length: > 0 } nome ? nome[..Math.Min(nome.Length, 60)] : null;

            if (await convites.RegistrarEntrada(gravado.Convite.Id, gravado.Convite.EventoId, usuarioId, quando, aparelho, ct))
            {
                validadas++;
                continue;
            }

            await convites.Adicionar(CheckIn.Repetida(gravado.Convite.Id, usuarioId, quando, aparelho, agora), ct);
            repetidas++;
        }

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation(
            "Sincronização da portaria por {UsuarioId}: {Validadas} validadas, {Repetidas} repetidas, {Recusadas} recusadas.",
            usuarioId,
            validadas,
            repetidas,
            recusadas
        );

        return new ResultadoDaSincronizacao(validadas, repetidas, recusadas);
    }

    /// <summary>O convite como a lista e a consulta da portaria o mostram, com o documento mascarado.</summary>
    /// <param name="gravado">Convite lido do banco.</param>
    public static ConviteNaPortaria ParaPortaria(ConviteGravadoNaPortaria gravado)
    {
        var convite = gravado.Convite;
        var situacao =
            !convite.Valido ? SituacaoNaPortaria.Revogado
            : gravado.Entrada is not null ? SituacaoNaPortaria.Validado
            : gravado.Preso ? SituacaoNaPortaria.Preso
            : !convite.Nomeado ? SituacaoNaPortaria.SemTitular
            : SituacaoNaPortaria.Valido;
        return new ConviteNaPortaria(
            convite.Id,
            convite.EventoId,
            convite.Codigo,
            convite.NomeDoConvidado,
            DocumentoDoConvidado.Mascarar(convite.TipoDoDocumento, convite.NumeroDoDocumento),
            gravado.ConvidadoDe,
            convite.Origem,
            situacao,
            convite.MotivoDaRevogacao,
            gravado.Entrada,
            gravado.EntrouSemRedeDuasVezes,
            convite.Observacoes
        );
    }

    /// <summary>
    /// A instrução não gravou: diz se foi porque o convite já entrou ou porque acabou de ser revogado.
    /// </summary>
    /// <remarks>Relê depois do <c>INSERT</c>: o estorno do mesmo segundo pode ter chegado entre a leitura e a instrução.</remarks>
    private async Task<Result<EntradaNaPortaria>> PorQueNaoEntrou(string codigo, CancellationToken ct)
    {
        if (await convites.ObterNaPortaria(codigo, ct) is not { } relido)
            return ErrosDoConvite.NaoEncontrado;

        if (!relido.Convite.Valido)
            return ErrosDoConvite.Revogado(relido.Convite.MotivoDaRevogacao);

        if (relido.Entrada is not { } anterior)
            return ErrosDoConvite.NaoEncontrado;

        var hora = DataUtils.ParaExibicao(anterior.ValidadoEm).ToString("HH'h'mm", CultureInfo.InvariantCulture);

        return new Erro("festa.ja_validado", $"Já validado às {hora} por {anterior.ValidadoPor}.", ETipoErro.Conflito) { Dados = anterior };
    }

    /// <summary>O 409 de fora do horário, dizendo quando a validação abre ou que ela já fechou (P7).</summary>
    private static Erro ForaDaJanela(EventoDoConvite evento, DateTime agora)
    {
        var abre = DataUtils.ParaExibicao(evento.JanelaAbreEmUtc);
        var mensagem =
            agora < evento.JanelaAbreEmUtc
                ? $"A validação abre às {abre.ToString("HH'h'mm", CultureInfo.InvariantCulture)} de {abre.ToString("dd/MM", CultureInfo.InvariantCulture)}."
                : "A validação deste evento já fechou.";

        return Erro.Conflito("festa.fora_da_janela", mensagem);
    }

    /// <summary>O convite pelo código digitado ou pelo token do QR; nulo se não existe nesta turma ou a assinatura não bate.</summary>
    private async Task<ConviteGravadoNaPortaria?> Ler(string texto, CancellationToken ct) =>
        codigos.ParaPortaria(texto) is { } codigo ? await convites.ObterNaPortaria(codigo, ct) : null;

    /// <summary>
    /// O que entra na lista: tudo, menos o convite de pacote que chegou sem nome ao fechamento (Sprint 30, P1).
    /// </summary>
    /// <remarks>
    /// O convite comprado sem titular continua — é pendência da Gestão (P5). O do pacote não: quem não
    /// nomeou até o fechamento não usou o convite, e ele deixa de ser linha na porta (P4).
    /// </remarks>
    private static IEnumerable<ConviteGravadoNaPortaria> NaLista(IEnumerable<ConviteGravadoNaPortaria> linhas, EventoDoConvite evento)
    {
        var fechada = !evento.ListaAberta(DateTime.UtcNow);

        return linhas.Where(linha => !(fechada && linha.Convite.Origem is OrigemDoConvite.Pacote && !linha.Convite.Nomeado));
    }

    /// <summary>O evento informado, ou o evento único do tipo.</summary>
    private async Task<EventoDoConvite?> EventoDaPortaria(Guid? eventoId, TipoDeEvento tipo, CancellationToken ct) =>
        (eventoId is { } id ? await agenda.Obter(id, ct) : await agenda.ObterDoTipo(tipo, ct)) is { } evento
            ? EmissaoDeConvites.ParaConvite(evento)
            : null;

    private async Task<EventoDoConvite> Evento(Guid eventoId, CancellationToken ct) =>
        EmissaoDeConvites.ParaConvite(
            await agenda.Obter(eventoId, ct) ?? throw new InvalidOperationException($"Convite sem o evento {eventoId} na agenda.")
        );
}
