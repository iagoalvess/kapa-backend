using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Formandos.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Cobrancas.Services;

/// <summary>
/// O lançamento avulso no vínculo de um formando (Sprint 48, D23/D42).
/// </summary>
/// <remarks>
/// Um item <see cref="TipoDeCobranca.Avulsa"/> por lançamento, ligado ao vínculo (<see cref="ItemDeCobranca.VinculoDoLancamento"/>),
/// com a grade mensal a partir do primeiro vencimento — a mesma <see cref="GradeDeParcelas"/> de todo o resto. Exige a
/// adesão, como o pedido: sem ela não há regras de atraso aceitas para o valor do dia.
/// </remarks>
/// <param name="planos">Plano vigente e os lançamentos.</param>
/// <param name="parcelas">As parcelas do lançamento.</param>
/// <param name="perfis">O vínculo do formando.</param>
/// <param name="adesoes">Se o formando já aderiu.</param>
/// <param name="validator">Quem e o quê.</param>
/// <param name="itemValidator">Valor, parcelas e data, pela regra do item.</param>
/// <param name="eventos">Trilha de auditoria.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class LancamentoAvulsoService(
    IPlanoDeCobrancaRepository planos,
    IParcelaRepository parcelas,
    IPerfilRepository perfis,
    IAdesaoRepository adesoes,
    IValidator<LancamentoAvulso> validator,
    IValidator<DadosDoItem> itemValidator,
    IEventoRepository eventos,
    IUnitOfWork unitOfWork,
    ILogger<LancamentoAvulsoService> logger
) : ILancamentoAvulsoService
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<LancamentoResumo>>> Listar(CancellationToken ct = default) =>
        Result.Ok(await planos.ListarLancamentos(ct));

    /// <inheritdoc />
    /// <remarks>
    /// O primeiro vencimento não pode estar no passado: a parcela nasceria vencida, com multa e juros de um atraso que
    /// a pessoa não cometeu — a mesma regra do rateio.
    /// </remarks>
    public async Task<Result<LancamentoResumo>> Lancar(Guid formaturaId, LancamentoAvulso dados, Guid autorId, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<LancamentoResumo>(validacao.Erros);

        var item = new DadosDoItem(
            TipoDeCobranca.Avulsa,
            dados.Descricao,
            dados.ValorEmCentavos,
            dados.NumeroDeParcelas,
            dados.PrimeiroVencimento.Day,
            dados.PrimeiroVencimento
        );

        var formaDoItem = itemValidator.Validar(item);
        if (formaDoItem.Falhou)
            return Result.Falha<LancamentoResumo>(formaDoItem.Erros);

        if (dados.PrimeiroVencimento < DataUtils.Hoje())
            return Erro.Validacao(
                "cobranca.lancamento_retroativo",
                "O primeiro vencimento não pode estar no passado: a parcela nasceria vencida.",
                campo: "primeiro_vencimento"
            );

        if (await perfis.ObterMembro(formaturaId, dados.UsuarioId, ct) is not { } membro)
            return Erro.NaoEncontrado("cobranca.formando_nao_encontrado", "Formando não encontrado nesta turma.");

        if (!await adesoes.JaAderiuAlgumaVez(membro.VinculoId, ct))
            return Erro.Conflito(
                "cobranca.lancamento_sem_adesao",
                "Este formando ainda não aderiu ao termo: sem a adesão, não há regra de atraso para o lançamento."
            );

        if (await planos.ObterVigenteParaEdicao(ct) is not { } plano)
            return Erro.Conflito("cobranca.sem_plano_vigente", "A turma ainda não tem plano de cobrança em vigor.");

        var lancamento = ItemDeCobranca.NovoLancamento(plano.Id, membro.VinculoId, item);
        plano.Itens.Add(lancamento);

        await planos.AdicionarItem(lancamento, ct);
        await parcelas.Adicionar([.. GradeDeParcelas.Calcular(item).Select(prevista => Parcela.Nova(membro.VinculoId, lancamento.Id, prevista))], ct);

        await eventos.Auditar(
            NomesDeAuditoria.LancamentoAvulso,
            autorId,
            new
            {
                formaturaId,
                itemId = lancamento.Id,
                vinculoId = membro.VinculoId,
                lancamento.Descricao,
                valorEmCentavos = lancamento.ValorEmCentavos,
                lancamento.NumeroDeParcelas,
                primeiroVencimento = dados.PrimeiroVencimento,
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Lançamento avulso {ItemId} no vínculo {VinculoId}.", lancamento.Id, membro.VinculoId);

        return (await planos.ListarLancamentos(ct)).First(linha => linha.ItemDeCobrancaId == lancamento.Id);
    }
}
