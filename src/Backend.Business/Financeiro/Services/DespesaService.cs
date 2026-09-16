using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Financeiro.Services;

/// <summary>
/// O que a turma deve e o que ela já pagou.
/// </summary>
/// <remarks>
/// Parcelada gera N linhas, uma por vencimento, com o resto do centavo na primeira — a mesma divisão
/// da grade de cobrança da Sprint 6, e o mesmo ajuste de dia em mês curto
/// (<see cref="GradeDeParcelas.Vencimento"/>).
/// <para>
/// Pagar exige comprovante (decisão 3); corrigir uma paga é permitido à Tesouraria (decisão 5).
/// Nada aqui grava saldo: o caixa é agregação (decisão 1).
/// </para>
/// </remarks>
/// <param name="despesaRepository">Despesas da turma.</param>
/// <param name="fornecedorRepository">Fornecedores, para conferir o informado.</param>
/// <param name="arquivoService">Comprovantes.</param>
/// <param name="novaValidator">Forma do lançamento.</param>
/// <param name="dadosValidator">Forma da correção.</param>
/// <param name="pagamentoValidator">Forma do pagamento.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class DespesaService(
    IDespesaRepository despesaRepository,
    IFornecedorRepository fornecedorRepository,
    IArquivoService arquivoService,
    IValidator<NovaDespesa> novaValidator,
    IValidator<DadosDaDespesa> dadosValidator,
    IValidator<PagarDespesa> pagamentoValidator,
    IUnitOfWork unitOfWork,
    ILogger<DespesaService> logger
) : IDespesaService
{
    /// <summary>Categoria dos comprovantes de despesa no módulo de arquivos.</summary>
    public const string CategoriaDoComprovante = "comprovantes-despesa";

    /// <summary>
    /// Comprovante é PDF ou imagem.
    /// </summary>
    /// <remarks>
    /// Planilha e texto passam no módulo de arquivos, mas não comprovam pagamento — e comprovante sem
    /// restrição de tipo vira depósito de arquivos. Mesma lista do comprovante do formando (Sprint 9),
    /// repetida de propósito: são duas decisões que podem divergir, não uma regra em dois lugares.
    /// </remarks>
    private static readonly HashSet<string> ExtensoesDoComprovante = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
    };

    private static readonly Erro NaoEncontrada = Erro.NaoEncontrado("financeiro.despesa_nao_encontrada", "Despesa não encontrada.");

    private static readonly Erro SemComprovante = Erro.Validacao(
        "financeiro.comprovante_obrigatorio",
        "Anexe o comprovante do pagamento: é ele que sustenta a prestação de contas.",
        "comprovante"
    );

    /// <inheritdoc />
    public async Task<Result<PaginaDe<DespesaResumo>>> Listar(PaginacaoRequest paginacao, FiltroDeDespesas filtro, CancellationToken ct = default) =>
        Result.Ok(await despesaRepository.Listar(paginacao.Normalizar(), filtro, DataUtils.Hoje(), ct));

    /// <inheritdoc />
    public async Task<Result<ResumoDeDespesas>> Resumir(FiltroDeDespesas filtro, CancellationToken ct = default)
    {
        var contagens = await despesaRepository.Contar(filtro, DataUtils.Hoje(), ct);

        return new ResumoDeDespesas(
            Somar(contagens, _ => true),
            Somar(contagens, linha => linha.Status == StatusDaDespesa.Prevista),
            Somar(contagens, linha => linha is { Status: StatusDaDespesa.Prevista, Atrasada: true }),
            Somar(contagens, linha => linha.Status == StatusDaDespesa.Paga),
            Somar(contagens, linha => linha.Status == StatusDaDespesa.Cancelada)
        );
    }

    /// <inheritdoc />
    public async Task<Result<DespesaResumo>> ObterPorId(Guid id, CancellationToken ct = default) =>
        await despesaRepository.Obter(id, DataUtils.Hoje(), ct) is { } despesa ? despesa : NaoEncontrada;

    /// <inheritdoc />
    /// <remarks>
    /// Todas as linhas nascem com o mesmo <see cref="Despesa.LancamentoId"/>: é o que liga "2 de 3"
    /// às irmãs depois, mesmo que uma delas tenha fornecedor ou descrição corrigidos.
    /// <para>
    /// A duplicidade é conferida pela primeira linha: o clique repetido reenvia o lançamento inteiro,
    /// com o mesmo primeiro vencimento. O índice único do banco é a segunda barreira, para dois
    /// cliques que passem juntos pela conferência.
    /// </para>
    /// <para>
    /// O comprovante é gravado antes das despesas: se a gravação falhar depois, sobra um arquivo
    /// órfão — nunca uma despesa apontando para comprovante que não existe.
    /// </para>
    /// </remarks>
    public async Task<Result<IReadOnlyList<DespesaResumo>>> Lancar(
        NovaDespesa dados,
        Guid usuarioId,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    )
    {
        var validacao = novaValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<IReadOnlyList<DespesaResumo>>(validacao.Erros);

        if (dados.FornecedorId is { } fornecedorId && await fornecedorRepository.ObterCategoria(fornecedorId, ct) is null)
            return Erro.Validacao("financeiro.fornecedor_nao_encontrado", "Fornecedor não encontrado.", "fornecedor_id");

        if (dados.PagaEm is not null && comprovante is null)
            return SemComprovante;

        var linhas = Parcelar(dados);

        if (await despesaRepository.ExisteIgual(dados.FornecedorId, dados.Descricao.Trim(), linhas[0].Vencimento, ct))
            return Erro.Conflito(
                "financeiro.despesa_duplicada",
                "Esta despesa já foi lançada para este vencimento. Confira a lista antes de lançar de novo."
            );

        var arquivo = await EnviarComprovante(comprovante, usuarioId, ct);
        if (arquivo.Falhou)
            return Result.Falha<IReadOnlyList<DespesaResumo>>(arquivo.Erros);

        var lancamentoId = Guid.CreateVersion7();

        var despesas = linhas.Select(linha => Despesa.Nova(dados, lancamentoId, linha.Numero, linha.Vencimento, linha.ValorEmCentavos)).ToList();

        if (dados.PagaEm is { } pagoEm)
        {
            var pagamento = despesas[0].Pagar(pagoEm, arquivo.Valor!.Value);
            if (pagamento.Falhou)
                return Result.Falha<IReadOnlyList<DespesaResumo>>(pagamento.Erros);
        }

        await despesaRepository.Adicionar(despesas, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Despesa lançada em {Linhas} linha(s); primeira {DespesaId}.", despesas.Count, despesas[0].Id);

        var hoje = DataUtils.Hoje();
        var resumos = new List<DespesaResumo>(despesas.Count);

        foreach (var despesa in despesas)
            if (await despesaRepository.Obter(despesa.Id, hoje, ct) is { } resumo)
                resumos.Add(resumo);

        return Result.Ok<IReadOnlyList<DespesaResumo>>(resumos);
    }

    /// <inheritdoc />
    public async Task<Result<DespesaResumo>> Atualizar(Guid id, DadosDaDespesa dados, CancellationToken ct = default)
    {
        var validacao = dadosValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<DespesaResumo>(validacao.Erros);

        if (dados.FornecedorId is { } fornecedorId && await fornecedorRepository.ObterCategoria(fornecedorId, ct) is null)
            return Erro.Validacao("financeiro.fornecedor_nao_encontrado", "Fornecedor não encontrado.", "fornecedor_id");

        var despesa = await despesaRepository.ObterParaEdicao(id, ct);
        if (despesa is null)
            return NaoEncontrada;

        var alteracao = despesa.Aplicar(dados);
        if (alteracao.Falhou)
            return Result.Falha<DespesaResumo>(alteracao.Erros);

        await unitOfWork.SalvarAsync(ct);

        return await ObterPorId(id, ct);
    }

    /// <inheritdoc />
    /// <remarks>Sem comprovante, 400: é o anexo que sustenta a prestação de contas em assembleia (decisão 3).</remarks>
    public async Task<Result<DespesaResumo>> Pagar(
        Guid id,
        PagarDespesa dados,
        Guid usuarioId,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    )
    {
        var validacao = pagamentoValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<DespesaResumo>(validacao.Erros);

        if (comprovante is null)
            return SemComprovante;

        var despesa = await despesaRepository.ObterParaEdicao(id, ct);
        if (despesa is null)
            return NaoEncontrada;

        if (despesa.Status != StatusDaDespesa.Prevista)
            return Erro.Conflito("financeiro.despesa_nao_prevista", "Esta despesa não está prevista para pagamento.");

        var arquivo = await EnviarComprovante(comprovante, usuarioId, ct);
        if (arquivo.Falhou)
            return Result.Falha<DespesaResumo>(arquivo.Erros);

        var pagamento = despesa.Pagar(dados.PagoEm, arquivo.Valor!.Value);
        if (pagamento.Falhou)
            return Result.Falha<DespesaResumo>(pagamento.Erros);

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Despesa {DespesaId} paga por {UsuarioId}.", id, usuarioId);

        return await ObterPorId(id, ct);
    }

    /// <inheritdoc />
    public async Task<Result<DespesaResumo>> Cancelar(Guid id, CancellationToken ct = default)
    {
        var despesa = await despesaRepository.ObterParaEdicao(id, ct);
        if (despesa is null)
            return NaoEncontrada;

        var cancelamento = despesa.Cancelar();
        if (cancelamento.Falhou)
            return Result.Falha<DespesaResumo>(cancelamento.Erros);

        await unitOfWork.SalvarAsync(ct);

        return await ObterPorId(id, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pede o arquivo ao módulo de arquivos <b>como quem o enviou</b>: lá a regra é "cada um vê os
    /// próprios", e quem pode ver o comprovante da turma é decisão daqui — a política de Tesouraria já
    /// passou, e a despesa foi achada nesta formatura. O mesmo caminho do comprovante do formando.
    /// </remarks>
    public async Task<Result<ArquivoParaDownload>> BaixarComprovante(Guid id, CancellationToken ct = default)
    {
        var comprovante = await despesaRepository.ObterComprovante(id, ct);
        if (comprovante is null)
            return Erro.NaoEncontrado("financeiro.sem_comprovante", "Esta despesa não tem comprovante.");

        return await arquivoService.Baixar(
            comprovante.ArquivoId,
            new SolicitanteDeArquivo(comprovante.EnviadoPorUsuarioId, EhAdministrador: false),
            ct
        );
    }

    /// <summary>
    /// As linhas de um lançamento: uma à vista, N mensais na parcelada.
    /// </summary>
    /// <remarks>
    /// Divisão inteira em centavos, com o resto na primeira — a soma das linhas fecha com o total
    /// combinado, ao centavo. O dia do vencimento em mês curto cai no último dia dele, pela mesma
    /// regra da grade de cobrança.
    /// </remarks>
    /// <param name="dados">Lançamento já validado.</param>
    public static IReadOnlyList<ParcelaPrevista> Parcelar(NovaDespesa dados)
    {
        var basica = dados.ValorEmCentavos / dados.NumeroDeParcelas;
        var resto = dados.ValorEmCentavos % dados.NumeroDeParcelas;

        return
        [
            .. Enumerable
                .Range(1, dados.NumeroDeParcelas)
                .Select(numero => new ParcelaPrevista(
                    numero,
                    GradeDeParcelas.Vencimento(dados.Vencimento.AddMonths(numero - 1), dados.Vencimento.Day),
                    numero == 1 ? basica + resto : basica
                )),
        ];
    }

    private static SomaDeDespesas Somar(IReadOnlyList<ContagemDeDespesas> contagens, Func<ContagemDeDespesas, bool> filtro)
    {
        var linhas = contagens.Where(filtro).ToList();

        return linhas.Count == 0 ? SomaDeDespesas.Zero : new SomaDeDespesas(linhas.Sum(l => l.Quantidade), linhas.Sum(l => l.ValorEmCentavos));
    }

    /// <summary>Grava o comprovante, se veio; devolve o id do arquivo, ou nulo.</summary>
    private async Task<Result<Guid?>> EnviarComprovante(NovoArquivo? comprovante, Guid usuarioId, CancellationToken ct)
    {
        if (comprovante is null)
            return Result.Ok<Guid?>(null);

        if (!ExtensoesDoComprovante.Contains(Path.GetExtension(comprovante.Nome)))
            return Erro.Validacao("financeiro.comprovante_invalido", "Envie o comprovante em PDF ou imagem (PNG, JPG ou WebP).", "comprovante");

        var arquivo = await arquivoService.Enviar(comprovante with { Categoria = CategoriaDoComprovante }, usuarioId, ct);

        return arquivo.Falhou ? Result.Falha<Guid?>(arquivo.Erros) : arquivo.Valor.Id;
    }
}
