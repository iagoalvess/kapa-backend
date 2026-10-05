using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Texto;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Festa.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Loja.Models;
using Backend.Business.Usuarios.Models;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Adesoes.Services;

/// <summary>
/// A cesta depois da adesão: o que o formando tem, e o aditivo que acrescenta (Sprint 48, D7/D38).
/// </summary>
/// <remarks>
/// O aditivo só acrescenta — subir de faixa no mesmo grupo ou incluir pacote novo. A diferença se divide pelas
/// parcelas que faltam do pacote novo (<see cref="GradeDeParcelas.DeQuemAdereEm"/>), e a faixa que sai continua com as
/// parcelas dela: o que foi pago está pago, e o que falta continua devido. Descer ou tirar é a solicitação de
/// cancelamento (D8).
/// <para>
/// O rito é o da adesão: snapshot, hash sobre o termo e o snapshot, código no e-mail, IP e User-Agent. O hash que a
/// tela devolve é o da prévia que ela mostrou; se o catálogo mudou no meio, o aceite é recusado.
/// </para>
/// </remarks>
/// <param name="adesoes">Termo vigente, adesão e aditivos.</param>
/// <param name="planos">Catálogo e cesta.</param>
/// <param name="parcelas">As parcelas do formando.</param>
/// <param name="solicitacoes">As solicitações abertas, para a tela da cesta.</param>
/// <param name="perfis">O vínculo de quem pede.</param>
/// <param name="formaturas">Nome da turma, para o e-mail.</param>
/// <param name="emails">O código do aceite.</param>
/// <param name="convites">Os convites dos pacotes novos.</param>
/// <param name="userManager">Gera e confere o código.</param>
/// <param name="validator">Forma do aceite.</param>
/// <param name="eventos">Trilha de auditoria.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class AditivoService(
    IAdesaoRepository adesoes,
    IPlanoDeCobrancaRepository planos,
    IParcelaRepository parcelas,
    ISolicitacaoDeCancelamentoRepository solicitacoes,
    IPerfilRepository perfis,
    IFormaturaRepository formaturas,
    EmailsDeAdesao emails,
    EmissaoDeConvites convites,
    UserManager<Usuario> userManager,
    IValidator<AceitarAditivo> validator,
    IEventoRepository eventos,
    IUnitOfWork unitOfWork,
    ILogger<AditivoService> logger
) : IAditivoService
{
    /// <summary>Finalidade do código no provedor do Identity: o do aditivo não serve para aderir, e vice-versa.</summary>
    public const string FinalidadeDoCodigo = "aditivo";

    private static readonly Erro SemAdesao = Erro.Conflito("adesao.aditivo_sem_adesao", "Aditivo é mudança de uma adesão: adira ao termo antes.");

    private static readonly Erro SemPlano = Erro.Conflito("adesao.sem_plano_vigente", "A turma ainda não tem plano de cobrança em vigor.");

    /// <inheritdoc />
    public async Task<Result<MinhaCesta>> ObterCesta(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var contexto = await Ler(formaturaId, usuarioId, ct);
        if (contexto.Falhou)
            return Result.Falha<MinhaCesta>(contexto.Erros);

        var (membro, plano, escolhas, contratado) = contexto.Valor!;
        var abertas = (await solicitacoes.Listar(membro.VinculoId, StatusDoPedidoDeCancelamento.Aberto, ct))
            .Select(solicitacao => solicitacao.ItemDeCobrancaId)
            .ToHashSet();

        List<PacoteNaCesta> naCesta =
        [
            .. plano
                .Itens.Where(item => escolhas.ContainsKey(item.Id))
                .OrderBy(item => item.CriadoEm)
                .ThenBy(item => item.Id)
                .Select(item => new PacoteNaCesta(
                    item.Id,
                    item.Tipo,
                    item.Descricao,
                    item.Grupo,
                    JaContratado(item, plano, contratado),
                    item.ConvitesDaFesta,
                    item.ConvitesDaColacao,
                    escolhas[item.Id].Observacao,
                    item.CancelavelAte,
                    abertas.Contains(item.Id)
                )),
        ];

        List<PacoteDisponivel> disponiveis =
        [
            .. plano
                .Pacotes()
                .Where(pacote => !escolhas.ContainsKey(pacote.Id))
                .Select(pacote => Mudanca(pacote, plano, escolhas, contratado))
                .Where(mudanca => mudanca.DiferencaEmCentavos > 0)
                .Select(mudanca => new PacoteDisponivel(
                    mudanca.Entra.ItemId,
                    mudanca.Entra.Tipo,
                    mudanca.Entra.Descricao,
                    mudanca.Entra.Grupo,
                    mudanca.Entra.ValorEmCentavos,
                    mudanca.DiferencaEmCentavos,
                    mudanca.Entra.ConvitesDaFesta,
                    mudanca.Entra.ConvitesDaColacao,
                    mudanca.Sai?.ItemId
                )),
        ];

        return new MinhaCesta(naCesta, disponiveis);
    }

    /// <inheritdoc />
    public async Task<Result<PreviaDoAditivo>> Simular(Guid formaturaId, Guid usuarioId, IReadOnlyList<Guid> pacotes, CancellationToken ct = default)
    {
        var montado = await Montar(formaturaId, usuarioId, pacotes, ct);

        return montado.Falhou ? Result.Falha<PreviaDoAditivo>(montado.Erros) : montado.Valor!.Previa;
    }

    /// <inheritdoc />
    /// <remarks>O mesmo código de seis dígitos da adesão, com outra finalidade: um não confirma o outro.</remarks>
    public async Task<Result<CodigoEnviado>> SolicitarCodigo(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        if (await perfis.ObterMembro(formaturaId, usuarioId, ct) is not { } membro)
            return ErrosDeFormatura.MembroNaoEncontrado;

        if (!await adesoes.JaAderiuAlgumaVez(membro.VinculoId, ct))
            return SemAdesao;

        if (await userManager.FindByIdAsync(usuarioId.ToString()) is not { } usuario)
            return ErrosDeFormatura.MembroNaoEncontrado;

        var codigo = await userManager.GenerateUserTokenAsync(usuario, TokenOptions.DefaultEmailProvider, FinalidadeDoCodigo);
        var formatura = await formaturas.ObterDetalheDeTodasAsFormaturas(formaturaId, ct);
        var email = usuario.Email ?? membro.Email;

        await emails.CodigoDoAditivo(email, formatura?.Nome ?? string.Empty, codigo, AdesaoService.MinutosDeValidadeDoCodigo, ct);
        await unitOfWork.SalvarAsync(ct);

        return new CodigoEnviado(AdesaoService.MascararEmail(email), AdesaoService.MinutosDeValidadeDoCodigo);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O código é a última conferência, como na adesão: ele vale poucos minutos. A cesta muda, as parcelas nascem e
    /// o aditivo é gravado numa transação só; os convites dos pacotes novos saem logo depois, na mesma.
    /// </remarks>
    public async Task<Result<PreviaDoAditivo>> Aceitar(
        Guid formaturaId,
        Guid usuarioId,
        AceitarAditivo dados,
        OrigemDoAceite origem,
        CancellationToken ct = default
    )
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PreviaDoAditivo>(validacao.Erros);

        var montado = await Montar(formaturaId, usuarioId, dados.Pacotes, ct);
        if (montado.Falhou)
            return Result.Falha<PreviaDoAditivo>(montado.Erros);

        var (previa, membro, adesaoId, mudancas, plano) = montado.Valor!;

        if (!string.Equals(previa.HashDoConteudo, dados.HashDoConteudo, StringComparison.Ordinal))
            return Erro.Conflito(
                "adesao.aditivo_desatualizado",
                "O catálogo ou a sua cesta mudou enquanto você lia. Confira o aditivo de novo antes de aceitar."
            );

        if (await userManager.FindByIdAsync(usuarioId.ToString()) is not { } usuario)
            return ErrosDeFormatura.MembroNaoEncontrado;

        if (!await userManager.VerifyUserTokenAsync(usuario, TokenOptions.DefaultEmailProvider, FinalidadeDoCodigo, dados.Codigo))
            return Erro.Conflito(
                "adesao.codigo_invalido",
                "O código não confere ou já expirou. Peça um código novo e use o mais recente que chegou no seu e-mail."
            );

        var aditivo = new AditivoDaAdesao
        {
            VinculoId = membro.VinculoId,
            AdesaoId = adesaoId,
            HashDoConteudo = previa.HashDoConteudo,
            AceitoEm = DateTime.UtcNow,
            EnderecoIp = origem.EnderecoIp ?? string.Empty,
            UserAgent = TextoUtils.Truncar(origem.UserAgent, AdesaoService.TamanhoMaximoDoUserAgent) ?? string.Empty,
            EmailDoAceite = usuario.Email ?? membro.Email,
            Conteudo = previa.Aditivo.ParaJson(),
        };

        var gravado = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                await adesoes.AdicionarAditivo(aditivo, token);

                foreach (var (entra, sai, diferenca) in mudancas)
                {
                    if (sai is not null && await planos.ObterEscolhaParaEdicao(membro.VinculoId, sai.Id, token) is { } escolhaQueSai)
                        planos.RemoverEscolha(escolhaQueSai);

                    var observacao = dados.Observacoes?.FirstOrDefault(o => o.PacoteId == entra.Id)?.Texto;
                    await planos.AdicionarEscolhas([EscolhaDaCesta.Nova(membro.VinculoId, entra.Id, observacao)], token);

                    var ultimo = (await parcelas.ListarNumerosGerados(membro.VinculoId, entra.Id, token)).DefaultIfEmpty(0).Max();
                    await parcelas.Adicionar(
                        [
                            .. GradeDeParcelas
                                .DeQuemAdereEm(entra.ParaDados() with { ValorEmCentavos = diferenca }, DataUtils.Hoje())
                                .Select(prevista => Parcela.Nova(membro.VinculoId, entra.Id, prevista with { Numero = ultimo + prevista.Numero })),
                        ],
                        token
                    );
                }

                await eventos.Auditar(
                    NomesDeAuditoria.AditivoAceito,
                    usuarioId,
                    new
                    {
                        formaturaId,
                        aditivoId = aditivo.Id,
                        planoId = plano.Id,
                        entram = mudancas.Select(m => m.Entra.Id).ToArray(),
                        saem = mudancas.Where(m => m.Sai is not null).Select(m => m.Sai!.Id).ToArray(),
                        totalEmCentavos = previa.Aditivo.TotalEmCentavos,
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);
                await convites.EmitirDosPacotes(formaturaId, membro.VinculoId, token);

                return Result.Ok();
            },
            ct
        );
        if (gravado.Falhou)
            return Result.Falha<PreviaDoAditivo>(gravado.Erros);

        logger.LogInformation("Aditivo {AditivoId} do vínculo {VinculoId}: {Pacotes} pacotes.", aditivo.Id, membro.VinculoId, mudancas.Count);

        return previa;
    }

    /// <summary>
    /// Confere a escolha e monta o aditivo: cada pacote que entra, a faixa que ele substitui e a diferença; o
    /// snapshot com as parcelas novas; e o hash.
    /// </summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    /// <param name="pacoteIds">Os pacotes que entram.</param>
    private async Task<Result<AditivoMontado>> Montar(Guid formaturaId, Guid usuarioId, IReadOnlyList<Guid> pacoteIds, CancellationToken ct)
    {
        var contexto = await Ler(formaturaId, usuarioId, ct);
        if (contexto.Falhou)
            return Result.Falha<AditivoMontado>(contexto.Erros);

        var (membro, plano, escolhas, contratado) = contexto.Valor!;

        if (pacoteIds.Count == 0)
            return Erro.Validacao("adesao.aditivo_sem_pacote", "Escolha ao menos um pacote para acrescentar.", campo: "pacotes");

        var entram = plano.MontarCesta(pacoteIds);
        if (entram.Falhou)
            return Result.Falha<AditivoMontado>(entram.Erros);

        if (entram.Valor!.FirstOrDefault(pacote => escolhas.ContainsKey(pacote.Id)) is { } repetido)
            return Erro.Validacao(
                "adesao.pacote_ja_contratado",
                $"{RotuloDoItem.De(repetido.Tipo, repetido.Descricao)} já está na sua cesta.",
                campo: "pacotes"
            );

        var hoje = DataUtils.Hoje();
        List<MudancaDaCesta> mudancas = [];

        foreach (var pacote in entram.Valor!)
        {
            var mudanca = Mudanca(pacote, plano, escolhas, contratado);

            if (mudanca.DiferencaEmCentavos <= 0)
                return Erro.Conflito(
                    "adesao.aditivo_so_acrescenta",
                    "O aditivo só acrescenta: para descer de faixa ou tirar um pacote, peça o cancelamento à comissão."
                );

            var grade = GradeDeParcelas.DeQuemAdereEm(pacote.ParaDados() with { ValorEmCentavos = mudanca.DiferencaEmCentavos }, hoje);
            if (ItemDeCobranca.PassaDoLimite(grade[^1].Vencimento, pacote.UltimoVencimento) is { } tarde)
                return tarde;

            mudancas.Add(mudanca);
        }

        if (await adesoes.ObterUltimaDoVinculo(membro.VinculoId, ct) is not { } adesao)
            return SemAdesao;

        var parcelasNovas = GradeDeParcelas.DoFormando(
            entram.Valor!.Zip(mudancas, (pacote, mudanca) => pacote.ParaDados() with { ValorEmCentavos = mudanca.DiferencaEmCentavos }),
            hoje
        );
        var snapshot = new SnapshotDoAditivo(
            SnapshotDoAditivo.EsquemaAtual,
            plano.Id,
            adesao.Adesao.Versao,
            mudancas,
            parcelasNovas,
            parcelasNovas.Sum(parcela => parcela.ValorEmCentavos)
        );

        var termo = await adesoes.ObterTermoVigente(ct);
        var hash = AdesaoDoFormando.CalcularHash(termo?.Conteudo ?? adesao.ConteudoDoTermo, snapshot.ParaJson());

        return new AditivoMontado(
            new PreviaDoAditivo(snapshot, hash),
            membro,
            adesao.Adesao.Id,
            [
                .. entram.Valor!.Zip(
                    mudancas,
                    (pacote, mudanca) => (pacote, plano.Itens.Find(item => item.Id == mudanca.Sai?.ItemId), mudanca.DiferencaEmCentavos)
                ),
            ],
            plano
        );
    }

    /// <summary>
    /// O que um pacote do catálogo cobraria se entrasse: o preço, menos o que a faixa do mesmo grupo já soma nas
    /// parcelas do formando.
    /// </summary>
    /// <param name="pacote">Pacote do catálogo.</param>
    /// <param name="plano">Plano vigente.</param>
    /// <param name="escolhas">A cesta, por pacote.</param>
    /// <param name="contratado">O que cada item soma nas parcelas do formando.</param>
    private static MudancaDaCesta Mudanca(
        ItemDeCobranca pacote,
        PlanoDeCobranca plano,
        IReadOnlyDictionary<Guid, EscolhaDaCesta> escolhas,
        IReadOnlyDictionary<Guid, long> contratado
    )
    {
        var sai = pacote.Grupo is null
            ? null
            : plano.Itens.FirstOrDefault(item => escolhas.ContainsKey(item.Id) && string.Equals(item.Grupo, pacote.Grupo, StringComparison.Ordinal));
        var jaContratado = sai is null ? 0 : JaContratado(sai, plano, contratado);

        return new MudancaDaCesta(
            PacoteDaCesta.De(pacote),
            sai is null ? null : PacoteDaCesta.De(sai),
            jaContratado,
            pacote.ValorEmCentavos - jaContratado
        );
    }

    /// <summary>
    /// O que o formando já contratou por um pacote: no pacote de grupo, a soma de todas as faixas que ele teve — quem foi
    /// da Festa 15 para a 20 deve a 15 inteira mais a diferença, e é o grupo que soma o preço da 20.
    /// </summary>
    /// <param name="pacote">Pacote da cesta.</param>
    /// <param name="plano">Plano vigente.</param>
    /// <param name="contratado">O que cada item soma nas parcelas do formando.</param>
    private static long JaContratado(ItemDeCobranca pacote, PlanoDeCobranca plano, IReadOnlyDictionary<Guid, long> contratado) =>
        pacote.Grupo is null
            ? contratado.GetValueOrDefault(pacote.Id)
            : plano
                .Itens.Where(item => string.Equals(item.Grupo, pacote.Grupo, StringComparison.Ordinal))
                .Sum(item => contratado.GetValueOrDefault(item.Id));

    /// <summary>O formando, o plano vigente, a cesta e o que cada item dela soma nas parcelas dele.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">O próprio.</param>
    private async Task<Result<ContextoDaCesta>> Ler(Guid formaturaId, Guid usuarioId, CancellationToken ct)
    {
        if (await perfis.ObterMembro(formaturaId, usuarioId, ct) is not { } membro)
            return ErrosDeFormatura.MembroNaoEncontrado;

        if (await planos.ObterVigente(ct) is not { } plano)
            return SemPlano;

        var escolhas = (await planos.ListarEscolhas(membro.VinculoId, ct)).ToDictionary(escolha => escolha.ItemDeCobrancaId);
        if (escolhas.Count == 0 && !await adesoes.JaAderiuAlgumaVez(membro.VinculoId, ct))
            return SemAdesao;

        var contratado = (await parcelas.ListarDoVinculo(membro.VinculoId, DataUtils.Hoje(), ct))
            .Where(parcela => parcela.Status != StatusDaParcela.Cancelada)
            .GroupBy(parcela => parcela.ItemDeCobrancaId)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Sum(parcela => parcela.ValorOriginalEmCentavos));

        return new ContextoDaCesta(membro, plano, escolhas, contratado);
    }

    /// <summary>O que a leitura da cesta junta.</summary>
    private sealed record ContextoDaCesta(
        MembroDoPerfil Membro,
        PlanoDeCobranca Plano,
        IReadOnlyDictionary<Guid, EscolhaDaCesta> Escolhas,
        IReadOnlyDictionary<Guid, long> Contratado
    );

    /// <summary>O aditivo montado: a prévia, e o que a gravação precisa.</summary>
    private sealed record AditivoMontado(
        PreviaDoAditivo Previa,
        MembroDoPerfil Membro,
        Guid AdesaoId,
        IReadOnlyList<(ItemDeCobranca Entra, ItemDeCobranca? Sai, long Diferenca)> Mudancas,
        PlanoDeCobranca Plano
    );
}
