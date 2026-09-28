using System.Collections.Concurrent;
using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.MercadoPago.Interfaces;
using Backend.Business.MercadoPago.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infra;

/// <summary>
/// O Mercado Pago de mentira dos testes de integração: autoriza qualquer código, emite pedidos em memória
/// e diz que um pedido foi pago quando o teste manda.
/// </summary>
/// <remarks>
/// A assinatura do aviso é "válida" quando vale <see cref="AssinaturaValida"/>: a conta HMAC em si é do
/// teste unitário do cliente; aqui interessa o caminho do aviso até a parcela.
/// </remarks>
public sealed class MercadoPagoFalso : IMercadoPago
{
    /// <summary>O cabeçalho <c>x-signature</c> que o falso aceita.</summary>
    public const string AssinaturaValida = "ts=1,v1=valida";

    /// <summary>O token que a autorização devolve — o teste confere que ele nunca sai pela API.</summary>
    public const string AccessToken = "APP_USR-token-secreto-da-turma";

    /// <summary>A conta do próprio Kapa no Mercado Pago (<c>MercadoPago:ContaDoKapa</c>) — a turma é a 42.</summary>
    public const long ContaDoKapa = 900;

    /// <summary>A conta que "autorizou".</summary>
    public const string Conta = "turma@mp.testes";

    private readonly ConcurrentDictionary<string, (Guid Referencia, long Valor)> _pedidos = new();
    private readonly ConcurrentDictionary<string, bool> _pagos = new();

    /// <summary>Os pedidos emitidos, pelo id.</summary>
    public IReadOnlyDictionary<string, (Guid Referencia, long Valor)> Pedidos => _pedidos;

    /// <summary>Marca o pedido como pago — o que o formando faria no app do banco.</summary>
    public void Pagar(string idExterno) => _pagos[idExterno] = true;

    /// <summary>Uma fábrica com o Mercado Pago ligado e este falso no lugar da rede.</summary>
    public WebApplicationFactory<Program> Na(ApiFactory fabrica) =>
        fabrica.WithWebHostBuilder(host =>
            host.UseSetting("MercadoPago:ClientId", "app-de-teste")
                .UseSetting("MercadoPago:ClientSecret", "segredo-da-aplicacao")
                .UseSetting("MercadoPago:UrlDeRetorno", "https://localhost/api/v1/mercado-pago/retorno")
                .UseSetting("MercadoPago:ContaDoKapa", ContaDoKapa.ToString(CultureInfo.InvariantCulture))
                .ConfigureTestServices(servicos => servicos.Replace(ServiceDescriptor.Singleton<IMercadoPago>(this)))
        );

    /// <inheritdoc />
    public string UrlDeAutorizacao(string state) => $"https://auth.mercadopago.testes/authorization?state={Uri.EscapeDataString(state)}";

    /// <inheritdoc />
    public Task<Result<TokensDoMercadoPago>> Autorizar(string codigo, CancellationToken ct = default) =>
        Task.FromResult(Result.Ok(new TokensDoMercadoPago(AccessToken, "renovacao", 42, DateTime.UtcNow.AddDays(180))));

    /// <inheritdoc />
    public Task<Result<TokensDoMercadoPago>> Renovar(string refreshToken, CancellationToken ct = default) =>
        Task.FromResult(Result.Ok(new TokensDoMercadoPago(AccessToken, "renovacao-2", 42, DateTime.UtcNow.AddDays(180))));

    /// <inheritdoc />
    public Task<Result<ContaNoMercadoPago>> ConsultarConta(string accessToken, CancellationToken ct = default) =>
        Task.FromResult(Result.Ok(new ContaNoMercadoPago(42, Conta, true)));

    /// <inheritdoc />
    public Task<Result<DocumentoEmitido>> Emitir(string accessToken, PedidoDeCobranca pedido, CancellationToken ct = default)
    {
        var id = $"ORD{pedido.Referencia:N}".ToUpperInvariant();
        _pedidos[id] = (pedido.Referencia, pedido.ValorEmCentavos);

        return Task.FromResult(Result.Ok(new DocumentoEmitido(id, $"00020126-{id}")));
    }

    /// <summary>Os pedidos consultados, pelo id — o aviso da conta do Kapa não consulta nenhum.</summary>
    public IReadOnlyCollection<string> Consultados => _consultados.Keys.ToList();

    private readonly ConcurrentDictionary<string, bool> _consultados = new();

    /// <inheritdoc />
    public Task<Result<RecorrenciaNoMercadoPago>> CriarRecorrencia(string accessToken, PedidoDeRecorrencia pedido, CancellationToken ct = default) =>
        Task.FromResult(
            Result.Ok(
                new RecorrenciaNoMercadoPago(
                    $"PRE{pedido.Referencia:N}",
                    pedido.Referencia.ToString("N"),
                    SituacaoDaRecorrencia.Pendente,
                    $"https://mp.testes/preapproval/{pedido.Referencia:N}",
                    null
                )
            )
        );

    /// <inheritdoc />
    public Task<Result<RecorrenciaNoMercadoPago>> ConsultarRecorrencia(string accessToken, string idExterno, CancellationToken ct = default) =>
        Task.FromResult(Result.Ok(new RecorrenciaNoMercadoPago(idExterno, null, SituacaoDaRecorrencia.Pendente, null, null)));

    /// <inheritdoc />
    public Task<Result> CancelarRecorrencia(string accessToken, string idExterno, CancellationToken ct = default) => Task.FromResult(Result.Ok());

    /// <inheritdoc />
    public Task<Result<PedidoConsultado>> ConsultarPedido(string accessToken, string idExterno, CancellationToken ct = default)
    {
        _consultados[idExterno] = true;
        var (referencia, valor) = _pedidos[idExterno];
        var pago = _pagos.ContainsKey(idExterno);

        return Task.FromResult(
            Result.Ok(
                new PedidoConsultado(
                    idExterno,
                    referencia.ToString("N"),
                    pago ? SituacaoDoPedido.Pago : SituacaoDoPedido.Aberto,
                    pago ? valor : 0,
                    pago ? DateTime.UtcNow : null
                )
            )
        );
    }

    /// <inheritdoc />
    public bool AvisoAutentico(string? assinatura, string? idDaRequisicao, string? idDoRecurso) => assinatura == AssinaturaValida;
}
