using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Pagamentos.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Cobrancas.Services;

/// <summary>
/// O plano financeiro da turma: montar, simular e pôr em vigor. A consulta das parcelas é o
/// <see cref="ConsultaDeParcelasService"/>.
/// </summary>
/// <remarks>
/// "Item em uso" é item que já gerou parcela. Como só a adesão gera parcela (Sprint 7), é o mesmo
/// que "item com adesão ativa" — sem precisar conhecer a adesão daqui.
/// </remarks>
/// <param name="planoRepository">Planos e itens.</param>
/// <param name="parcelaRepository">Parcelas geradas.</param>
/// <param name="vinculoRepository">Membros, para o total da turma.</param>
/// <param name="geracaoDeParcelas">Parcelas do rateio extraordinário no nome de quem já aderiu.</param>
/// <param name="planoValidator">Forma do plano.</param>
/// <param name="itemValidator">Forma do item.</param>
/// <param name="rateioValidator">Forma do rateio extraordinário.</param>
/// <param name="simulacaoValidator">Forma da simulação.</param>
/// <param name="eventos">Trilha de auditoria.</param>
/// <param name="valoresADevolver">O parcial das parcelas canceladas ao encerrar um item (Sprint 42, decisão 3).</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class PlanoDeCobrancaService(
    IPlanoDeCobrancaRepository planoRepository,
    IParcelaRepository parcelaRepository,
    IVinculoRepository vinculoRepository,
    IGeracaoDeParcelasService geracaoDeParcelas,
    IValidator<DadosDoPlano> planoValidator,
    IValidator<DadosDoItem> itemValidator,
    IValidator<RateioExtraordinario> rateioValidator,
    IValidator<SimularPlano> simulacaoValidator,
    IEventoRepository eventos,
    ValoresADevolver valoresADevolver,
    IUnitOfWork unitOfWork,
    ILogger<PlanoDeCobrancaService> logger
) : ICobrancaService
{
    private static readonly Erro PlanoNaoEncontrado = Erro.NaoEncontrado("cobranca.plano_nao_encontrado", "Plano de cobrança não encontrado.");

    private static readonly Erro ItemNaoEncontrado = Erro.NaoEncontrado("cobranca.item_nao_encontrado", "Item não encontrado neste plano.");

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PlanoDeCobrancaResumo>>> Listar(CancellationToken ct = default) =>
        Result.Ok(await planoRepository.Listar(ct));

    /// <inheritdoc />
    public async Task<Result<PlanoDeCobrancaDetalhe>> Obter(Guid planoId, CancellationToken ct = default)
    {
        var plano = await planoRepository.Obter(planoId, ct);

        if (plano is null)
            return PlanoNaoEncontrado;

        return await Detalhar(plano, ct);
    }

    /// <inheritdoc />
    public async Task<Result<PlanoDeCobrancaDetalhe>> Criar(DadosDoPlano dados, CancellationToken ct = default)
    {
        var validacao = planoValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PlanoDeCobrancaDetalhe>(validacao.Erros);

        var plano = new PlanoDeCobranca();
        plano.Aplicar(dados);

        await planoRepository.Adicionar(plano, ct);
        await unitOfWork.SalvarAsync(ct);

        return await Detalhar(plano, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Vale também para o plano vigente: a adesão congela as regras que cada formando aceitou
    /// (Sprint 7), então mudar aqui só alcança quem aderir depois.
    /// </remarks>
    public async Task<Result<PlanoDeCobrancaDetalhe>> Atualizar(Guid planoId, DadosDoPlano dados, CancellationToken ct = default)
    {
        var validacao = planoValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PlanoDeCobrancaDetalhe>(validacao.Erros);

        var plano = await planoRepository.ObterParaEdicao(planoId, ct);
        if (plano is null)
            return PlanoNaoEncontrado;

        plano.Aplicar(dados);

        await unitOfWork.SalvarAsync(ct);

        return await Detalhar(plano, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Sem <paramref name="rateio"/>, vale a regra normal: a parcela nasce na adesão, então o item
    /// alcança só quem aderir depois — e a tela avisa quantos ficam de fora.
    /// <para>
    /// Com ele, o item é um <b>rateio extraordinário</b> (revisão de 17/09/2026 da decisão 8 da
    /// Sprint 7): a grade também é gravada para cada vínculo ativo que já aderiu, na mesma
    /// transação. O primeiro vencimento não pode ser passado nesse caso — a parcela nasceria
    /// vencida, com multa e juros de um atraso que a pessoa não teve como cometer.
    /// </para>
    /// </remarks>
    public async Task<Result<PlanoDeCobrancaDetalhe>> AdicionarItem(
        Guid planoId,
        DadosDoItem dados,
        RateioExtraordinario? rateio = null,
        CancellationToken ct = default
    )
    {
        var validacao = itemValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PlanoDeCobrancaDetalhe>(validacao.Erros);

        if (rateio is not null)
        {
            var formaDoRateio = rateioValidator.Validar(rateio);
            if (formaDoRateio.Falhou)
                return Result.Falha<PlanoDeCobrancaDetalhe>(formaDoRateio.Erros);

            if (GradeDeParcelas.PrimeiroDoMes(dados.PrimeiroMes) < GradeDeParcelas.PrimeiroDoMes(DataUtils.Hoje()))
                return Erro.Validacao(
                    "cobranca.rateio_retroativo",
                    "O rateio não pode começar num mês que já passou: a parcela nasceria vencida, com multa e juros.",
                    campo: "primeiro_mes"
                );
        }

        var plano = await planoRepository.ObterParaEdicao(planoId, ct);
        if (plano is null)
            return PlanoNaoEncontrado;

        var aceita = plano.AceitaItem(dados.Tipo);
        if (aceita.Falhou)
            return Result.Falha<PlanoDeCobrancaDetalhe>(aceita.Erros);

        var item = ItemDeCobranca.Novo(plano.Id, dados, rateio?.OrigemDaDecisao);
        plano.Itens.Add(item);

        await planoRepository.AdicionarItem(item, ct);

        if (rateio is not null)
        {
            var ratear = await Ratear(item, ct);
            if (ratear.Falhou)
                return Result.Falha<PlanoDeCobrancaDetalhe>(ratear.Erros);
        }

        await unitOfWork.SalvarAsync(ct);

        return await Detalhar(plano, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Com parcela gerada, a grade nova é calculada e cada parcela aberta que ainda não venceu recebe
    /// o valor da mesma posição nela. Paga e vencida ficam como estão — por isso, depois de uma
    /// mudança, a soma das parcelas de um formando pode não ser o total novo do item: é a mudança
    /// valendo só para o futuro.
    /// </remarks>
    public async Task<Result<PlanoDeCobrancaDetalhe>> AlterarItem(
        Guid planoId,
        Guid itemId,
        DadosDoItem dados,
        Guid autorId,
        CancellationToken ct = default
    )
    {
        var validacao = itemValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PlanoDeCobrancaDetalhe>(validacao.Erros);

        var plano = await planoRepository.ObterParaEdicao(planoId, ct);
        if (plano is null)
            return PlanoNaoEncontrado;

        var item = plano.Itens.Find(i => i.Id == itemId);
        if (item is null)
            return ItemNaoEncontrado;

        if (item.EncerradoEm is not null)
            return Erro.Conflito("cobranca.item_encerrado", "Este item foi encerrado e não muda mais.");

        var aceita = plano.AceitaItem(dados.Tipo, exceto: item);
        if (aceita.Falhou)
            return Result.Falha<PlanoDeCobrancaDetalhe>(aceita.Erros);

        var emUso = await parcelaRepository.ExisteDoItem(item.Id, ct);
        if (emUso && item.MudaAGrade(dados))
            return Erro.Conflito(
                "cobranca.item_em_uso",
                "Este item já gerou parcelas: só o valor e a descrição podem mudar. Para mudar o resto, encerre-o e inclua outro."
            );

        var antes = Retrato(item);

        item.Aplicar(dados);

        if (emUso)
            await Repactuar(item, ct);

        await eventos.Auditar(
            NomesDeAuditoria.ItemAlterado,
            autorId,
            new
            {
                formaturaId = plano.FormaturaId,
                itemId,
                antes,
                depois = Retrato(item),
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        return await Detalhar(plano, ct);
    }

    /// <summary>
    /// O item como ele fica na trilha: o que muda valor ou vencimento do que a turma deve.
    /// </summary>
    /// <remarks>
    /// É o corpo do <c>antes</c> e do <c>depois</c>, e a tela mostra só o que diferiu entre os dois.
    /// Sem ele, "Item de cobrança alterado" dizia quem alterou e não o que mudou — que é a metade
    /// da pergunta que a assembleia faz.
    /// </remarks>
    /// <param name="item">Item do plano.</param>
    private static object Retrato(ItemDeCobranca item) =>
        new
        {
            item.Descricao,
            item.Tipo,
            valorEmCentavos = item.ValorEmCentavos,
            item.NumeroDeParcelas,
            item.DiaDeVencimento,
            item.PrimeiroMes,
        };

    /// <inheritdoc />
    /// <remarks>
    /// O retrato do evento é tirado <b>antes</b> da remoção: depois dela a linha não existe mais, e o
    /// evento seria a única memória de um item que ninguém mais consegue consultar.
    /// </remarks>
    public async Task<Result<PlanoDeCobrancaDetalhe>> RemoverItem(Guid planoId, Guid itemId, Guid autorId, CancellationToken ct = default)
    {
        var plano = await planoRepository.ObterParaEdicao(planoId, ct);
        if (plano is null)
            return PlanoNaoEncontrado;

        var item = plano.Itens.Find(i => i.Id == itemId);
        if (item is null)
            return ItemNaoEncontrado;

        if (await parcelaRepository.ExisteDoItem(item.Id, ct))
            return Erro.Conflito(
                "cobranca.item_em_uso",
                "Este item já gerou parcelas e não pode ser removido. Encerre-o: ele para de cobrar e o que já foi cobrado fica."
            );

        await eventos.Auditar(
            NomesDeAuditoria.ItemRemovido,
            autorId,
            new
            {
                formaturaId = plano.FormaturaId,
                itemId,
                removido = Retrato(item),
            },
            ct
        );

        plano.Itens.Remove(item);
        planoRepository.RemoverItem(item);

        await unitOfWork.SalvarAsync(ct);

        return await Detalhar(plano, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Vence hoje ainda é devida e fica; o que vence de amanhã em diante é cancelado. O que alguém já tinha adiantado
    /// numa parcela cancelada vai para a lista "a devolver" (Sprint 42, decisão 3).
    /// </remarks>
    public async Task<Result<PlanoDeCobrancaDetalhe>> EncerrarItem(Guid planoId, Guid itemId, Guid autorId, CancellationToken ct = default)
    {
        var plano = await planoRepository.ObterParaEdicao(planoId, ct);
        if (plano is null)
            return PlanoNaoEncontrado;

        var item = plano.Itens.Find(i => i.Id == itemId);
        if (item is null)
            return ItemNaoEncontrado;

        var hoje = DataUtils.Hoje();
        item.Encerrar(hoje);

        var canceladasAgora = (await parcelaRepository.ListarAbertasParaEdicao(item.Id, hoje.AddDays(1), ct))
            .Where(parcela => parcela.Cancelar(hoje))
            .ToList();
        var canceladas = canceladasAgora.Count;
        var aDevolver = await valoresADevolver.RegistrarParciais(canceladasAgora, ct);

        await eventos.Auditar(
            NomesDeAuditoria.ItemEncerrado,
            autorId,
            new
            {
                formaturaId = plano.FormaturaId,
                itemId,
                item.Descricao,
                item.Tipo,
                encerradoEm = hoje,
                parcelasCanceladas = canceladas,
                aDevolverEmCentavos = aDevolver,
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Item {ItemId} encerrado; {Canceladas} parcelas futuras canceladas.", item.Id, canceladas);

        return await Detalhar(plano, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A grade sai de <see cref="GradeDeParcelas"/>, a mesma conta da geração: a prévia que a
    /// tesouraria confere é, parcela por parcela, o que o formando vai dever.
    /// </remarks>
    public async Task<Result<SimulacaoDoPlano>> Simular(Guid formaturaId, Guid planoId, SimularPlano pedido, CancellationToken ct = default)
    {
        var validacao = simulacaoValidator.Validar(pedido);
        if (validacao.Falhou)
            return Result.Falha<SimulacaoDoPlano>(validacao.Erros);

        var plano = await planoRepository.Obter(planoId, ct);
        if (plano is null)
            return PlanoNaoEncontrado;

        var parcelas = GradeDeParcelas.DoFormando(pedido.Itens ?? plano.DadosDosItensAtivos());
        var totalPorFormando = parcelas.Sum(parcela => parcela.ValorEmCentavos);
        var formandos = (await vinculoRepository.ContarMembros(formaturaId, ct)).Where(c => c.Ativo).Sum(c => c.Quantidade);

        return new SimulacaoDoPlano(parcelas, totalPorFormando, formandos, totalPorFormando * formandos);
    }

    /// <inheritdoc />
    /// <remarks>
    /// "Só um vigente" é conferido aqui para dar a mensagem certa, e garantido pelo índice único
    /// parcial do banco para os dois cliques simultâneos.
    /// <para>
    /// O plano inteiro vai no corpo do evento, e não só o id: é a partir daqui que todo formando passa
    /// a dever, e a assembleia de dois anos depois pergunta "o que exatamente entrou em vigor naquele
    /// dia". O plano pode ser editado depois; o evento, não.
    /// </para>
    /// </remarks>
    public async Task<Result<PlanoDeCobrancaDetalhe>> Vigorar(Guid planoId, Guid autorId, CancellationToken ct = default)
    {
        var plano = await planoRepository.ObterParaEdicao(planoId, ct);
        if (plano is null)
            return PlanoNaoEncontrado;

        if (plano.Status == StatusDoPlano.Rascunho && await planoRepository.ExisteVigente(ct))
            return Erro.Conflito("cobranca.plano_vigente_existente", "A turma já tem um plano em vigor. Só um vale por vez.");

        var vigorar = plano.Vigorar(DateTime.UtcNow);
        if (vigorar.Falhou)
            return Result.Falha<PlanoDeCobrancaDetalhe>(vigorar.Erros);

        var ativos = plano.Itens.Where(item => item.EncerradoEm is null).ToList();

        await eventos.Auditar(
            NomesDeAuditoria.PlanoVigorado,
            autorId,
            new
            {
                formaturaId = plano.FormaturaId,
                planoId,
                itens = ativos.Select(Retrato).ToList(),
                totalPorFormandoEmCentavos = ativos.Sum(item => item.ValorEmCentavos * item.NumeroDeParcelas),
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Plano de cobrança {PlanoId} em vigor.", plano.Id);

        return await Detalhar(plano, ct);
    }

    /// <summary>Grava a grade do item novo para quem já aderiu — o rateio extraordinário.</summary>
    /// <remarks>
    /// Vínculo ativo, e por isso a contagem aqui pode ser menor que o <c>FormandosComParcela</c> que
    /// a tela mostrou: quem saiu da turma continua devendo o que já devia, e não recebe cobrança nova.
    /// </remarks>
    /// <param name="item">Item recém-incluído, ainda não salvo.</param>
    private async Task<Result> Ratear(ItemDeCobranca item, CancellationToken ct)
    {
        var vinculos = await parcelaRepository.ListarVinculosAtivosComParcela(item.PlanoId, ct);

        var geradas = await geracaoDeParcelas.GerarDoItem(vinculos, item, ct);
        if (geradas.Falhou)
            return Result.Falha(geradas.Erros);

        logger.LogInformation(
            "Rateio extraordinário no item {ItemId}: {Parcelas} parcelas para {Vinculos} formandos que já haviam aderido.",
            item.Id,
            geradas.Valor,
            vinculos.Count
        );

        return Result.Ok();
    }

    /// <summary>
    /// Leva o valor novo do item às parcelas abertas que ainda não venceram, formando a formando.
    /// </summary>
    /// <remarks>
    /// Por vínculo, e não pela grade do item: quem aderiu depois do começo do plano tem menos
    /// parcelas que ele (<c>GradeDeParcelas.DeQuemAdereEm</c>), e copiar o valor da posição
    /// correspondente faria essa pessoa pagar menos que o resto da turma.
    /// <para>
    /// O que já venceu, foi pago ou foi cancelado fica como está e conta como <i>comprometido</i>; o
    /// que sobra do total novo se redistribui pelas parcelas que ainda não venceram. Cancelada não
    /// entra no comprometido — ela não é devida. <c>ponytail:</c> se o valor novo for menor do que o
    /// formando já pagou, as futuras vão a zero em vez de negativo, e a soma dele fica acima do total
    /// novo; devolver dinheiro é decisão de gente, não de repactuação.
    /// </para>
    /// </remarks>
    private async Task Repactuar(ItemDeCobranca item, CancellationToken ct)
    {
        var hoje = DataUtils.Hoje();
        var repactuadas = 0;

        foreach (var doVinculo in (await parcelaRepository.ListarDoItemParaEdicao(item.Id, ct)).GroupBy(parcela => parcela.VinculoId))
        {
            var futuras = doVinculo
                .Where(parcela => parcela.StatusEm(hoje) == StatusDaParcela.Aberta)
                .OrderBy(parcela => parcela.Vencimento)
                .ToList();
            if (futuras.Count == 0)
                continue;

            var comprometido = doVinculo
                .Where(parcela => !futuras.Contains(parcela) && parcela.Status != StatusDaParcela.Cancelada)
                .Sum(parcela => parcela.ValorOriginalEmCentavos);

            var restante = item.ValorEmCentavos - comprometido;
            var valores = GradeDeParcelas.Distribuir(item.ValorEmCentavos >= 0 ? Math.Max(0, restante) : Math.Min(0, restante), futuras.Count);

            repactuadas += futuras.Where((parcela, posicao) => parcela.Repactuar(valores[posicao], hoje)).Count();
        }

        logger.LogInformation("Item {ItemId} alterado; {Repactuadas} parcelas futuras com valor novo.", item.Id, repactuadas);
    }

    /// <summary>Monta o detalhe do plano, com os itens em ordem de criação e os que já estão em uso.</summary>
    /// <remarks>
    /// Os campos dos opcionais vêm junto: é a mesma tabela, e é por <c>Opcional</c> que o cartão
    /// Opcionais da tela separa o que a turma inteira deve do que só alguns pedem.
    /// </remarks>
    private async Task<PlanoDeCobrancaDetalhe> Detalhar(PlanoDeCobranca plano, CancellationToken ct)
    {
        var emUso = await parcelaRepository.ListarItensEmUso(plano.Id, ct);

        return new PlanoDeCobrancaDetalhe(
            plano.Id,
            plano.Nome,
            plano.Status,
            plano.VigenteDesde,
            plano.PercentualDeMulta,
            plano.PercentualDeJurosAoMes,
            plano.CarenciaEmDias,
            plano.PercentualDeDescontoPorAntecipacao,
            plano.DiasMinimosParaDesconto,
            [
                .. plano
                    .Itens.OrderBy(item => item.CriadoEm)
                    .ThenBy(item => item.Id)
                    .Select(item => ItemDeCobrancaDetalhe.De(item, emUso.Contains(item.Id))),
            ],
            emUso.Count == 0 ? 0 : await parcelaRepository.ContarVinculosComParcela(plano.Id, ct)
        );
    }
}
