using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Festa.Services;

/// <summary>
/// O convite da festa do lado de quem o tem: ver, nomear, abrir e imprimir. O que a Gestão emite à mão é o
/// <see cref="GestaoDeConvitesService"/>.
/// </summary>
/// <remarks>
/// Escrita de convite existente sempre trava a linha antes (decisão 15): a transferência e o estorno
/// do mesmo segundo não podem deixar dois convites valendo na mesma posição. Troca de código é
/// revogar, salvar e só então incluir o substituto — o índice único parcial aceita um válido por
/// posição, e a ordem das instruções dentro de um <c>SaveChanges</c> não é garantida.
/// <para>Log só com ids: nome e documento de convidado são dado de terceiro.</para>
/// </remarks>
/// <param name="convites">Convites da turma.</param>
/// <param name="pedidos">Pedidos de convite extra e o item que a cortesia ocupa.</param>
/// <param name="perfis">Vínculo e papel de quem chama.</param>
/// <param name="agenda">O evento de cada convite.</param>
/// <param name="emissao">Emissão idempotente e prefixo dos códigos.</param>
/// <param name="codigos">Sorteio e assinatura.</param>
/// <param name="emails">Convite e cancelamento para o convidado.</param>
/// <param name="eventos">Auditoria.</param>
/// <param name="validator">Forma do titular.</param>
/// <param name="formaturaAtual">Turma da sessão, para o PDF que vai anexo ao e-mail do convidado.</param>
/// <param name="formaturas">Nome e instituição da turma, para o PDF que vai anexo ao e-mail do convidado.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ConviteDoEventoService(
    IConviteDoEventoRepository convites,
    IPedidoRepository pedidos,
    IPerfilRepository perfis,
    IEventoDaTurmaRepository agenda,
    EmissaoDeConvites emissao,
    CodigoDoConvite codigos,
    EmailsDoConvite emails,
    IEventoRepository eventos,
    IValidator<DadosDoConvidado> validator,
    IFormaturaAtual formaturaAtual,
    IFormaturaRepository formaturas,
    IUnitOfWork unitOfWork,
    ILogger<ConviteDoEventoService> logger
) : IConviteDoEventoService
{
    private static readonly Erro SemVinculo = Erro.NaoEncontrado("formatura.vinculo_nao_encontrado", "Você não é membro ativo desta turma.");

    /// <inheritdoc />
    /// <remarks>
    /// "Aguardando pagamento" são as unidades pedidas que ainda não viraram convite: é a consequência
    /// da P2 escrita na tela — quem parcela em 3× só recebe o convite depois da terceira.
    /// </remarks>
    public async Task<Result<MeusConvites>> ListarMeus(Guid formaturaId, Guid usuarioId, TipoDeEvento tipo, CancellationToken ct = default)
    {
        if (await perfis.ObterMembro(formaturaId, usuarioId, ct) is not { } membro)
            return SemVinculo;

        var pedidas =
            tipo is TipoDeEvento.Festa
                ? (await pedidos.ListarDoVinculo(membro.VinculoId, ct))
                    .Where(pedido => pedido.Tipo == TipoDeCobranca.ConviteExtra && pedido.Status == StatusDoPedido.Confirmado)
                    .Sum(pedido => pedido.Quantidade)
                : 0;

        if (await agenda.ObterDoTipo(tipo, ct) is not { } doTipo)
            return new MeusConvites(null, null, false, [], pedidas);

        var evento = EmissaoDeConvites.ParaConvite(doTipo);
        var meus = await convites.ListarDoVinculo(membro.VinculoId, doTipo.Id, ct);
        var comprados = meus.Count(linha => linha.Convite.PedidoId is not null);

        return new MeusConvites(
            evento,
            evento.FechamentoEmUtc,
            evento.ListaAberta(DateTime.UtcNow),
            [.. meus.Select(linha => ParaMeu(linha.Convite, linha.ValidadoEm))],
            Math.Max(0, pedidas - comprados)
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Convite "a definir" responde o mesmo 404 de um código que não existe: é vaga paga, não ingresso
    /// (revisão da P1 em 24/09/2026). A primeira nomeação não troca o código, e a página aberta antes
    /// dela faria o link que já circulou valer para quem fosse nomeado depois.
    /// </remarks>
    public async Task<Result<ConvitePublico>> AbrirPublico(string token, CancellationToken ct = default)
    {
        if (codigos.Conferir(token) is not { } codigo)
            return ErrosDoConvite.NaoEncontrado;

        if (await convites.ObterPublicoDeTodasAsFormaturas(codigo, ct) is not { NomeDoConvidado: { } nome } gravado)
            return ErrosDoConvite.NaoEncontrado;

        return new ConvitePublico(
            gravado.Turma,
            gravado.Instituicao,
            gravado.Evento,
            gravado.Codigo,
            codigos.Token(gravado.Codigo),
            nome,
            DocumentoDoConvidado.Mascarar(gravado.TipoDoDocumento, gravado.NumeroDoDocumento)
        );
    }

    /// <inheritdoc />
    public async Task<Result<ArquivoParaDownload>> GerarPdf(string token, CancellationToken ct = default)
    {
        var convite = await AbrirPublico(token, ct);
        if (convite.Falhou)
            return Result.Falha<ArquivoParaDownload>(convite.Erros);

        var pdf = ConviteEmPdf.Gerar(convite.Valor, emails.Link(convite.Valor.Token));

        return new ArquivoParaDownload(new MemoryStream(pdf), $"convite-{convite.Valor.Codigo}.pdf", "application/pdf");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Quem é da Gestão sai do vínculo gravado, e não da claim — rebaixar alguém vale na requisição
    /// seguinte, como no cancelamento do pedido. Convite de outro, para quem não é da Gestão, responde
    /// 404, nunca 403.
    /// </remarks>
    public async Task<Result<MeuConvite>> NomearConvidado(
        Guid conviteId,
        Guid formaturaId,
        Guid usuarioId,
        DadosDoConvidado dados,
        CancellationToken ct = default
    )
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<MeuConvite>(validacao.Erros);

        if (await perfis.ObterMembro(formaturaId, usuarioId, ct) is not { } membro)
            return SemVinculo;

        var daGestao = PapelNaFormatura.Gestao.Contains(membro.Papel);

        return await Nomear(
            conviteId,
            dados,
            daGestao,
            convite => daGestao || convite.VinculoId == membro.VinculoId,
            (convite, listaAberta) =>
                daGestao && (!listaAberta || convite.VinculoId != membro.VinculoId) ? new AutoriaDaNomeacao(formaturaId, usuarioId) : null,
            ct
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// As mesmas regras do dono formando: até o fechamento da lista, e a troca de titular revoga o código.
    /// Convite de outra compra responde 404, como o de outro formando.
    /// </remarks>
    public async Task<Result<MeuConvite>> NomearDaCompra(Guid conviteId, Guid compraId, DadosDoConvidado dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<MeuConvite>(validacao.Erros);

        return await Nomear(conviteId, dados, daGestao: false, convite => convite.CompraId == compraId, (_, _) => null, ct);
    }

    /// <summary>
    /// O miolo da nomeação, para os dois donos de convite — o formando (ou a Gestão por ele) e o comprador da loja.
    /// </summary>
    /// <param name="conviteId">Convite.</param>
    /// <param name="dados">Titular já validado.</param>
    /// <param name="daGestao">Se quem edita é da Gestão — passa do fechamento da lista.</param>
    /// <param name="ehDono">Se quem edita pode mexer neste convite.</param>
    /// <param name="auditoria">A autoria a auditar, ou nulo quando a edição não se audita.</param>
    private async Task<Result<MeuConvite>> Nomear(
        Guid conviteId,
        DadosDoConvidado dados,
        bool daGestao,
        Func<ConviteDoEvento, bool> ehDono,
        Func<ConviteDoEvento, bool, AutoriaDaNomeacao?> auditoria,
        CancellationToken ct
    )
    {
        var convidado = Normalizar(dados);
        var prefixo = await emissao.Prefixo(ct);

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var convite = await convites.Travar(conviteId, token);
                if (convite is not { Valido: true } || !ehDono(convite))
                    return Result.Falha<MeuConvite>(ErrosDoConvite.NaoEncontrado);

                var evento = await Evento(convite.EventoId, token);
                var listaAberta = evento.ListaAberta(DateTime.UtcNow);
                var titular = MantendoODocumento(convite, convidado);

                if (!listaAberta && !daGestao)
                    return Result.Falha<MeuConvite>(
                        Erro.Conflito(
                            "festa.lista_fechada",
                            "A lista de convidados fechou 24 horas antes do evento. Agora só a comissão altera o nome."
                        )
                    );

                if (!listaAberta && titular.NumeroDoDocumento is null)
                    return Result.Falha<MeuConvite>(
                        Erro.Validacao(
                            "festa.documento_obrigatorio",
                            "Depois do fechamento da lista, o convite precisa de nome e documento.",
                            campo: "numero_do_documento"
                        )
                    );

                var transferencia = convite.TrocaDeTitular(titular);
                var emailAnterior = convite.EmailDoConvidado;
                var final = convite;

                if (transferencia)
                {
                    convite.Revogar("transferido para outro convidado", DateTime.UtcNow);
                    await unitOfWork.SalvarAsync(token);

                    final = convite.Substituto(CodigoDoConvite.Sortear(prefixo), titular);
                    await convites.Adicionar(final, token);

                    if (emailAnterior is not null)
                        await emails.Cancelado(emailAnterior, evento, ct: token);
                }
                else
                {
                    convite.Aplicar(titular);
                }

                if (
                    final.EmailDoConvidado is { } email
                    && (transferencia || !string.Equals(email, emailAnterior, StringComparison.OrdinalIgnoreCase))
                )
                    await emails.Enviado(email, await ParaPublico(final, evento, token), token);

                if (auditoria(convite, listaAberta) is { } autoria)
                    await eventos.Auditar(
                        NomesDeAuditoria.ConvidadoAlterado,
                        autoria.AutorId,
                        new
                        {
                            autoria.FormaturaId,
                            conviteId = convite.Id,
                            novoConviteId = transferencia ? final.Id : (Guid?)null,
                            transferencia,
                            depoisDoFechamento = !listaAberta,
                        },
                        token
                    );

                await unitOfWork.SalvarAsync(token);

                logger.LogInformation("Convite {ConviteId} nomeado (transferência: {Transferencia}).", final.Id, transferencia);

                return Result.Ok(ParaMeu(final, null));
            },
            ct
        );
    }

    /// <summary>O convite como a página pública o mostra, com a turma e a assinatura deste service.</summary>
    /// <param name="convite">Convite recém-escrito.</param>
    /// <param name="evento">O evento dele.</param>
    private Task<ConvitePublico> ParaPublico(ConviteDoEvento convite, EventoDoConvite evento, CancellationToken ct) =>
        ParaPublico(convite, evento, formaturaAtual, formaturas, codigos, ct);

    /// <summary>O convite como a página pública o mostra — é o que vai no PDF anexo ao e-mail.</summary>
    /// <remarks>
    /// Só se manda convite nomeado: o validador exige o nome em quem chega aqui. Estático porque a cortesia
    /// (<see cref="GestaoDeConvitesService"/>) manda o mesmo e-mail.
    /// </remarks>
    /// <param name="convite">Convite recém-escrito.</param>
    /// <param name="evento">O evento dele.</param>
    /// <param name="formaturaAtual">Turma da sessão.</param>
    /// <param name="formaturas">Nome e instituição da turma.</param>
    /// <param name="codigos">A assinatura do token.</param>
    internal static async Task<ConvitePublico> ParaPublico(
        ConviteDoEvento convite,
        EventoDoConvite evento,
        IFormaturaAtual formaturaAtual,
        IFormaturaRepository formaturas,
        CodigoDoConvite codigos,
        CancellationToken ct
    )
    {
        var turma = formaturaAtual.Id is { } id ? await formaturas.ObterDetalheDeTodasAsFormaturas(id, ct) : null;

        return new ConvitePublico(
            turma?.Nome ?? string.Empty,
            turma?.Instituicao ?? string.Empty,
            evento,
            convite.Codigo,
            codigos.Token(convite.Codigo),
            convite.NomeDoConvidado ?? throw new InvalidOperationException($"Convite {convite.Id} mandado sem convidado."),
            DocumentoDoConvidado.Mascarar(convite.TipoDoDocumento, convite.NumeroDoDocumento)
        );
    }

    /// <summary>O evento do convite; o convite só existe com evento, então a falta dele é bug.</summary>
    private async Task<EventoDoConvite> Evento(Guid eventoId, CancellationToken ct) =>
        EmissaoDeConvites.ParaConvite(
            await agenda.Obter(eventoId, ct) ?? throw new InvalidOperationException($"Convite sem o evento {eventoId} na agenda.")
        );

    /// <summary>
    /// O titular a gravar: sem documento novo e com o mesmo nome, o documento que já estava fica.
    /// </summary>
    /// <remarks>
    /// A tela não recebe o número inteiro de volta — só o mascarado —, então corrigir o e-mail de um
    /// convite nomeado chega sem documento. Sem isto, isso contaria como troca de titular e apagaria o
    /// documento do convidado.
    /// </remarks>
    private static DadosDoConvidado MantendoODocumento(ConviteDoEvento convite, DadosDoConvidado convidado) =>
        convidado.NumeroDoDocumento is null && string.Equals(convite.NomeDoConvidado, convidado.Nome, StringComparison.OrdinalIgnoreCase)
            ? convidado with
            {
                TipoDoDocumento = convite.TipoDoDocumento,
                NumeroDoDocumento = convite.NumeroDoDocumento,
            }
            : convidado;

    /// <summary>Nome aparado e documento só com os caracteres que importam.</summary>
    internal static DadosDoConvidado Normalizar(DadosDoConvidado dados) =>
        dados with
        {
            Nome = dados.Nome.Trim(),
            NumeroDoDocumento = DocumentoDoConvidado.Normalizar(dados.TipoDoDocumento, dados.NumeroDoDocumento),
            Email = string.IsNullOrWhiteSpace(dados.Email) ? null : dados.Email.Trim(),
            Observacoes = string.IsNullOrWhiteSpace(dados.Observacoes) ? null : dados.Observacoes.Trim(),
        };

    private MeuConvite ParaMeu(ConviteDoEvento convite, DateTime? validadoEm) => ParaMeu(convite, validadoEm, codigos);

    /// <summary>O convite como o dono o vê — o formando ou o comprador da loja.</summary>
    /// <param name="convite">Convite.</param>
    /// <param name="validadoEm">Quando entrou, se entrou.</param>
    /// <param name="codigos">A assinatura do token — só o convite nomeado ganha link (revisão da P1).</param>
    internal static MeuConvite ParaMeu(ConviteDoEvento convite, DateTime? validadoEm, CodigoDoConvite codigos) =>
        new(
            convite.Id,
            convite.Sequencial,
            convite.Codigo,
            convite.NomeDoConvidado is null ? null : codigos.Token(convite.Codigo),
            convite.NomeDoConvidado,
            convite.TipoDoDocumento,
            DocumentoDoConvidado.Mascarar(convite.TipoDoDocumento, convite.NumeroDoDocumento),
            convite.EmailDoConvidado,
            convite.EmitidoEm,
            validadoEm,
            convite.Observacoes
        );
}

/// <summary>Quem a auditoria da nomeação registra — só a Gestão mexendo no convite de outro, ou depois do fechamento.</summary>
/// <param name="FormaturaId">Turma.</param>
/// <param name="AutorId">Quem editou.</param>
internal sealed record AutoriaDaNomeacao(Guid FormaturaId, Guid AutorId);
