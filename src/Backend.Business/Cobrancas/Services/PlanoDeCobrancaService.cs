using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Cobrancas.Services;

/// <summary>
/// O plano financeiro da turma: montar, simular, pôr em vigor e consultar as parcelas.
/// </summary>
/// <remarks>
/// "Item em uso" é item que já gerou parcela. Como só a adesão gera parcela (Sprint 7), é o mesmo
/// que "item com adesão ativa" — sem precisar conhecer a adesão daqui.
/// </remarks>
/// <param name="planoRepository">Planos e itens.</param>
/// <param name="parcelaRepository">Parcelas geradas.</param>
/// <param name="vinculoRepository">Membros, para o total da turma.</param>
/// <param name="planoValidator">Forma do plano.</param>
/// <param name="itemValidator">Forma do item.</param>
/// <param name="simulacaoValidator">Forma da simulação.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class PlanoDeCobrancaService(
    IPlanoDeCobrancaRepository planoRepository,
    IParcelaRepository parcelaRepository,
    IVinculoRepository vinculoRepository,
    IValidator<DadosDoPlano> planoValidator,
    IValidator<DadosDoItem> itemValidator,
    IValidator<SimularPlano> simulacaoValidator,
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
    public async Task<Result<PlanoDeCobrancaDetalhe>> AdicionarItem(Guid planoId, DadosDoItem dados, CancellationToken ct = default)
    {
        var validacao = itemValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PlanoDeCobrancaDetalhe>(validacao.Erros);

        var plano = await planoRepository.ObterParaEdicao(planoId, ct);
        if (plano is null)
            return PlanoNaoEncontrado;

        var aceita = plano.AceitaItem(dados.Tipo);
        if (aceita.Falhou)
            return Result.Falha<PlanoDeCobrancaDetalhe>(aceita.Erros);

        var item = ItemDeCobranca.Novo(plano.Id, dados);
        plano.Itens.Add(item);

        await planoRepository.AdicionarItem(item, ct);
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
    public async Task<Result<PlanoDeCobrancaDetalhe>> AlterarItem(Guid planoId, Guid itemId, DadosDoItem dados, CancellationToken ct = default)
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

        item.Aplicar(dados);

        if (emUso)
            await Repactuar(item, ct);

        await unitOfWork.SalvarAsync(ct);

        return await Detalhar(plano, ct);
    }

    /// <inheritdoc />
    public async Task<Result<PlanoDeCobrancaDetalhe>> RemoverItem(Guid planoId, Guid itemId, CancellationToken ct = default)
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

        plano.Itens.Remove(item);
        planoRepository.RemoverItem(item);

        await unitOfWork.SalvarAsync(ct);

        return await Detalhar(plano, ct);
    }

    /// <inheritdoc />
    /// <remarks>Vence hoje ainda é devida e fica; o que vence de amanhã em diante é cancelado.</remarks>
    public async Task<Result<PlanoDeCobrancaDetalhe>> EncerrarItem(Guid planoId, Guid itemId, CancellationToken ct = default)
    {
        var plano = await planoRepository.ObterParaEdicao(planoId, ct);
        if (plano is null)
            return PlanoNaoEncontrado;

        var item = plano.Itens.Find(i => i.Id == itemId);
        if (item is null)
            return ItemNaoEncontrado;

        var hoje = DataUtils.Hoje();
        item.Encerrar(hoje);

        var canceladas = (await parcelaRepository.ListarAbertasParaEdicao(item.Id, hoje.AddDays(1), ct)).Count(parcela => parcela.Cancelar(hoje));

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
    /// </remarks>
    public async Task<Result<PlanoDeCobrancaDetalhe>> Vigorar(Guid planoId, CancellationToken ct = default)
    {
        var plano = await planoRepository.ObterParaEdicao(planoId, ct);
        if (plano is null)
            return PlanoNaoEncontrado;

        if (plano.Status == StatusDoPlano.Rascunho && await planoRepository.ExisteVigente(ct))
            return Erro.Conflito("cobranca.plano_vigente_existente", "A turma já tem um plano em vigor. Só um vale por vez.");

        var vigorar = plano.Vigorar(DateTime.UtcNow);
        if (vigorar.Falhou)
            return Result.Falha<PlanoDeCobrancaDetalhe>(vigorar.Erros);

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Plano de cobrança {PlanoId} em vigor.", plano.Id);

        return await Detalhar(plano, ct);
    }

    /// <inheritdoc />
    /// <remarks>Aberta e vencida saem com o valor do dia, pelas regras que cada formando aceitou.</remarks>
    public async Task<Result<PaginaDe<ParcelaResumo>>> ListarParcelas(
        PaginacaoRequest paginacao,
        FiltroDeParcelas filtro,
        CancellationToken ct = default
    )
    {
        var hoje = DataUtils.Hoje();
        var pagina = await parcelaRepository.Listar(paginacao.Normalizar(), filtro, hoje, ct);

        return pagina with
        {
            Itens = await parcelaRepository.ComValorDoDia(pagina.Itens, hoje, ct),
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// A contagem é uma consulta agrupada; o vencido atualizado soma o valor do dia de cada vencida,
    /// pelas regras de cada formando — multa e juros nunca estão gravados, então são somados aqui.
    /// </remarks>
    public async Task<Result<ResumoDeParcelas>> ResumirParcelas(FiltroDeParcelas filtro, CancellationToken ct = default)
    {
        var hoje = DataUtils.Hoje();
        var contagem = await parcelaRepository.Contar(filtro, hoje, ct);
        var emAtraso = await parcelaRepository.ListarEmAtraso(filtro, hoje, ct);
        IReadOnlyDictionary<Guid, RegrasDeAtraso> regras =
            emAtraso.Count == 0
                ? new Dictionary<Guid, RegrasDeAtraso>()
                : await parcelaRepository.ObterRegrasDeAtraso([.. emAtraso.Select(p => p.VinculoId).Distinct()], ct);

        SomaDeParcelas Somar(StatusDaParcela status, bool peloPago = false) =>
            contagem.FirstOrDefault(c => c.Status == status) is { } linha
                ? new SomaDeParcelas(linha.Quantidade, peloPago ? linha.PagoEmCentavos : linha.OriginalEmCentavos)
                : SomaDeParcelas.Zero;

        return new ResumoDeParcelas(
            new SomaDeParcelas(contagem.Sum(c => c.Quantidade), contagem.Sum(c => c.OriginalEmCentavos)),
            Somar(StatusDaParcela.Aberta),
            Somar(StatusDaParcela.Vencida),
            Somar(StatusDaParcela.Paga, peloPago: true),
            Somar(StatusDaParcela.Cancelada),
            emAtraso.Sum(parcela =>
                ValorDoDia
                    .Calcular(
                        parcela.ValorOriginalEmCentavos,
                        parcela.Vencimento,
                        hoje,
                        regras.GetValueOrDefault(parcela.VinculoId, RegrasDeAtraso.Nenhuma)
                    )
                    .TotalEmCentavos
            )
        );
    }

    /// <summary>Leva o valor novo do item às parcelas abertas que ainda não venceram.</summary>
    private async Task Repactuar(ItemDeCobranca item, CancellationToken ct)
    {
        var hoje = DataUtils.Hoje();
        var grade = item.ParaDados().Grade().ToDictionary(parcela => parcela.Numero, parcela => parcela.ValorEmCentavos);

        var repactuadas = (await parcelaRepository.ListarAbertasParaEdicao(item.Id, hoje, ct)).Count(parcela =>
            parcela.Repactuar(grade[parcela.Numero], hoje)
        );

        logger.LogInformation("Item {ItemId} alterado; {Repactuadas} parcelas futuras com valor novo.", item.Id, repactuadas);
    }

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
            [
                .. plano
                    .Itens.OrderBy(item => item.CriadoEm)
                    .ThenBy(item => item.Id)
                    .Select(item => new ItemDeCobrancaDetalhe(
                        item.Id,
                        item.Tipo,
                        item.Descricao,
                        item.ValorEmCentavos,
                        item.NumeroDeParcelas,
                        item.DiaDeVencimento,
                        item.PrimeiroMes,
                        item.EncerradoEm,
                        emUso.Contains(item.Id)
                    )),
            ],
            emUso.Count == 0 ? 0 : await parcelaRepository.ContarVinculosComParcela(plano.Id, ct)
        );
    }
}
