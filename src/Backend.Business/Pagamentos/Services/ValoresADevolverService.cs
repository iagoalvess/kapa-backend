using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Arquivos.Services;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Pagamentos.Services;

/// <summary>
/// A lista "a devolver" do lado da tesouraria (Sprint 42): ver o que espera, registrar a devolução e fechar o pago sem
/// parcela.
/// </summary>
/// <remarks>
/// O Kapa registra, a comissão resolve (decisão 1): nada daqui chama o Mercado Pago. A devolução por PIX vira despesa
/// paga no dia do registro, com o comprovante — é a saída que faz o caixa fechar; o pago sem parcela nunca entrou no
/// caixa, e fechá-lo não lança nada.
/// </remarks>
/// <param name="repositorio">Valores a devolver.</param>
/// <param name="despesas">A saída da devolução no caixa.</param>
/// <param name="arquivoService">Comprovantes.</param>
/// <param name="eventos">Auditoria.</param>
/// <param name="fecharValidator">Forma do fechamento.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ValoresADevolverService(
    IValorADevolverRepository repositorio,
    IDespesaRepository despesas,
    IArquivoService arquivoService,
    IEventoRepository eventos,
    IValidator<FecharValorADevolver> fecharValidator,
    IUnitOfWork unitOfWork,
    ILogger<ValoresADevolverService> logger
) : IValoresADevolverService
{
    private static readonly Erro NaoEncontrado = Erro.NaoEncontrado("pagamento.valor_nao_encontrado", "Valor a devolver não encontrado.");

    /// <inheritdoc />
    public async Task<Result<PaginaDe<ValorADevolverNaLista>>> Listar(
        PaginacaoRequest paginacao,
        FiltroDeValoresADevolver filtro,
        CancellationToken ct = default
    ) => Result.Ok(await repositorio.Listar(paginacao.Normalizar(), filtro, ct));

    /// <inheritdoc />
    /// <remarks>
    /// O comprovante é gravado antes da trava, como na compra da loja; se o valor saiu da lista no meio do caminho, ele
    /// é apagado, e a resposta é a do valor.
    /// </remarks>
    public async Task<Result<ValorADevolverNaLista>> Devolver(Guid usuarioId, Guid id, NovoArquivo? comprovante, CancellationToken ct = default)
    {
        if (comprovante is null)
            return Erro.Validacao("pagamento.comprovante_obrigatorio", "Anexe o comprovante do PIX de volta.", "comprovante");

        if (await repositorio.Obter(id, ct) is not { } lido)
            return NaoEncontrado;

        if (lido.Status != StatusDoValorADevolver.ADevolver)
            return ValorADevolver.NaoADevolver;

        if (lido.Origem == OrigemDoValorADevolver.PagoSemParcela)
            return PagoSemParcelaNaoDevolve;

        var arquivo = await ComprovanteDePagamento.Enviar(arquivoService, comprovante, usuarioId, ct);
        if (arquivo.Falhou)
            return Result.Falha<ValorADevolverNaLista>(arquivo.Erros);

        var devolvido = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var valor = await repositorio.Travar(id, token);
                if (valor is null)
                    return Result.Falha(NaoEncontrado);

                var despesa = Despesa.DevolucaoAoFormando(Descricao(lido), valor.ValorEmCentavos, DataUtils.Hoje(), arquivo.Valor!.Value);
                var devolver = valor.Devolver(usuarioId, arquivo.Valor.Value, despesa.Id, DateTime.UtcNow);
                if (devolver.Falhou)
                    return devolver;

                await despesas.Adicionar([despesa], token);
                await eventos.Auditar(
                    NomesDeAuditoria.ValorDevolvido,
                    usuarioId,
                    new
                    {
                        formaturaId = valor.FormaturaId,
                        valorADevolverId = valor.Id,
                        valor.VinculoId,
                        valor.Origem,
                        valorEmCentavos = valor.ValorEmCentavos,
                        despesaId = despesa.Id,
                        comprovanteId = arquivo.Valor,
                    },
                    token
                );

                return Result.Ok();
            },
            ct
        );

        if (devolvido.Falhou)
        {
            await arquivoService.DescartarComprovante(arquivo.Valor, usuarioId, ct);
            return Result.Falha<ValorADevolverNaLista>(devolvido.Erros);
        }

        logger.LogInformation("Valor a devolver {Id} registrado devolvido por {UsuarioId}.", id, usuarioId);

        return await Reler(id, ct);
    }

    /// <inheritdoc />
    public async Task<Result<ValorADevolverNaLista>> Fechar(Guid usuarioId, Guid id, FecharValorADevolver dados, CancellationToken ct = default)
    {
        var validacao = fecharValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ValorADevolverNaLista>(validacao.Erros);

        var fechado = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var valor = await repositorio.Travar(id, token);
                if (valor is null)
                    return Result.Falha(NaoEncontrado);

                var fechar = valor.Fechar(usuarioId, dados.Observacao, DateTime.UtcNow);
                if (fechar.Falhou)
                    return fechar;

                await eventos.Auditar(
                    NomesDeAuditoria.PagoSemParcelaResolvido,
                    usuarioId,
                    new
                    {
                        formaturaId = valor.FormaturaId,
                        valorADevolverId = valor.Id,
                        valor.VinculoId,
                        valor.CobrancaId,
                        valorEmCentavos = valor.ValorEmCentavos,
                        observacao = valor.Observacao,
                    },
                    token
                );

                return Result.Ok();
            },
            ct
        );

        if (fechado.Falhou)
            return Result.Falha<ValorADevolverNaLista>(fechado.Erros);

        logger.LogInformation("Pago sem parcela {Id} fechado por {UsuarioId}.", id, usuarioId);

        return await Reler(id, ct);
    }

    private static readonly Erro PagoSemParcelaNaoDevolve = Erro.Conflito(
        "pagamento.pago_sem_parcela_nao_devolve",
        "Este dinheiro está na conta do Mercado Pago, não no caixa: devolva pelo painel dele ou lance como outra receita, e feche o aviso."
    );

    /// <summary>
    /// Como a despesa da devolução aparece no caixa: a quem, de onde e o fim do id — a descrição é única por turma e dia,
    /// e é o id que separa duas devoluções à mesma pessoa no mesmo dia.
    /// </summary>
    private static string Descricao(ValorADevolverNaLista valor)
    {
        var origem = valor.Origem == OrigemDoValorADevolver.CreditoDePedido ? "crédito de pedido cancelado" : "parcela cancelada";
        var sufixo = $" — {origem} {valor.Id.ToString("N")[^8..].ToUpperInvariant()}";
        var cabe = 200 - "Devolução a ".Length - sufixo.Length;

        return $"Devolução a {(valor.Nome.Length > cabe ? valor.Nome[..cabe] : valor.Nome)}{sufixo}";
    }

    private async Task<Result<ValorADevolverNaLista>> Reler(Guid id, CancellationToken ct) =>
        await repositorio.Obter(id, ct) is { } valor ? valor : NaoEncontrado;
}
