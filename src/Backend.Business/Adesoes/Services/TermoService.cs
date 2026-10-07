using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Formandos.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Adesoes.Services;

/// <summary>
/// O termo de adesão da turma: versões publicadas pela comissão e o conteúdo que o formando aceita.
/// </summary>
/// <remarks>
/// Publicar é inserir a versão seguinte, vigente na hora. A corrida — dois presidentes publicando ao
/// mesmo tempo — fica com o índice único <c>(formatura_id, versao)</c>, que recusa a segunda.
/// </remarks>
/// <param name="adesaoRepository">Termos e adesões.</param>
/// <param name="planoRepository">Plano vigente e a cesta já contratada.</param>
/// <param name="perfilRepository">O vínculo de quem lê, para a cesta que ele já contratou.</param>
/// <param name="validator">Forma do texto.</param>
/// <param name="eventos">Trilha de auditoria.</param>
/// <param name="formaturaAtual">Turma da sessão — o termo é novo, e o contexto só carimba a dele no commit.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class TermoService(
    IAdesaoRepository adesaoRepository,
    IPlanoDeCobrancaRepository planoRepository,
    IPerfilRepository perfilRepository,
    IValidator<PublicarTermo> validator,
    IEventoRepository eventos,
    IFormaturaAtual formaturaAtual,
    IUnitOfWork unitOfWork,
    ILogger<TermoService> logger
) : ITermoService
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<TermoPublicado>>> Listar(CancellationToken ct = default) =>
        Result.Ok(await adesaoRepository.ListarTermos(ct));

    /// <inheritdoc />
    /// <remarks>
    /// O texto <b>não</b> entra no corpo do evento: ele já é imutável na própria tabela de termos, e
    /// copiar páginas de contrato para dentro do evento engordaria a maior tabela do banco sem
    /// responder nada que a versão não responda.
    /// <para>
    /// A formatura do evento vem da sessão, e <b>não</b> de <c>termo.FormaturaId</c>: o termo acabou
    /// de ser criado, e quem preenche essa coluna é o <c>SaveChangesAsync</c>, que ainda não rodou.
    /// Lê-la aqui grava <c>Guid.Empty</c> no evento — e o evento some da trilha da turma.
    /// </para>
    /// <para>
    /// Exige plano de cobrança em vigor (<c>adesao.termo_sem_plano_vigente</c>, decisão de 06/10/2026): o que o formando
    /// assina é o termo com o quadro de escolhas do plano (Sprint 47), e sem o plano o texto seria escrito antes de os
    /// pacotes e os preços existirem. Vale para toda versão — numa turma rodando, o plano em vigor sempre existe.
    /// </para>
    /// </remarks>
    public async Task<Result<VersaoDoTermo>> Publicar(Guid usuarioId, PublicarTermo dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<VersaoDoTermo>(validacao.Erros);

        if (!await planoRepository.ExisteVigente(ct))
            return Erro.Conflito(
                "adesao.termo_sem_plano_vigente",
                "Ponha o plano de cobrança em vigor antes de publicar o termo: ele é assinado junto com os pacotes do plano."
            );

        var vigente = await adesaoRepository.ObterTermoVigente(ct);
        var conteudo = dados.Conteudo.Trim();

        if (vigente?.Conteudo == conteudo)
            return Erro.Conflito("adesao.termo_sem_mudanca", "Este texto é igual ao da versão vigente.");

        var termo = new TermoDaFormatura
        {
            Versao = (vigente?.Versao ?? 0) + 1,
            Conteudo = conteudo,
            VigenteDesde = DateTime.UtcNow,
        };

        await adesaoRepository.AdicionarTermo(termo, ct);

        await eventos.Auditar(
            NomesDeAuditoria.TermoPublicado,
            usuarioId,
            new
            {
                formaturaId = formaturaAtual.Id,
                termoId = termo.Id,
                termo.Versao,
                versaoAnterior = vigente?.Versao,
                caracteres = conteudo.Length,
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Termo de adesão versão {Versao} publicado por {UsuarioId}.", termo.Versao, usuarioId);

        return new VersaoDoTermo(termo.Id, termo.Versao, termo.Conteudo, termo.VigenteDesde);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O plano é congelado agora com a cesta pedida, como seria no aceite: o hash devolvido é o mesmo que a adesão vai
    /// recalcular. Se o termo, o catálogo ou a escolha mudarem enquanto o formando lê, os dois deixam de bater — e a
    /// tela pede este conteúdo de novo a cada pacote marcado.
    /// <para>
    /// Quem já contratou uma cesta numa versão anterior do termo a mantém (D4): a escolha da tela é ignorada.
    /// </para>
    /// <para>
    /// O resumo por IA vem junto e fica <b>fora</b> do hash (Sprint 24, decisão 2): ele chegar depois
    /// não devolve <c>adesao.termo_desatualizado</c> a quem está com a tela aberta.
    /// </para>
    /// </remarks>
    public async Task<Result<ConteudoParaAdesao>> ObterParaAdesao(
        Guid formaturaId,
        Guid usuarioId,
        IReadOnlyCollection<Guid> pacotes,
        CancellationToken ct = default
    )
    {
        var termo = await adesaoRepository.ObterTermoVigente(ct);
        var vigente = await planoRepository.ObterVigente(ct);
        var resumo = termo is null ? null : await adesaoRepository.ObterResumo(termo.Id, ct);

        if (vigente is null)
            return new ConteudoParaAdesao(termo, null, null, resumo, [], []);

        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        IReadOnlyList<Guid> contratada = membro is null ? [] : await planoRepository.ListarCesta(membro.VinculoId, ct);

        var cesta = vigente.CestaDe(contratada, pacotes);
        if (cesta.Falhou)
            return Result.Falha<ConteudoParaAdesao>(cesta.Erros);

        var hoje = DataUtils.Hoje();
        var plano = SnapshotDoPlano.De(vigente, cesta.Valor, hoje);
        var hash = termo is null ? null : AdesaoDoFormando.CalcularHash(termo.Conteudo, plano.ParaJson());

        return new ConteudoParaAdesao(termo, plano, hash, resumo, [.. vigente.Pacotes().Select(pacote => NoCatalogo(pacote, hoje))], contratada);
    }

    /// <summary>O pacote como o formando o vê, com as parcelas que restam a quem adere hoje.</summary>
    /// <param name="pacote">Pacote do catálogo.</param>
    /// <param name="hoje">Dia de referência.</param>
    private static PacoteDoCatalogo NoCatalogo(ItemDeCobranca pacote, DateOnly hoje) =>
        new(
            pacote.Id,
            pacote.Grupo,
            pacote.Tipo,
            pacote.Descricao,
            pacote.ValorEmCentavos,
            GradeDeParcelas.DeQuemAdereEm(pacote.ParaDados(), hoje).Count,
            pacote.ConvitesDaFesta,
            pacote.ConvitesDaColacao
        );
}
