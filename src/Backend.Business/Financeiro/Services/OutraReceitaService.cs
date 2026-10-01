using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Financeiro.Services;

/// <summary>
/// O dinheiro que entra na conta da turma sem ser parcela de formando (Sprint 28).
/// </summary>
/// <remarks>
/// O espelho do <see cref="DespesaService"/> (decisão 1), sem parcelamento: o comprovante é um documento
/// do acervo, escolhido pela tesouraria ou enviado junto do lançamento. Nada aqui grava saldo — o caixa,
/// o dashboard, o balancete e a meta da festa passam a ver a receita recebida porque somam a tabela,
/// não porque alguém os avisa.
/// <para>
/// Quem lança é a Tesouraria (P1, confirmada em 23/09/2026), e o autor fica na trilha pelo
/// <c>[RegistrarEvento]</c> do controller; cancelar grava também o retrato da receita.
/// </para>
/// </remarks>
/// <param name="outraReceitaRepository">Receitas da turma.</param>
/// <param name="documentoRepository">Acervo, para conferir o comprovante informado.</param>
/// <param name="documentoService">Acervo, para criar o comprovante enviado junto do lançamento.</param>
/// <param name="novaValidator">Forma do lançamento.</param>
/// <param name="dadosValidator">Forma da correção.</param>
/// <param name="recebimentoValidator">Forma do recebimento.</param>
/// <param name="eventos">Trilha de auditoria.</param>
/// <param name="formaturaAtual">Turma da sessão, para gravar o comprovante no acervo dela.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class OutraReceitaService(
    IOutraReceitaRepository outraReceitaRepository,
    IDocumentoRepository documentoRepository,
    IDocumentoService documentoService,
    IValidator<NovaOutraReceita> novaValidator,
    IValidator<DadosDaOutraReceita> dadosValidator,
    IValidator<ReceberOutraReceita> recebimentoValidator,
    IEventoRepository eventos,
    IFormaturaAtual formaturaAtual,
    IUnitOfWork unitOfWork,
    ILogger<OutraReceitaService> logger
) : IOutraReceitaService
{
    private static readonly Erro NaoEncontrada = Erro.NaoEncontrado("financeiro.outra_receita_nao_encontrada", "Receita não encontrada.");

    private static readonly Erro DocumentoNaoEncontrado = Erro.Validacao(
        "financeiro.documento_nao_encontrado",
        "Documento não encontrado no acervo, ou visível apenas para a comissão.",
        "documento_id"
    );

    private static readonly Erro DataFutura = Erro.Validacao(
        "financeiro.outra_receita_data_futura",
        "Receita recebida não pode ter data no futuro.",
        "data"
    );

    /// <inheritdoc />
    public async Task<Result<PaginaDe<OutraReceitaResumo>>> Listar(
        PaginacaoRequest paginacao,
        FiltroDeOutrasReceitas filtro,
        CancellationToken ct = default
    ) => Result.Ok(await outraReceitaRepository.Listar(paginacao.Normalizar(), filtro, DataUtils.Hoje(), ct));

    /// <inheritdoc />
    public async Task<Result<ResumoDeOutrasReceitas>> Resumir(FiltroDeOutrasReceitas filtro, CancellationToken ct = default)
    {
        var contagens = await outraReceitaRepository.Contar(filtro, DataUtils.Hoje(), ct);

        return new ResumoDeOutrasReceitas(
            Somar(contagens, _ => true),
            Somar(contagens, linha => linha.Status == StatusDaOutraReceita.Prevista),
            Somar(contagens, linha => linha is { Status: StatusDaOutraReceita.Prevista, Atrasada: true }),
            Somar(contagens, linha => linha.Status == StatusDaOutraReceita.Recebida),
            Somar(contagens, linha => linha.Status == StatusDaOutraReceita.Cancelada)
        );
    }

    /// <inheritdoc />
    public async Task<Result<OutraReceitaResumo>> ObterPorId(Guid id, CancellationToken ct = default) =>
        await outraReceitaRepository.Obter(id, DataUtils.Hoje(), ct) is { } outraReceita ? outraReceita : NaoEncontrada;

    /// <inheritdoc />
    /// <remarks>
    /// A duplicidade é conferida antes e o índice único do banco é a segunda barreira, para dois
    /// cliques que passem juntos pela conferência — o mesmo desenho da despesa. Se a tesouraria
    /// anexou o comprovante aqui, ele nasce no acervo (visível para a turma) e a receita aponta para
    /// ele; o arquivo é gravado antes da receita, como o comprovante da despesa (revisto em
    /// 01/10/2026: o comprovante continua no acervo, mas ninguém sai da tela para criá-lo).
    /// </remarks>
    public async Task<Result<OutraReceitaResumo>> Lancar(
        NovaOutraReceita dados,
        Guid usuarioId,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    )
    {
        var validacao = novaValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<OutraReceitaResumo>(validacao.Erros);

        var comComprovante = await ComComprovante(dados.DocumentoId, dados.Descricao, usuarioId, comprovante, ct);
        if (comComprovante.Falhou)
            return Result.Falha<OutraReceitaResumo>(comComprovante.Erros);

        var outraReceita = OutraReceita.Nova(dados with { DocumentoId = comComprovante.Valor });

        if (await outraReceitaRepository.ExisteIgual(outraReceita.Descricao, outraReceita.Origem, outraReceita.Data, ct))
            return Erro.Conflito(
                "financeiro.outra_receita_duplicada",
                "Esta receita já foi lançada para esta data. Confira a lista antes de lançar de novo."
            );

        await outraReceitaRepository.Adicionar(outraReceita, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Outra receita {OutraReceitaId} lançada como {Status}.", outraReceita.Id, outraReceita.Status);

        return await ObterPorId(outraReceita.Id, ct);
    }

    /// <inheritdoc />
    /// <remarks>Recebida com data no futuro é recusada: ela mudaria o mês do caixa em que o dinheiro já está.</remarks>
    public async Task<Result<OutraReceitaResumo>> Atualizar(
        Guid id,
        DadosDaOutraReceita dados,
        Guid usuarioId,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    )
    {
        var validacao = dadosValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<OutraReceitaResumo>(validacao.Erros);

        var comComprovante = await ComComprovante(dados.DocumentoId, dados.Descricao, usuarioId, comprovante, ct);
        if (comComprovante.Falhou)
            return Result.Falha<OutraReceitaResumo>(comComprovante.Erros);

        var outraReceita = await outraReceitaRepository.ObterParaEdicao(id, ct);
        if (outraReceita is null)
            return NaoEncontrada;

        if (outraReceita.Status == StatusDaOutraReceita.Recebida && dados.Data > DataUtils.Hoje())
            return DataFutura;

        var alteracao = outraReceita.Aplicar(dados with { DocumentoId = comComprovante.Valor });
        if (alteracao.Falhou)
            return Result.Falha<OutraReceitaResumo>(alteracao.Erros);

        await unitOfWork.SalvarAsync(ct);

        return await ObterPorId(id, ct);
    }

    /// <inheritdoc />
    public async Task<Result<OutraReceitaResumo>> Receber(Guid id, ReceberOutraReceita dados, CancellationToken ct = default)
    {
        var validacao = recebimentoValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<OutraReceitaResumo>(validacao.Erros);

        var outraReceita = await outraReceitaRepository.ObterParaEdicao(id, ct);
        if (outraReceita is null)
            return NaoEncontrada;

        var recebimento = outraReceita.Receber(dados.RecebidaEm);
        if (recebimento.Falhou)
            return Result.Falha<OutraReceitaResumo>(recebimento.Erros);

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Outra receita {OutraReceitaId} recebida em {RecebidaEm}.", id, dados.RecebidaEm);

        return await ObterPorId(id, ct);
    }

    /// <inheritdoc />
    public async Task<Result<OutraReceitaResumo>> Cancelar(Guid id, Guid autorId, CancellationToken ct = default)
    {
        var outraReceita = await outraReceitaRepository.ObterParaEdicao(id, ct);
        if (outraReceita is null)
            return NaoEncontrada;

        var cancelamento = outraReceita.Cancelar();
        if (cancelamento.Falhou)
            return Result.Falha<OutraReceitaResumo>(cancelamento.Erros);

        await eventos.Auditar(
            NomesDeAuditoria.OutraReceitaCancelada,
            autorId,
            new
            {
                formaturaId = outraReceita.FormaturaId,
                outraReceitaId = id,
                outraReceita.Descricao,
                outraReceita.Origem,
                outraReceita.Categoria,
                valorEmCentavos = outraReceita.ValorEmCentavos,
                outraReceita.Data,
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        return await ObterPorId(id, ct);
    }

    /// <summary>
    /// Se o documento informado não serve de comprovante.
    /// </summary>
    /// <remarks>
    /// A consulta vai com o papel de <see cref="PapelNaFormatura.Formando"/>, como o contrato do item
    /// da festa: a lista de receitas é lida pela turma inteira, e um comprovante que só a comissão
    /// abre seria um link quebrado para todo o resto.
    /// </remarks>
    /// <param name="documentoId">Documento informado, se houver.</param>
    private async Task<bool> DocumentoInvalido(Guid? documentoId, CancellationToken ct) =>
        documentoId is { } id && await documentoRepository.Obter(id, PapelNaFormatura.Formando, ct) is null;

    /// <summary>
    /// Resolve o comprovante: se veio arquivo, ele nasce no acervo (visível para a turma, categoria
    /// <see cref="CategoriaDeDocumento.Comprovante"/>) e a receita aponta para ele; senão, vale o
    /// documento informado, conferido contra a turma.
    /// </summary>
    /// <param name="documentoId">Documento escolhido no acervo, se houver.</param>
    /// <param name="titulo">Descrição da receita, que nomeia o documento enviado aqui.</param>
    /// <param name="usuarioId">Quem envia o comprovante.</param>
    /// <param name="comprovante">Arquivo anexado aqui, se houver.</param>
    private async Task<Result<Guid?>> ComComprovante(
        Guid? documentoId,
        string titulo,
        Guid usuarioId,
        NovoArquivo? comprovante,
        CancellationToken ct
    )
    {
        if (comprovante is null)
        {
            if (await DocumentoInvalido(documentoId, ct))
                return DocumentoNaoEncontrado;

            return Result.Ok(documentoId);
        }

        var documento = await documentoService.Enviar(
            FormaturaDaSessao(),
            usuarioId,
            new DadosDoDocumento(titulo.Trim(), CategoriaDeDocumento.Comprovante, Visibilidade.Turma),
            comprovante,
            ct
        );

        return documento.Falhou ? Result.Falha<Guid?>(documento.Erros) : documento.Valor.Id;
    }

    /// <summary>Turma da sessão, que abre o acervo onde o comprovante nasce. Ausente é escrita sem turma.</summary>
    private Guid FormaturaDaSessao() =>
        formaturaAtual.Id ?? throw new InvalidOperationException("Escrita de receita sem formatura selecionada na sessão.");

    private static SomaDeLancamentos Somar(IReadOnlyList<ContagemDeOutrasReceitas> contagens, Func<ContagemDeOutrasReceitas, bool> filtro)
    {
        var linhas = contagens.Where(filtro).ToList();

        return linhas.Count == 0 ? SomaDeLancamentos.Zero : new SomaDeLancamentos(linhas.Sum(l => l.Quantidade), linhas.Sum(l => l.ValorEmCentavos));
    }
}
