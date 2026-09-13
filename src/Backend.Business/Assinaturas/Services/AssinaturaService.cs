using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Settings;
using Backend.Business.Common;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Backend.Business.Assinaturas.Services;

/// <summary>
/// Catálogo de planos e o lado da comissão na assinatura.
/// </summary>
/// <param name="assinaturaRepository">Planos e assinaturas.</param>
/// <param name="formaturaRepository">A formatura, que muda de status no checkout.</param>
/// <param name="provedor">PSP que cobra a licença.</param>
/// <param name="checkoutValidator">Forma do pedido de checkout.</param>
/// <param name="settings">Configuração da assinatura.</param>
/// <param name="aplicacao">Endereço do front, para a URL de retorno.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class AssinaturaService(
    IAssinaturaRepository assinaturaRepository,
    IFormaturaRepository formaturaRepository,
    IProvedorDeAssinatura provedor,
    IValidator<IniciarCheckout> checkoutValidator,
    IOptions<AssinaturaSettings> settings,
    IOptions<AplicacaoSettings> aplicacao,
    IUnitOfWork unitOfWork
) : IAssinaturaService
{
    private static readonly Erro NaoEncontrada = Erro.NaoEncontrado("assinatura.nao_encontrada", "Esta formatura ainda não contratou um plano.");

    private string UrlDeRetorno => $"{aplicacao.Value.UrlDoFrontend.TrimEnd('/')}{settings.Value.CaminhoDeRetorno}";

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PlanoResumo>>> ListarPlanos(CancellationToken ct = default) =>
        Result.Ok(await assinaturaRepository.ListarPlanosAtivos(ct));

    /// <inheritdoc />
    public async Task<Result<AssinaturaDetalhe>> ObterAtual(CancellationToken ct = default) =>
        await assinaturaRepository.ObterDetalheDaMaisRecente(ct) is { } detalhe ? detalhe : NaoEncontrada;

    /// <inheritdoc />
    /// <remarks>
    /// A sessão é criada no provedor <b>antes</b> de gravar: provedor fora do ar devolve 503 e nada
    /// muda — a formatura continua em rascunho, sem assinatura órfã.
    /// <para>
    /// Uma pendente por formatura. Clicar de novo em "contratar" (fechou a aba, voltou pelo histórico)
    /// retoma a pendente com uma sessão nova, em vez de empilhar assinaturas. A referência enviada ao
    /// provedor é o id da assinatura, então o pagamento de qualquer uma das sessões encontra a mesma
    /// linha. O índice único parcial em <c>AssinaturaMapping</c> fecha a corrida do clique duplo.
    /// </para>
    /// <para>
    /// <b>Trocar de plano invalida a sessão anterior no provedor.</b> Como o pagamento de qualquer
    /// sessão confirma a mesma linha, pagar a sessão antiga do Essencial depois de pedir o Ampliado
    /// ativaria o Ampliado pelo preço do Essencial. Mesmo plano não precisa: qualquer sessão cobra o
    /// mesmo valor.
    /// </para>
    /// <para>
    /// Formatura suspensa contrata sem sair de <c>Suspensa</c>: ela continua em modo leitura até o
    /// pagamento confirmar — é o webhook que a reativa.
    /// </para>
    /// </remarks>
    public async Task<Result<SessaoDeCheckout>> IniciarCheckout(Guid formaturaId, IniciarCheckout dados, CancellationToken ct = default)
    {
        var validacao = checkoutValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<SessaoDeCheckout>(validacao.Erros);

        var formatura = await formaturaRepository.ObterParaEdicao(formaturaId, ct);

        if (formatura is null)
            return Erro.NaoEncontrado("formatura.nao_encontrada", "Formatura não encontrada.");

        if (formatura.Status == StatusDaFormatura.Ativa)
            return Erro.Conflito("assinatura.ja_ativa", "Esta formatura já tem uma assinatura ativa.");

        if (formatura.Status == StatusDaFormatura.Encerrada)
            return Erro.Conflito("formatura.encerrada", "Uma formatura encerrada não contrata assinatura.");

        var plano = await assinaturaRepository.ObterPlanoAtivo(dados.PlanoCodigo.Trim(), ct);

        if (plano is null)
            return Erro.Validacao("assinatura.plano_invalido", "Plano não encontrado.", campo: "planoCodigo");

        var pendente = await assinaturaRepository.ObterMaisRecenteParaEdicao(ct) is { Status: StatusDaAssinatura.Pendente } atual ? atual : null;

        if (pendente is { IdExterno: { } sessaoAnterior } && pendente.PlanoId != plano.Id)
        {
            var invalidada = await provedor.Cancelar(sessaoAnterior, ct);
            if (invalidada.Falhou)
                return Result.Falha<SessaoDeCheckout>(invalidada.Erros);
        }

        var assinatura = pendente ?? new Assinatura();
        assinatura.PlanoId = plano.Id;

        var sessao = await provedor.CriarCheckout(
            new PedidoDeCheckout(assinatura.Id, plano.Codigo, plano.Nome, plano.PrecoEmCentavos, plano.Ciclo, UrlDeRetorno),
            ct
        );

        if (sessao.Falhou)
            return sessao;

        assinatura.IdExterno = sessao.Valor.IdExterno;

        if (pendente is null)
            await assinaturaRepository.Adicionar(assinatura, ct);

        if (formatura.Status == StatusDaFormatura.Rascunho)
            formatura.Transicionar(StatusDaFormatura.AguardandoPagamento);

        await unitOfWork.SalvarAsync(ct);

        return sessao;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A regra da entidade vem antes do provedor: não se cancela no PSP o que aqui nem está ativo. Se
    /// a gravação falhar depois de o provedor cancelar, o <c>assinatura.cancelada</c> que ele manda
    /// em seguida acerta o estado.
    /// </remarks>
    public async Task<Result<AssinaturaDetalhe>> Cancelar(CancellationToken ct = default)
    {
        var assinatura = await assinaturaRepository.ObterMaisRecenteParaEdicao(ct);

        if (assinatura is null)
            return NaoEncontrada;

        var cancelamento = assinatura.Cancelar(DateTime.UtcNow);
        if (cancelamento.Falhou)
            return Result.Falha<AssinaturaDetalhe>(cancelamento.Erros);

        if (assinatura.IdExterno is { } idExterno)
        {
            var noProvedor = await provedor.Cancelar(idExterno, ct);
            if (noProvedor.Falhou)
                return Result.Falha<AssinaturaDetalhe>(noProvedor.Erros);
        }

        await unitOfWork.SalvarAsync(ct);

        return await ObterAtual(ct);
    }
}
