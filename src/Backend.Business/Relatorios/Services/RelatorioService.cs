using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Relatorios.Models;
using Backend.Business.Usuarios.Interfaces;

namespace Backend.Business.Relatorios.Services;

/// <summary>
/// O balancete, as exportações e a fila do PDF.
/// </summary>
/// <remarks>
/// A divisão de esforço é por formato: <see cref="Planilha"/> sai na própria requisição;
/// <see cref="Solicitar"/> grava o pedido do PDF e volta, e quem diagrama é o worker.
/// <para>
/// O saldo acumulado do balancete vem do <see cref="ICaixaService"/>, o mesmo do dashboard
/// (decisão 4) — é isso que faz os dois nunca discordarem, e há teste que compara os dois.
/// </para>
/// </remarks>
/// <param name="relatorioRepository">As agregações do período.</param>
/// <param name="solicitacaoRepository">A fila de relatórios pesados.</param>
/// <param name="caixaService">O saldo de hoje — a fonte única dos indicadores.</param>
/// <param name="formaturaRepository">Nome e instituição da turma, para a capa.</param>
/// <param name="usuarioRepository">Nome de quem emitiu, para a capa.</param>
/// <param name="arquivoService">Os bytes do PDF gerado.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class RelatorioService(
    IRelatorioRepository relatorioRepository,
    ISolicitacaoDeRelatorioRepository solicitacaoRepository,
    ICaixaService caixaService,
    IFormaturaRepository formaturaRepository,
    IUsuarioRepository usuarioRepository,
    IArquivoService arquivoService,
    IUnitOfWork unitOfWork
) : IRelatorioService
{
    /// <summary>Solicitações na tela de acompanhamento.</summary>
    public const int MaximoDeSolicitacoes = 20;

    /// <summary>Capa de um balancete cujo emissor já saiu da plataforma.</summary>
    private const string Anonimo = "Usuário removido";

    /// <inheritdoc />
    public async Task<Result<Balancete>> Balancete(
        Guid formaturaId,
        PeriodoDoRelatorio periodo,
        Guid emitidoPorUsuarioId,
        CancellationToken ct = default
    )
    {
        var formatura = await formaturaRepository.ObterDetalheDeTodasAsFormaturas(formaturaId, ct);

        if (formatura is null)
            return ErrosDeFormatura.FormaturaNaoEncontrada;

        var caixa = await caixaService.Consolidado(ct);

        if (caixa.Falhou)
            return Result.Falha<Balancete>(caixa.Erros);

        return new Balancete(
            formatura.Nome,
            $"{formatura.Curso} — {formatura.Instituicao}",
            periodo,
            (await usuarioRepository.ObterDetalhe(emitidoPorUsuarioId, ct))?.Nome ?? Anonimo,
            DateTime.UtcNow,
            await relatorioRepository.EntradasPorTipo(periodo, ct),
            await relatorioRepository.OutrasReceitasPorCategoria(periodo, ct),
            await relatorioRepository.SaidasPorCategoria(periodo, ct),
            [.. (await relatorioRepository.PorFornecedor(periodo, ct)).Select(Linha)],
            caixa.Valor.SaldoEmCentavos,
            await relatorioRepository.MovimentoPorMes(periodo, ct),
            await relatorioRepository.Totais(periodo.Anterior(), ct)
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// O balancete não aceita recorte: filtrado por fornecedor ou por formando ele deixa de fechar, e
    /// sai um papel que não bate com a cara do que bate.
    /// </remarks>
    public async Task<Result<TabelaDoRelatorio>> Tabela(
        Guid formaturaId,
        TipoDeRelatorio tipo,
        FiltroDoRelatorio filtro,
        CancellationToken ct = default
    )
    {
        var formatura = await formaturaRepository.ObterDetalheDeTodasAsFormaturas(formaturaId, ct);

        if (formatura is null)
            return ErrosDeFormatura.FormaturaNaoEncontrada;

        var recorte = tipo == TipoDeRelatorio.Balancete ? filtro.SomentePeriodo() : filtro;
        var subtitulo = await Subtitulo(formatura.Nome, recorte, ct);

        return tipo switch
        {
            TipoDeRelatorio.Despesas => await Despesas(subtitulo, recorte, ct),
            TipoDeRelatorio.Parcelas => await Parcelas(subtitulo, recorte, ct),
            TipoDeRelatorio.Fornecedores => await Fornecedores(subtitulo, recorte, ct),
            _ => await BalanceteEmTabela(subtitulo, recorte.Periodo, ct),
        };
    }

    /// <inheritdoc />
    public async Task<Result<ArquivoParaDownload>> Planilha(
        Guid formaturaId,
        TipoDeRelatorio tipo,
        FiltroDoRelatorio filtro,
        CancellationToken ct = default
    )
    {
        var tabela = await Tabela(formaturaId, tipo, filtro, ct);

        if (tabela.Falhou)
            return Result.Falha<ArquivoParaDownload>(tabela.Erros);

        return new ArquivoParaDownload(
            new MemoryStream(RelatorioEmExcel.Gerar(tabela.Valor)),
            $"{tipo.ToString().ToLowerInvariant()}-{filtro.Periodo.De:yyyy-MM-dd}-a-{filtro.Periodo.Ate:yyyy-MM-dd}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        );
    }

    /// <summary>
    /// A linha embaixo do título: turma, período e, quando há, o recorte por extenso.
    /// </summary>
    /// <remarks>
    /// O recorte <b>tem</b> que aparecer. Um arquivo que mostra parte das linhas sem dizer qual parte
    /// é um arquivo que mente, e este circula em assembleia e vai para o contador.
    /// </remarks>
    /// <param name="turma">Nome da formatura.</param>
    /// <param name="filtro">Recorte já resolvido para o tipo.</param>
    private async Task<string> Subtitulo(string turma, FiltroDoRelatorio filtro, CancellationToken ct)
    {
        var partes = new List<string> { turma, $"período de {Dia(filtro.Periodo.De)} a {Dia(filtro.Periodo.Ate)}" };

        if (!filtro.TemRecorte)
            return string.Join(" · ", partes);

        var nomes = await relatorioRepository.NomesDoFiltro(filtro, ct);

        if (nomes.Fornecedor is { } fornecedor)
            partes.Add($"fornecedor: {fornecedor}");

        if (filtro.Categoria is { } categoria)
            partes.Add($"categoria: {RotuloDaCategoria.De(categoria)}");

        if (filtro.SituacaoDaDespesa is { } situacaoDaDespesa)
            partes.Add($"situação: {RotuloDaDespesa(situacaoDaDespesa)}");

        if (nomes.Formando is { } formando)
            partes.Add($"formando: {formando}");

        if (nomes.Item is { } item)
            partes.Add($"item: {item}");

        if (filtro.SituacaoDaParcela is { } situacaoDaParcela)
            partes.Add($"situação: {RotuloDaParcela(situacaoDaParcela)}");

        return string.Join(" · ", partes);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O balancete não aceita recorte; gravar o filtro dele seria gravar uma promessa que a geração
    /// não cumpre, e dois pedidos iguais deixariam de deduplicar por causa dela.
    /// </remarks>
    public async Task<Result<SolicitacaoResumo>> Solicitar(
        TipoDeRelatorio tipo,
        FiltroDoRelatorio filtro,
        Guid solicitadaPorUsuarioId,
        CancellationToken ct = default
    )
    {
        var recorte = tipo == TipoDeRelatorio.Balancete ? filtro.SomentePeriodo() : filtro;
        var naFila = await solicitacaoRepository.ObterNaFila(tipo, recorte, ct);

        if (naFila is not null)
            return Resumir(naFila);

        var solicitacao = SolicitacaoDeRelatorio.Nova(tipo, recorte, solicitadaPorUsuarioId);

        await solicitacaoRepository.Adicionar(solicitacao, ct);
        await unitOfWork.SalvarAsync(ct);

        return Resumir(solicitacao);
    }

    /// <inheritdoc />
    public async Task<Result<OpcoesDeFiltro>> OpcoesDeFiltro(CancellationToken ct = default) =>
        Result.Ok(await relatorioRepository.OpcoesDeFiltro(ct));

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SolicitacaoResumo>>> ListarSolicitacoes(CancellationToken ct = default) =>
        Result.Ok<IReadOnlyList<SolicitacaoResumo>>([.. (await solicitacaoRepository.Listar(MaximoDeSolicitacoes, ct)).Select(Resumir)]);

    /// <inheritdoc />
    public async Task<Result<ArquivoParaDownload>> Baixar(Guid id, Guid solicitante, CancellationToken ct = default)
    {
        var solicitacao = await solicitacaoRepository.Obter(id, ct);

        if (solicitacao is null || !solicitacao.Disponivel(DateTime.UtcNow))
            return Erro.NaoEncontrado("relatorio.nao_disponivel", "Este relatório não está disponível para download.");

        return await arquivoService.Baixar(
            solicitacao.ArquivoId!.Value,
            new SolicitanteDeArquivo(solicitacao.SolicitadaPorUsuarioId, PeloSistema: false),
            ct
        );
    }

    /// <summary>A solicitação como a tela a acompanha.</summary>
    /// <param name="solicitacao">Pedido.</param>
    private static SolicitacaoResumo Resumir(SolicitacaoDeRelatorio solicitacao) =>
        new(
            solicitacao.Id,
            solicitacao.Tipo,
            solicitacao.De,
            solicitacao.Ate,
            solicitacao.Status,
            solicitacao.Motivo,
            solicitacao.ExpiraEm,
            solicitacao.CriadoEm
        );

    private static LinhaDeBalancete Linha(GastoPorFornecedor gasto) => new(gasto.Nome, gasto.Quantidade, gasto.PagoEmCentavos);

    /// <summary>
    /// O balancete em tabela: as mesmas quatro aberturas do PDF, uma embaixo da outra.
    /// </summary>
    /// <remarks>
    /// Uma planilha só, com uma coluna de bloco: o tesoureiro filtra por ela e tem os três quadros
    /// separados. Quatro arquivos seriam quatro downloads para responder uma pergunta.
    /// </remarks>
    private async Task<TabelaDoRelatorio> BalanceteEmTabela(string subtitulo, PeriodoDoRelatorio periodo, CancellationToken ct)
    {
        var linhas = new List<IReadOnlyList<Celula>>();

        foreach (var linha in await relatorioRepository.EntradasPorTipo(periodo, ct))
            linhas.Add([Celula.De("Entradas"), Celula.De(linha.Rotulo), Celula.Inteiro(linha.Quantidade), Celula.Reais(linha.ValorEmCentavos)]);

        foreach (var linha in await relatorioRepository.OutrasReceitasPorCategoria(periodo, ct))
            linhas.Add([
                Celula.De("Outras receitas"),
                Celula.De(linha.Rotulo),
                Celula.Inteiro(linha.Quantidade),
                Celula.Reais(linha.ValorEmCentavos),
            ]);

        foreach (var linha in await relatorioRepository.SaidasPorCategoria(periodo, ct))
            linhas.Add([
                Celula.De("Saídas por categoria"),
                Celula.De(linha.Rotulo),
                Celula.Inteiro(linha.Quantidade),
                Celula.Reais(linha.ValorEmCentavos),
            ]);

        foreach (var gasto in await relatorioRepository.PorFornecedor(periodo, ct))
            linhas.Add([
                Celula.De("Saídas por fornecedor"),
                Celula.De(gasto.Nome),
                Celula.Inteiro(gasto.Quantidade),
                Celula.Reais(gasto.PagoEmCentavos),
            ]);

        return new TabelaDoRelatorio(
            "Balancete",
            subtitulo,
            [new("Bloco", 28), new("Descrição", 40), new("Lançamentos", 15, Direita: true), new("Valor", 17, Direita: true)],
            linhas
        );
    }

    /// <summary>Uma linha por despesa lançada no período, dentro do recorte.</summary>
    private async Task<TabelaDoRelatorio> Despesas(string subtitulo, FiltroDoRelatorio filtro, CancellationToken ct)
    {
        var linhas = new List<IReadOnlyList<Celula>>();

        await foreach (var despesa in relatorioRepository.ListarDespesas(filtro, ct))
            linhas.Add([
                Celula.Data(despesa.Vencimento),
                Celula.Data(despesa.Competencia),
                Celula.De(despesa.Descricao),
                Celula.De(RotuloDaCategoria.De(despesa.Categoria)),
                Celula.De(despesa.Fornecedor),
                Celula.De(RotuloDaDespesa(despesa.Status)),
                Celula.Reais(despesa.ValorEmCentavos),
                Celula.Data(despesa.PagoEm),
            ]);

        return new TabelaDoRelatorio(
            "Despesas lançadas",
            subtitulo,
            [
                new("Vencimento", 9, Direita: true),
                new("Competência", 9, Direita: true),
                new("Descrição", 25),
                new("Categoria", 12),
                new("Fornecedor", 17),
                new("Situação", 9),
                new("Valor", 10, Direita: true),
                new("Pago em", 9, Direita: true),
            ],
            linhas
        );
    }

    /// <summary>
    /// Uma linha por parcela do período, dentro do recorte. Nomeia quem deve — é exportação de gestão.
    /// </summary>
    /// <remarks>
    /// A situação é peneirada <b>aqui</b>, e não no banco: "vencida" é uma aberta cujo vencimento
    /// passou, e quem sabe disso é <see cref="Parcela.StatusNoDia"/> — a mesma chamada que já monta a
    /// coluna. Repetir a regra em SQL seria a segunda cópia dela.
    /// </remarks>
    private async Task<TabelaDoRelatorio> Parcelas(string subtitulo, FiltroDoRelatorio filtro, CancellationToken ct)
    {
        var hoje = DataUtils.Hoje();
        var linhas = new List<IReadOnlyList<Celula>>();

        await foreach (var parcela in relatorioRepository.ListarParcelas(filtro, ct))
        {
            var situacao = Parcela.StatusNoDia(parcela.Status, parcela.Vencimento, hoje);

            if (filtro.SituacaoDaParcela is { } pedida && situacao != pedida)
                continue;

            linhas.Add([
                Celula.De(parcela.Nome),
                Celula.De(parcela.Item),
                Celula.De($"{parcela.Numero}/{parcela.De}"),
                Celula.Data(parcela.Vencimento),
                Celula.Reais(parcela.ValorOriginalEmCentavos),
                Celula.De(RotuloDaParcela(situacao)),
                Celula.Reais(parcela.ValorPagoEmCentavos),
                Celula.Data(parcela.PagoEm),
            ]);
        }

        return new TabelaDoRelatorio(
            "Parcelas e pagamentos",
            subtitulo,
            [
                new("Formando", 25),
                new("Item", 17),
                new("Parcela", 8, Direita: true),
                new("Vencimento", 9, Direita: true),
                new("Valor", 10, Direita: true),
                new("Situação", 10),
                new("Valor pago", 11, Direita: true),
                new("Pago em", 9, Direita: true),
            ],
            linhas
        );
    }

    /// <summary>
    /// Uma linha por fornecedor, com o que já saiu e o que ainda vai sair.
    /// </summary>
    /// <remarks>
    /// Este já é uma linha por fornecedor: escolher um deixaria o relatório com uma linha só. Quando
    /// há fornecedor no recorte, o que se peneira é a lista pronta — assim o subtítulo e o conteúdo
    /// continuam dizendo a mesma coisa.
    /// </remarks>
    private async Task<TabelaDoRelatorio> Fornecedores(string subtitulo, FiltroDoRelatorio filtro, CancellationToken ct)
    {
        var gastos = await relatorioRepository.PorFornecedor(filtro.Periodo, ct);

        if (filtro.FornecedorId is { } escolhido)
            gastos = [.. gastos.Where(gasto => gasto.FornecedorId == escolhido)];

        return new TabelaDoRelatorio(
            "Fornecedores",
            subtitulo,
            [new("Fornecedor", 43), new("Despesas", 15, Direita: true), new("Pago", 21, Direita: true), new("Previsto", 21, Direita: true)],
            [
                .. gastos.Select(gasto => new List<Celula>
                {
                    Celula.De(gasto.Nome),
                    Celula.Inteiro(gasto.Quantidade),
                    Celula.Reais(gasto.PagoEmCentavos),
                    Celula.Reais(gasto.PrevistoEmCentavos),
                }),
            ]
        );
    }

    /// <summary>O dia como o relatório o escreve.</summary>
    /// <param name="dia">Data.</param>
    private static string Dia(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>A situação da despesa como a tela a chama — "A pagar", e não "Prevista".</summary>
    /// <param name="status">Situação gravada.</param>
    private static string RotuloDaDespesa(StatusDaDespesa status) =>
        status switch
        {
            StatusDaDespesa.Prevista => "A pagar",
            StatusDaDespesa.Paga => "Paga",
            _ => "Cancelada",
        };

    /// <summary>A situação da parcela no dia, como a tela a chama.</summary>
    /// <param name="status">Situação já resolvida pelo dia de hoje.</param>
    private static string RotuloDaParcela(StatusDaParcela status) =>
        status switch
        {
            StatusDaParcela.Aberta => "Em aberto",
            StatusDaParcela.Vencida => "Vencida",
            StatusDaParcela.Paga => "Paga",
            _ => "Cancelada",
        };
}
